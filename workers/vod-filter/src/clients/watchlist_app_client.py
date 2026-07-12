"""Client for consuming the watchlist-app backend API."""

from __future__ import annotations

from datetime import datetime
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
        sync_timeout_seconds: int = 900,
        sync_key: str | None = None,
    ):
        self.base_url = base_url.rstrip("/")
        self.http_client = http_client or httpx.Client(timeout=timeout_seconds)
        self.sync_timeout_seconds = sync_timeout_seconds
        self.sync_key = sync_key

    def fetch_radarr_movie_export(self, sync_first: bool = False) -> list[dict[str, Any]]:
        """Fetch Radarr export movies and map them into workflow watchlist entries."""
        if sync_first:
            self._sync_movies()

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

    def fetch_movie_watchlist(
        self,
        sync_first: bool = False,
        include_plex_only: bool = False,
    ) -> list[dict[str, Any]]:
        """Fetch backend movie watchlist rows for reconciliation."""
        if sync_first:
            self._sync_movies()

        response = self.http_client.get(
            f"{self.base_url}/api/watchlist",
            params={
                "collection": "movie",
                "availability": "plex,not_on_plex,unreleased,unknown_match",
                "sort": "title_asc",
            },
        )
        try:
            response.raise_for_status()
        except httpx.HTTPStatusError as e:
            raise WatchlistAppError(
                f"watchlist-app movie watchlist failed: HTTP {response.status_code}"
            ) from e

        payload = response.json()
        if not isinstance(payload, list):
            raise WatchlistAppError("watchlist-app movie watchlist returned non-list JSON")

        movies = []
        for item in payload:
            mapped = self._map_watchlist_item(item)
            if (
                not include_plex_only
                and mapped["source"] == "plex"
                and mapped["library_membership"] == "plex_only"
            ):
                continue
            movies.append(mapped)

        logger.info("watchlist_app_movie_watchlist_fetched", count=len(movies))
        return movies

    def fetch_movie_sync_snapshot(self, sync_first: bool = False) -> dict[str, Any]:
        """Fetch and strictly map the complete backend movie worker snapshot."""
        if sync_first:
            self._sync_movies()

        response = self.http_client.get(
            f"{self.base_url}/api/export/movies/sync-state"
        )
        try:
            response.raise_for_status()
        except httpx.HTTPStatusError as e:
            raise WatchlistAppError(
                f"watchlist-app movie sync snapshot failed: HTTP {response.status_code}"
            ) from e

        try:
            payload = response.json()
        except ValueError as e:
            raise WatchlistAppError(
                "watchlist-app movie sync snapshot returned invalid JSON"
            ) from e

        if not isinstance(payload, dict) or not isinstance(payload.get("movies"), list):
            raise WatchlistAppError(
                "watchlist-app movie sync snapshot returned invalid shape"
            )

        generated_at = self._parse_datetime(payload.get("generatedAt"), "generatedAt")
        last_sync_value = payload.get("lastSuccessfulMovieSyncAt")
        last_successful_sync_at = (
            self._parse_datetime(last_sync_value, "lastSuccessfulMovieSyncAt")
            if last_sync_value is not None
            else None
        )

        return {
            "generated_at": generated_at,
            "last_successful_movie_sync_at": last_successful_sync_at,
            "movies": [self._map_sync_snapshot_item(item) for item in payload["movies"]],
        }

    def _sync_movies(self) -> None:
        headers = (
            {"X-Watchlist-Sync-Key": self.sync_key}
            if self.sync_key
            else None
        )
        response = self.http_client.post(
            f"{self.base_url}/api/sync/movies",
            headers=headers,
            timeout=self.sync_timeout_seconds,
        )
        try:
            response.raise_for_status()
        except httpx.HTTPStatusError as e:
            raise WatchlistAppError(
                f"watchlist-app sync failed: HTTP {response.status_code}"
            ) from e
        logger.info("watchlist_app_sync_completed")

    @staticmethod
    def _parse_datetime(value: Any, field: str) -> datetime:
        if not isinstance(value, str) or not value.strip():
            raise WatchlistAppError(
                f"watchlist-app movie sync snapshot has invalid {field}"
            )
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        except ValueError as e:
            raise WatchlistAppError(
                f"watchlist-app movie sync snapshot has invalid {field}"
            ) from e
        if parsed.tzinfo is None:
            raise WatchlistAppError(
                f"watchlist-app movie sync snapshot has timezone-free {field}"
            )
        return parsed

    @staticmethod
    def _map_sync_snapshot_item(item: Any) -> dict[str, Any]:
        if not isinstance(item, dict):
            raise WatchlistAppError("watchlist-app movie snapshot item is not an object")

        title = item.get("title")
        source_id = item.get("sourceId")
        metadata_status = item.get("metadataStatus")
        availability_status = item.get("availabilityStatus")
        eligibility_reason = item.get("radarrEligibilityReason")
        if not all(
            isinstance(value, str) and value.strip()
            for value in (
                title,
                source_id,
                metadata_status,
                availability_status,
                eligibility_reason,
            )
        ):
            raise WatchlistAppError(
                "watchlist-app movie snapshot item is missing required text fields"
            )

        tmdb_id = item.get("tmdbId")
        if tmdb_id is not None and (
            isinstance(tmdb_id, bool)
            or not isinstance(tmdb_id, int)
            or tmdb_id <= 0
        ):
            raise WatchlistAppError("watchlist-app movie snapshot item has invalid tmdbId")

        year = item.get("year")
        if year is not None and (
            isinstance(year, bool) or not isinstance(year, int)
        ):
            raise WatchlistAppError("watchlist-app movie snapshot item has invalid year")

        owned = item.get("ownedServiceAvailability")
        if not isinstance(owned, list) or not all(isinstance(value, str) for value in owned):
            raise WatchlistAppError(
                "watchlist-app movie snapshot item has invalid ownedServiceAvailability"
            )

        radarr_eligible = item.get("radarrEligible")
        if not isinstance(radarr_eligible, bool):
            raise WatchlistAppError(
                "watchlist-app movie snapshot item has invalid radarrEligible"
            )

        return {
            "tmdb_id": tmdb_id,
            "imdb_id": item.get("imdbId") or None,
            "title": title,
            "year": year,
            "source_id": source_id,
            "metadata_status": metadata_status,
            "availability_status": availability_status,
            "owned_service_availability": owned,
            "radarr_eligible": radarr_eligible,
            "radarr_eligibility_reason": eligibility_reason,
        }

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

    @staticmethod
    def _map_watchlist_item(item: Any) -> dict[str, Any]:
        if not isinstance(item, dict):
            raise WatchlistAppError("watchlist-app watchlist item is not an object")

        title = item.get("title")
        watchlist_app_id = item.get("id")
        source = item.get("source")
        source_id = item.get("sourceId")

        if not title or watchlist_app_id is None or not source:
            raise WatchlistAppError("watchlist-app watchlist item missing id/title/source")

        tmdb_id = None
        if source != "plex" and source_id not in (None, ""):
            try:
                parsed_source_id = int(source_id)
            except (TypeError, ValueError):
                parsed_source_id = None
            if parsed_source_id and parsed_source_id > 0:
                tmdb_id = parsed_source_id

        year = None
        release_year = item.get("year")
        if release_year not in (None, ""):
            try:
                year = int(release_year)
            except (TypeError, ValueError) as e:
                raise WatchlistAppError(
                    f"watchlist-app watchlist item has invalid year: {release_year}"
                ) from e

        owned_service_availability = item.get("ownedServiceAvailability") or []
        if not isinstance(owned_service_availability, list):
            raise WatchlistAppError(
                "watchlist-app watchlist item has invalid ownedServiceAvailability"
            )

        return {
            "title": title,
            "year": year,
            "tmdb_id": tmdb_id,
            "imdb_id": item.get("imdb_id") or item.get("imdbId") or None,
            "letterboxd_id": watchlist_app_id,
            "watchlist_app_id": watchlist_app_id,
            "source": source,
            "source_id": source_id,
            "availability_status": item.get("availabilityStatus") or None,
            "library_membership": item.get("libraryMembership") or None,
            "owned_service_availability": owned_service_availability,
        }
