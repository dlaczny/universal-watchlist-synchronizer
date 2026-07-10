"""Client for consuming the watchlist-app backend API."""

from __future__ import annotations

from typing import Any

import httpx
import structlog

logger = structlog.get_logger(__name__)


class WatchlistAppError(Exception):
    """Raised when watchlist-app API data cannot be consumed safely."""


class WatchlistAppClient:
    """Small API client for the .NET watchlist-app backend."""

    def __init__(
        self,
        base_url: str,
        http_client: httpx.Client | None = None,
        timeout_seconds: int = 30,
    ):
        self.base_url = base_url.rstrip("/")
        self.http_client = http_client or httpx.Client(timeout=timeout_seconds)

    def fetch_radarr_movie_export(self, sync_first: bool = False) -> list[dict[str, Any]]:
        """Fetch Radarr export movies and map them into workflow watchlist entries."""
        if sync_first:
            self._sync_all()

        response = self.http_client.get(f"{self.base_url}/api/export/radarr/movies")
        try:
            response.raise_for_status()
        except httpx.HTTPStatusError as e:
            raise WatchlistAppError(
                f"watchlist-app Radarr export failed: HTTP {response.status_code}"
            ) from e

        payload = response.json()
        if not isinstance(payload, list):
            raise WatchlistAppError("watchlist-app Radarr export returned non-list JSON")

        movies = [self._map_export_item(item) for item in payload]
        logger.info("watchlist_app_export_fetched", count=len(movies))
        return movies

    def _sync_all(self) -> None:
        response = self.http_client.post(f"{self.base_url}/api/sync/all")
        try:
            response.raise_for_status()
        except httpx.HTTPStatusError as e:
            raise WatchlistAppError(
                f"watchlist-app sync failed: HTTP {response.status_code}"
            ) from e
        logger.info("watchlist_app_sync_completed")

    @staticmethod
    def _map_export_item(item: Any) -> dict[str, Any]:
        if not isinstance(item, dict):
            raise WatchlistAppError("watchlist-app Radarr export item is not an object")

        title = item.get("title")
        release_year = item.get("release_year")
        watchlist_app_id = item.get("id")

        if not title or watchlist_app_id is None:
            raise WatchlistAppError("watchlist-app Radarr export item missing id/title")
        try:
            tmdb_id = int(watchlist_app_id)
        except (TypeError, ValueError) as e:
            raise WatchlistAppError(
                f"watchlist-app Radarr export item has invalid id: {watchlist_app_id}"
            ) from e
        if tmdb_id <= 0:
            raise WatchlistAppError(
                f"watchlist-app Radarr export item has invalid id: {watchlist_app_id}"
            )

        year = None
        if release_year not in (None, ""):
            try:
                year = int(release_year)
            except (TypeError, ValueError) as e:
                raise WatchlistAppError(
                    f"watchlist-app Radarr export item has invalid release_year: {release_year}"
                ) from e

        return {
            "title": title,
            "year": year,
            "tmdb_id": tmdb_id,
            "imdb_id": item.get("imdb_id") or None,
            "letterboxd_id": item.get("clean_title") or None,
            "watchlist_app_id": watchlist_app_id,
        }
