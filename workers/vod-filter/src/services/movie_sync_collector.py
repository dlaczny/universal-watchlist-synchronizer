"""Read all boundary state required to create one movie sync plan."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Any


@dataclass(frozen=True)
class CollectedMovieSyncState:
    backend_movies: tuple[dict[str, Any], ...]
    source_snapshot_at: datetime | None
    source_last_successful_sync_at: datetime | None
    radarr_movies: tuple[dict[str, Any], ...]
    plex_watchlist_movies: tuple[dict[str, Any], ...]
    plex_library_movies: tuple[dict[str, Any], ...]
    managed_destinations: tuple[dict[str, Any], ...]
    collection_errors: tuple[str, ...]


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
            "movies": [],
            "generated_at": None,
            "last_successful_movie_sync_at": None,
        }

        try:
            backend_snapshot = self.backend_client.fetch_movie_sync_snapshot(
                sync_first=sync_first
            )
        except Exception as error:
            errors.append(f"backend_snapshot: {error}")

        radarr_movies = self._collect(
            "radarr",
            self.radarr_client.get_all_movies,
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

        return CollectedMovieSyncState(
            backend_movies=tuple(backend_snapshot.get("movies") or []),
            source_snapshot_at=backend_snapshot.get("generated_at"),
            source_last_successful_sync_at=backend_snapshot.get(
                "last_successful_movie_sync_at"
            ),
            radarr_movies=tuple(radarr_movies),
            plex_watchlist_movies=tuple(plex_watchlist),
            plex_library_movies=tuple(plex_library),
            managed_destinations=tuple(managed),
            collection_errors=tuple(errors),
        )

    @staticmethod
    def _collect(name: str, operation, errors: list[str]) -> list[dict[str, Any]]:
        try:
            return list(operation())
        except Exception as error:
            errors.append(f"{name}: {error}")
            return []
