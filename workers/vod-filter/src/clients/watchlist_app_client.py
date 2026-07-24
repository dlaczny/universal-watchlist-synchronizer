"""Client for consuming the watchlist-app backend API."""

from __future__ import annotations

from datetime import datetime, timedelta
from typing import Any

import httpx
import structlog

from src.models.tv_sync import TvAvailability, TvEpisode, TvSeason, TvShow, TvSnapshot

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
        expected_snapshot_id = self._sync_movies() if sync_first else None

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

        if (
            not isinstance(payload, dict)
            or not isinstance(payload.get("movies"), list)
            or not isinstance(payload.get("watchedMovies"), list)
        ):
            raise WatchlistAppError(
                "watchlist-app movie sync snapshot returned invalid shape"
            )

        source_snapshot_id = payload.get("sourceSnapshotId")
        if not isinstance(source_snapshot_id, str) or not source_snapshot_id.strip():
            raise WatchlistAppError(
                "watchlist-app movie sync snapshot has invalid sourceSnapshotId"
            )
        if (
            expected_snapshot_id is not None
            and source_snapshot_id != expected_snapshot_id
        ):
            raise WatchlistAppError(
                "watchlist-app movie sync snapshot ID mismatch: "
                f"sync published {expected_snapshot_id}, export returned {source_snapshot_id}"
            )

        generated_at = self._parse_datetime(payload.get("generatedAt"), "generatedAt")
        last_sync_value = payload.get("lastSuccessfulMovieSyncAt")
        last_successful_sync_at = (
            self._parse_datetime(last_sync_value, "lastSuccessfulMovieSyncAt")
            if last_sync_value is not None
            else None
        )

        watched_movies = [
            self._map_watched_snapshot_item(item)
            for item in payload["watchedMovies"]
        ]
        lifecycle_event_ids = [
            movie["lifecycle_event_id"] for movie in watched_movies
        ]
        if len(lifecycle_event_ids) != len(set(lifecycle_event_ids)):
            raise WatchlistAppError(
                "watchlist-app movie sync snapshot has duplicate lifecycleEventId"
            )

        return {
            "source_snapshot_id": source_snapshot_id,
            "generated_at": generated_at,
            "last_successful_movie_sync_at": last_successful_sync_at,
            "movies": [self._map_sync_snapshot_item(item) for item in payload["movies"]],
            "watched_movies": watched_movies,
        }

    def fetch_tv_sync_snapshot(self) -> TvSnapshot:
        """Fetch the read-only TV snapshot without accepting destination authority."""
        response = self.http_client.get(f"{self.base_url}/api/export/tv/sync-state")
        try:
            response.raise_for_status()
        except httpx.HTTPStatusError as e:
            raise WatchlistAppError(
                f"watchlist-app TV sync snapshot failed: HTTP {response.status_code}"
            ) from e

        try:
            payload = response.json()
        except ValueError as e:
            raise WatchlistAppError(
                "watchlist-app TV sync snapshot returned invalid JSON"
            ) from e

        self._reject_credential_shaped_keys(payload)
        if not isinstance(payload, dict):
            raise WatchlistAppError("watchlist-app TV sync snapshot returned invalid shape")
        if payload.get("schemaVersion") != "2":
            raise WatchlistAppError("watchlist-app TV sync snapshot has unsupported schemaVersion")

        destination_sync = payload.get("destinationSync")
        if not isinstance(destination_sync, dict) or destination_sync.get("capable") is not True:
            raise WatchlistAppError("watchlist-app TV sync snapshot destinationSync incapable")
        if not isinstance(destination_sync.get("blockers"), list) or not all(
            isinstance(blocker, str) for blocker in destination_sync["blockers"]
        ):
            raise WatchlistAppError("watchlist-app TV sync snapshot has invalid destinationSync")

        if payload.get("mutationCapable") is not False:
            raise WatchlistAppError("watchlist-app TV sync snapshot mutationCapable must be false")
        shows = payload.get("shows")
        if not isinstance(shows, list):
            raise WatchlistAppError("watchlist-app TV sync snapshot has invalid shows")

        mapped_shows = tuple(self._map_tv_show(show) for show in shows)
        trakt_ids = [show.trakt_id for show in mapped_shows]
        tvdb_ids = [show.tvdb_id for show in mapped_shows]
        if len(trakt_ids) != len(set(trakt_ids)):
            raise WatchlistAppError("watchlist-app TV sync snapshot has duplicate Trakt ID")
        if len(tvdb_ids) != len(set(tvdb_ids)):
            raise WatchlistAppError("watchlist-app TV sync snapshot has duplicate TVDB ID")

        generation_id = payload.get("generationId")
        kind = payload.get("kind")
        if not isinstance(generation_id, str) or not generation_id.strip():
            raise WatchlistAppError("watchlist-app TV sync snapshot has invalid generationId")
        if not isinstance(kind, str) or not kind.strip():
            raise WatchlistAppError("watchlist-app TV sync snapshot has invalid kind")

        return TvSnapshot(
            schema_version="2",
            generation_id=generation_id,
            published_at=self._parse_utc_datetime(payload.get("publishedAt"), "publishedAt"),
            generated_at=self._parse_utc_datetime(payload.get("generatedAt"), "generatedAt"),
            kind=kind,
            mutation_capable=False,
            shows=mapped_shows,
        )

    def _sync_movies(self) -> str:
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
        try:
            payload = response.json()
        except ValueError as e:
            raise WatchlistAppError("watchlist-app sync returned invalid JSON") from e

        letterboxd = payload.get("letterboxd") if isinstance(payload, dict) else None
        source_snapshot_id = (
            letterboxd.get("sourceSnapshotId")
            if isinstance(letterboxd, dict)
            else None
        )
        if not isinstance(source_snapshot_id, str) or not source_snapshot_id.strip():
            raise WatchlistAppError(
                "watchlist-app sync did not publish a Letterboxd sourceSnapshotId"
            )

        logger.info(
            "watchlist_app_sync_completed",
            source_snapshot_id=source_snapshot_id,
        )
        return source_snapshot_id

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
    def _parse_utc_datetime(value: Any, field: str, *, nullable: bool = False) -> datetime | None:
        if value is None and nullable:
            return None
        if not isinstance(value, str) or not value.strip():
            raise WatchlistAppError(f"watchlist-app TV sync snapshot has invalid {field}")
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        except ValueError as e:
            raise WatchlistAppError(
                f"watchlist-app TV sync snapshot has invalid {field}"
            ) from e
        if parsed.tzinfo is None or parsed.utcoffset() != timedelta(0):
            raise WatchlistAppError(
                f"watchlist-app TV sync snapshot has non-UTC {field}"
            )
        return parsed

    @classmethod
    def _map_tv_show(cls, item: Any) -> TvShow:
        if not isinstance(item, dict):
            raise WatchlistAppError("watchlist-app TV sync snapshot show is not an object")
        trakt_id = cls._positive_int(item.get("traktId"), "Trakt ID")
        tvdb_id = cls._positive_int(item.get("tvdbId"), "TVDB ID")
        title = item.get("title")
        if not isinstance(title, str) or not title.strip():
            raise WatchlistAppError("watchlist-app TV sync snapshot show has invalid title")
        seasons = item.get("seasons")
        if not isinstance(seasons, list):
            raise WatchlistAppError("watchlist-app TV sync snapshot show has invalid seasons")
        mapped_seasons = tuple(cls._map_tv_season(season) for season in seasons)
        season_numbers = [season.season_number for season in mapped_seasons]
        if len(season_numbers) != len(set(season_numbers)):
            raise WatchlistAppError("watchlist-app TV sync snapshot show has duplicate season")
        return TvShow(trakt_id, tvdb_id, title, mapped_seasons)

    @classmethod
    def _map_tv_season(cls, item: Any) -> TvSeason:
        if not isinstance(item, dict):
            raise WatchlistAppError("watchlist-app TV sync snapshot season is not an object")
        raw_season_number = item.get("seasonNumber")
        if raw_season_number == 0:
            raise WatchlistAppError("watchlist-app TV sync snapshot has special season")
        season_number = cls._positive_int(raw_season_number, "season number")
        episodes = item.get("episodes")
        if not isinstance(episodes, list):
            raise WatchlistAppError("watchlist-app TV sync snapshot season has invalid episodes")
        mapped_episodes = tuple(cls._map_tv_episode(episode, season_number) for episode in episodes)
        episode_numbers = [episode.episode_number for episode in mapped_episodes]
        if len(episode_numbers) != len(set(episode_numbers)):
            raise WatchlistAppError("watchlist-app TV sync snapshot season has duplicate episode")
        return TvSeason(season_number, cls._map_tv_availability(item.get("polandAvailability")), mapped_episodes)

    @classmethod
    def _map_tv_episode(cls, item: Any, season_number: int) -> TvEpisode:
        if not isinstance(item, dict):
            raise WatchlistAppError("watchlist-app TV sync snapshot episode is not an object")
        episode_season = cls._positive_int(item.get("seasonNumber"), "episode season number")
        if episode_season != season_number:
            raise WatchlistAppError("watchlist-app TV sync snapshot episode has mismatched season")
        tvdb_id = item.get("tvdbId")
        if tvdb_id is not None:
            tvdb_id = cls._positive_int(tvdb_id, "episode TVDB ID")
        return TvEpisode(
            trakt_episode_id=cls._positive_int(item.get("traktEpisodeId"), "episode Trakt ID"),
            season_number=episode_season,
            episode_number=cls._positive_int(item.get("episodeNumber"), "episode number"),
            tvdb_id=tvdb_id,
            first_aired=cls._parse_utc_datetime(item.get("firstAired"), "firstAired", nullable=True),
            last_watched_at=cls._parse_utc_datetime(item.get("lastWatchedAt"), "lastWatchedAt", nullable=True),
        )

    @classmethod
    def _map_tv_availability(cls, item: Any) -> TvAvailability:
        if not isinstance(item, dict):
            raise WatchlistAppError("watchlist-app TV sync snapshot has invalid polandAvailability")
        state = item.get("state")
        region = item.get("region")
        if not isinstance(state, str) or not state.strip():
            raise WatchlistAppError("watchlist-app TV sync snapshot has invalid availability state")
        if region != "PL":
            raise WatchlistAppError("watchlist-app TV sync snapshot has invalid availability region")
        fetched_at = cls._parse_utc_datetime(item.get("fetchedAt"), "availability fetchedAt")
        return TvAvailability(state=state, region=region, fetched_at=fetched_at)

    @staticmethod
    def _positive_int(value: Any, label: str) -> int:
        if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
            raise WatchlistAppError(f"watchlist-app TV sync snapshot has invalid {label}")
        return value

    @classmethod
    def _reject_credential_shaped_keys(cls, value: Any) -> None:
        if isinstance(value, dict):
            for key, nested_value in value.items():
                if not isinstance(key, str):
                    raise WatchlistAppError("watchlist-app TV sync snapshot has non-text JSON key")
                normalized = "".join(character for character in key.lower() if character.isalnum())
                if normalized != "cleanupauthorizations" and any(
                    marker in normalized
                    for marker in (
                        "token",
                        "secret",
                        "password",
                        "authorization",
                        "credential",
                        "apikey",
                        "privatekey",
                        "encryptionkey",
                        "signingkey",
                        "synckey",
                    )
                ):
                    raise WatchlistAppError(
                        "watchlist-app TV sync snapshot contains credential-shaped key"
                    )
                cls._reject_credential_shaped_keys(nested_value)
        elif isinstance(value, list):
            for item in value:
                cls._reject_credential_shaped_keys(item)

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

    @classmethod
    def _map_watched_snapshot_item(cls, item: Any) -> dict[str, Any]:
        if not isinstance(item, dict):
            raise WatchlistAppError(
                "watchlist-app watched movie snapshot item is not an object"
            )

        title = item.get("title")
        source_id = item.get("sourceId")
        lifecycle_event_id = item.get("lifecycleEventId")
        if not all(
            isinstance(value, str) and value.strip()
            for value in (title, source_id, lifecycle_event_id)
        ):
            raise WatchlistAppError(
                "watchlist-app watched movie snapshot item is missing required text fields"
            )

        tmdb_id = item.get("tmdbId")
        if tmdb_id is not None and (
            isinstance(tmdb_id, bool)
            or not isinstance(tmdb_id, int)
            or tmdb_id <= 0
        ):
            raise WatchlistAppError(
                "watchlist-app watched movie snapshot item has invalid tmdbId"
            )

        year = item.get("year")
        if year is not None and (isinstance(year, bool) or not isinstance(year, int)):
            raise WatchlistAppError(
                "watchlist-app watched movie snapshot item has invalid year"
            )

        imdb_id = item.get("imdbId")
        if imdb_id is not None and (
            not isinstance(imdb_id, str) or not imdb_id.strip()
        ):
            raise WatchlistAppError(
                "watchlist-app watched movie snapshot item has invalid imdbId"
            )

        lifecycle_version = item.get("lifecycleVersion")
        if (
            isinstance(lifecycle_version, bool)
            or not isinstance(lifecycle_version, int)
            or lifecycle_version <= 0
        ):
            raise WatchlistAppError(
                "watchlist-app watched movie snapshot item has invalid lifecycleVersion"
            )

        return {
            "tmdb_id": tmdb_id,
            "imdb_id": imdb_id,
            "title": title,
            "year": year,
            "source_id": source_id,
            "watched_at": cls._parse_datetime(item.get("watchedAt"), "watchedAt"),
            "lifecycle_version": lifecycle_version,
            "lifecycle_event_id": lifecycle_event_id,
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
