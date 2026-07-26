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
