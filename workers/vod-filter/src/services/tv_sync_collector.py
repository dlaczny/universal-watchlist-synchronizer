"""Read one TV generation and all destination state without mutation."""

from __future__ import annotations

from src.models.tv_destination import TvCollectedState, TvOwnership


class TvSyncCollector:
    def __init__(self, *, backend_client, sonarr_client, plex_client, state_store, plex_library_name: str) -> None:
        self.backend_client = backend_client
        self.sonarr_client = sonarr_client
        self.plex_client = plex_client
        self.state_store = state_store
        self.plex_library_name = plex_library_name

    def collect(self) -> TvCollectedState:
        errors: list[str] = []
        snapshot = self._read("backend_snapshot", self.backend_client.fetch_tv_sync_snapshot, errors, None)
        sonarr = self._read("sonarr", self.sonarr_client.get_all_series, errors, ())
        sonarr_episode_ids = self._collect_sonarr_episode_ids(sonarr, errors)
        watchlist = self._read("plex_watchlist", self.plex_client.get_watchlist_shows, errors, ())
        library = self._read(
            "plex_library",
            lambda: self.plex_client.get_library_show_identities(self.plex_library_name),
            errors,
            frozenset(),
        )
        ownership_rows = self._read("worker_ownership", self.state_store.get_ownership, errors, ())
        ownership: list[TvOwnership] = []
        try:
            ownership = [
                TvOwnership(row["destination"], row["tvdb_id"], row["origin"])
                for row in ownership_rows
            ]
        except Exception as error:
            errors.append(f"worker_ownership: {error}")

        return TvCollectedState(
            snapshot=snapshot,
            sonarr_series=tuple(sonarr),
            sonarr_episode_ids_by_tvdb=sonarr_episode_ids,
            plex_watchlist=tuple(watchlist),
            plex_library_identities=frozenset(library),
            ownership=tuple(ownership),
            collection_errors=tuple(errors),
        )

    @staticmethod
    def _read(name: str, operation, errors: list[str], fallback):
        try:
            return operation()
        except Exception as error:
            errors.append(f"{name}: {error}")
            return fallback

    def _collect_sonarr_episode_ids(self, series_rows, errors: list[str]) -> tuple[tuple[int, int, int], ...]:
        mappings: list[tuple[int, int, int]] = []
        for series in series_rows:
            tvdb_id = getattr(series, "tvdb_id", None)
            if not isinstance(tvdb_id, int) or isinstance(tvdb_id, bool) or tvdb_id <= 0:
                continue
            episode_ids = self._read(
                f"sonarr_episodes:{tvdb_id}",
                lambda: self.sonarr_client.get_episode_ids_by_tvdb(series),
                errors,
                {},
            )
            try:
                for episode_tvdb_id, sonarr_episode_id in episode_ids.items():
                    if (
                        not isinstance(episode_tvdb_id, int)
                        or isinstance(episode_tvdb_id, bool)
                        or episode_tvdb_id <= 0
                        or not isinstance(sonarr_episode_id, int)
                        or isinstance(sonarr_episode_id, bool)
                        or sonarr_episode_id <= 0
                    ):
                        raise ValueError("episode identity mapping is invalid")
                    mappings.append((tvdb_id, episode_tvdb_id, sonarr_episode_id))
            except Exception as error:
                errors.append(f"sonarr_episodes:{tvdb_id}: {error}")
        return tuple(sorted(mappings))
