"""Read all boundary state required to create one movie sync plan."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Any


@dataclass(frozen=True)
class CollectedMovieSyncState:
    backend_movies: tuple[dict[str, Any], ...]
    backend_watched_movies: tuple[dict[str, Any], ...]
    source_snapshot_id: str | None
    source_snapshot_at: datetime | None
    source_last_successful_sync_at: datetime | None
    radarr_movies: tuple[dict[str, Any], ...]
    radarr_observations: tuple[dict[str, Any], ...]
    plex_watchlist_movies: tuple[dict[str, Any], ...]
    plex_library_movies: tuple[dict[str, Any], ...]
    managed_destinations: tuple[dict[str, Any], ...]
    collection_errors: tuple[str, ...]
    radarr_exclusions: tuple[dict[str, Any], ...] = ()


class MovieSyncCollector:
    """Collect backend, destination, and ownership state without mutation."""

    def __init__(
        self,
        *,
        backend_client,
        radarr_client,
        plex_client,
        cache_service,
        library_name: str,
    ):
        self.backend_client = backend_client
        self.radarr_client = radarr_client
        self.plex_client = plex_client
        self.cache_service = cache_service
        self.library_name = library_name

    def collect(self, *, sync_first: bool) -> CollectedMovieSyncState:
        errors: list[str] = []
        backend_snapshot: dict[str, Any] = {
            "source_snapshot_id": None,
            "movies": [],
            "watched_movies": [],
            "generated_at": None,
            "last_successful_movie_sync_at": None,
        }
        backend_succeeded = False

        try:
            backend_snapshot = self.backend_client.fetch_movie_sync_snapshot(
                sync_first=sync_first
            )
            backend_succeeded = True
        except Exception as error:
            errors.append(f"backend_snapshot: {error}")

        radarr_movies, radarr_succeeded = self._collect_with_status(
            "radarr",
            self.radarr_client.get_all_movies,
            errors,
        )
        radarr_exclusions = self._collect(
            "radarr_exclusions",
            self.radarr_client.get_exclusions,
            errors,
        )
        plex_watchlist = self._collect(
            "plex_watchlist",
            self.plex_client.get_watchlist,
            errors,
        )
        plex_library = self._collect(
            "plex_library",
            lambda: self.plex_client.get_library_movies(self.library_name),
            errors,
        )
        managed = self._collect(
            "worker_ownership",
            self.cache_service.get_managed_destinations,
            errors,
        )
        radarr_observations: list[dict[str, Any]] = []
        if backend_succeeded and radarr_succeeded:
            try:
                backend_movies = list(backend_snapshot.get("movies") or [])
                watched_movies = list(
                    backend_snapshot.get("watched_movies") or []
                )
                active_tmdb_ids = {
                    tmdb_id
                    for movie in backend_movies
                    if (tmdb_id := self._positive_tmdb_id(movie.get("tmdb_id")))
                    is not None
                }
                watched_events_by_tmdb = {
                    tmdb_id: str(movie["lifecycle_event_id"])
                    for movie in watched_movies
                    if (tmdb_id := self._positive_tmdb_id(movie.get("tmdb_id")))
                    is not None
                    and movie.get("lifecycle_event_id")
                }
                radarr_observations = list(
                    self.cache_service.observe_radarr_movies(
                        radarr_movies,
                        active_tmdb_ids,
                        watched_events_by_tmdb,
                    )
                )
            except Exception as error:
                errors.append(f"radarr_observations: {error}")

        return CollectedMovieSyncState(
            backend_movies=tuple(backend_snapshot.get("movies") or []),
            backend_watched_movies=tuple(
                backend_snapshot.get("watched_movies") or []
            ),
            source_snapshot_id=backend_snapshot.get("source_snapshot_id"),
            source_snapshot_at=backend_snapshot.get("generated_at"),
            source_last_successful_sync_at=backend_snapshot.get(
                "last_successful_movie_sync_at"
            ),
            radarr_movies=tuple(radarr_movies),
            radarr_observations=tuple(radarr_observations),
            plex_watchlist_movies=tuple(plex_watchlist),
            plex_library_movies=tuple(plex_library),
            managed_destinations=tuple(managed),
            collection_errors=tuple(errors),
            radarr_exclusions=tuple(radarr_exclusions),
        )

    @staticmethod
    def _collect(name: str, operation, errors: list[str]) -> list[dict[str, Any]]:
        values, _ = MovieSyncCollector._collect_with_status(name, operation, errors)
        return values

    @staticmethod
    def _collect_with_status(
        name: str,
        operation,
        errors: list[str],
    ) -> tuple[list[dict[str, Any]], bool]:
        try:
            return list(operation()), True
        except Exception as error:
            errors.append(f"{name}: {error}")
            return [], False

    @staticmethod
    def _positive_tmdb_id(value: Any) -> int | None:
        if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
            return None
        return value
