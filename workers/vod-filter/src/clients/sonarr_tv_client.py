"""Exact-TVDB Sonarr boundary client for reversible TV destination actions."""

from __future__ import annotations

from copy import deepcopy
from dataclasses import dataclass
from typing import Any

import httpx


class SonarrTvError(RuntimeError):
    """Raised when Sonarr cannot prove an exact TVDB operation."""


@dataclass(frozen=True)
class SonarrSeriesLookup:
    """An exact-TVDB lookup resource that may be safely added to Sonarr."""

    tvdb_id: int
    resource: dict[str, Any]


@dataclass(frozen=True)
class SonarrSeries:
    """A live Sonarr series, retaining its resource for narrow PUT updates."""

    series_id: int
    tvdb_id: int
    title: str
    monitored: bool
    seasons: dict[int, bool]
    resource: dict[str, Any]


class SonarrTvClient:
    """Expose only exact, reversible Sonarr series and episode operations."""

    def __init__(
        self,
        base_url: str,
        api_key: str,
        *,
        http_client: httpx.Client | None = None,
        timeout_seconds: int = 30,
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.http_client = http_client or httpx.Client(
            headers={"X-Api-Key": api_key},
            timeout=timeout_seconds,
        )

    def get_series_by_tvdb(self, tvdb_id: int) -> SonarrSeries | None:
        """Return the one exact-TVDB series from Sonarr's live series list."""
        tvdb_id = self._positive_id(tvdb_id, "TVDB ID")
        payload = self._request("GET", "/api/v3/series")
        if not isinstance(payload, list):
            raise SonarrTvError("Sonarr series list returned a non-list payload")

        matches = [row for row in payload if self._tvdb_matches(row, tvdb_id)]
        if not matches:
            return None
        if len(matches) != 1:
            raise SonarrTvError(f"Sonarr returned multiple series for TVDB {tvdb_id}")
        return self._series_from_resource(matches[0], expected_tvdb_id=tvdb_id)

    def get_all_series(self) -> list[SonarrSeries]:
        """Read all Sonarr series once and retain only exact positive TVDB rows."""
        payload = self._request("GET", "/api/v3/series")
        if not isinstance(payload, list):
            raise SonarrTvError("Sonarr series list returned a non-list payload")
        result: list[SonarrSeries] = []
        seen: set[int] = set()
        for row in payload:
            if not isinstance(row, dict):
                raise SonarrTvError("Sonarr series row is invalid")
            tvdb_id = self._positive_id(row.get("tvdbId"), "Sonarr TVDB ID")
            if tvdb_id in seen:
                raise SonarrTvError(f"Sonarr returned multiple series for TVDB {tvdb_id}")
            seen.add(tvdb_id)
            result.append(self._series_from_resource(row, expected_tvdb_id=tvdb_id))
        return result

    def lookup_by_tvdb(self, tvdb_id: int) -> SonarrSeriesLookup:
        """Look up a single addable series using Sonarr's exact TVDB term."""
        tvdb_id = self._positive_id(tvdb_id, "TVDB ID")
        payload = self._request(
            "GET",
            "/api/v3/series/lookup",
            params={"term": f"tvdb:{tvdb_id}"},
        )
        if not isinstance(payload, list):
            raise SonarrTvError("Sonarr series lookup returned a non-list payload")

        matches = [row for row in payload if self._tvdb_matches(row, tvdb_id)]
        if not matches:
            raise SonarrTvError(f"Sonarr TVDB identity mismatch for {tvdb_id}")
        if len(matches) != 1:
            raise SonarrTvError(f"Sonarr returned multiple lookup rows for TVDB {tvdb_id}")
        resource = self._verified_resource(matches[0], tvdb_id)
        return SonarrSeriesLookup(tvdb_id=tvdb_id, resource=resource)

    def add_series(
        self,
        lookup: SonarrSeriesLookup,
        root_folder: str,
        quality_profile_id: int,
    ) -> SonarrSeries:
        """Add one previously verified lookup result without starting a search."""
        if not isinstance(lookup, SonarrSeriesLookup):
            raise SonarrTvError("Sonarr add requires an exact TVDB lookup")
        tvdb_id = self._positive_id(lookup.tvdb_id, "TVDB ID")
        resource = self._verified_resource(lookup.resource, tvdb_id)
        if not isinstance(root_folder, str) or not root_folder.strip():
            raise SonarrTvError("Sonarr root folder is required")
        quality_profile_id = self._positive_id(quality_profile_id, "quality profile ID")

        resource.update(
            {
                "rootFolderPath": root_folder,
                "qualityProfileId": quality_profile_id,
                "monitored": True,
                "monitorNewItems": "all",
                "addOptions": {"searchForMissingEpisodes": False},
            }
        )
        result = self._request("POST", "/api/v3/series", json=resource)
        return self._series_from_resource(result, expected_tvdb_id=tvdb_id)

    def set_series_monitored(self, series: SonarrSeries, monitored: bool) -> SonarrSeries:
        """Set the exact series monitoring state while preserving its seasons."""
        if not isinstance(series, SonarrSeries):
            raise SonarrTvError("Sonarr monitoring requires a series resource")
        if not isinstance(monitored, bool):
            raise SonarrTvError("Sonarr monitored must be a boolean")
        series_id = self._positive_id(series.series_id, "Sonarr series ID")
        tvdb_id = self._positive_id(series.tvdb_id, "Sonarr TVDB ID")
        resource = self._verified_resource(series.resource, tvdb_id)
        if self._positive_id(resource.get("id"), "Sonarr series ID") != series_id:
            raise SonarrTvError("Sonarr series resource ID mismatch")
        resource["monitored"] = monitored
        resource["monitorNewItems"] = "all" if monitored else "none"
        result = self._request("PUT", f"/api/v3/series/{series_id}", json=resource)
        return self._series_from_resource(result, expected_tvdb_id=tvdb_id)

    def set_season_monitored(self, series: SonarrSeries, season_number: int) -> SonarrSeries:
        """Monitor one known season without changing any other season state."""
        if not isinstance(series, SonarrSeries):
            raise SonarrTvError("Sonarr season monitoring requires a series resource")
        season_number = self._season_number(season_number)
        series_id = self._positive_id(series.series_id, "Sonarr series ID")
        tvdb_id = self._positive_id(series.tvdb_id, "Sonarr TVDB ID")
        resource = self._verified_resource(series.resource, tvdb_id)
        if self._positive_id(resource.get("id"), "Sonarr series ID") != series_id:
            raise SonarrTvError("Sonarr series resource ID mismatch")
        seasons = resource.get("seasons")
        if not isinstance(seasons, list):
            raise SonarrTvError("Sonarr series has no seasons")
        found = False
        for season in seasons:
            if not isinstance(season, dict):
                raise SonarrTvError("Sonarr season row is invalid")
            if self._season_number(season.get("seasonNumber")) == season_number:
                season["monitored"] = True
                found = True
        if not found:
            raise SonarrTvError(f"Sonarr series has no season {season_number}")
        result = self._request("PUT", f"/api/v3/series/{series_id}", json=resource)
        return self._series_from_resource(result, expected_tvdb_id=tvdb_id)

    def search_episode_ids(self, series_id: int, episode_ids: list[int]) -> None:
        """Issue an episode search for exactly the supplied Sonarr episode IDs."""
        self._positive_id(series_id, "Sonarr series ID")
        if not isinstance(episode_ids, list):
            raise SonarrTvError("Sonarr episode IDs must be a list")
        exact_ids = sorted({self._positive_id(value, "Sonarr episode ID") for value in episode_ids})
        if not exact_ids:
            return
        self._request(
            "POST",
            "/api/v3/command",
            json={"name": "EpisodeSearch", "episodeIds": exact_ids},
        )

    def _request(self, method: str, path: str, **kwargs: Any) -> Any:
        try:
            response = self.http_client.request(method, f"{self.base_url}{path}", **kwargs)
            response.raise_for_status()
        except httpx.HTTPError as error:
            status = getattr(getattr(error, "response", None), "status_code", None)
            detail = f" HTTP {status}" if status is not None else ""
            raise SonarrTvError(f"Sonarr {method} {path} failed:{detail}") from error
        try:
            return response.json()
        except ValueError as error:
            raise SonarrTvError(f"Sonarr {method} {path} returned invalid JSON") from error

    @classmethod
    def _series_from_resource(cls, resource: Any, *, expected_tvdb_id: int) -> SonarrSeries:
        resource = cls._verified_resource(resource, expected_tvdb_id)
        series_id = cls._positive_id(resource.get("id"), "Sonarr series ID")
        title = resource.get("title")
        if not isinstance(title, str) or not title.strip():
            raise SonarrTvError("Sonarr series title is invalid")
        monitored = resource.get("monitored")
        if not isinstance(monitored, bool):
            raise SonarrTvError("Sonarr series monitored flag is invalid")
        raw_seasons = resource.get("seasons")
        if not isinstance(raw_seasons, list):
            raise SonarrTvError("Sonarr series seasons are invalid")
        seasons: dict[int, bool] = {}
        for season in raw_seasons:
            if not isinstance(season, dict):
                raise SonarrTvError("Sonarr season row is invalid")
            season_number = cls._season_number(season.get("seasonNumber"))
            season_monitored = season.get("monitored")
            if not isinstance(season_monitored, bool):
                raise SonarrTvError("Sonarr season monitored flag is invalid")
            if season_number in seasons:
                raise SonarrTvError(f"Sonarr has duplicate season {season_number}")
            seasons[season_number] = season_monitored
        return SonarrSeries(
            series_id=series_id,
            tvdb_id=expected_tvdb_id,
            title=title,
            monitored=monitored,
            seasons=seasons,
            resource=resource,
        )

    @classmethod
    def _verified_resource(cls, resource: Any, expected_tvdb_id: int) -> dict[str, Any]:
        if not isinstance(resource, dict):
            raise SonarrTvError("Sonarr series resource is invalid")
        actual_tvdb_id = cls._positive_id(resource.get("tvdbId"), "Sonarr TVDB ID")
        if actual_tvdb_id != expected_tvdb_id:
            raise SonarrTvError(
                f"Sonarr TVDB identity mismatch: expected {expected_tvdb_id}, received {actual_tvdb_id}"
            )
        return deepcopy(resource)

    @staticmethod
    def _tvdb_matches(resource: Any, tvdb_id: int) -> bool:
        return isinstance(resource, dict) and resource.get("tvdbId") == tvdb_id

    @staticmethod
    def _positive_id(value: Any, name: str) -> int:
        if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
            raise SonarrTvError(f"{name} must be a positive integer")
        return value

    @staticmethod
    def _season_number(value: Any) -> int:
        if not isinstance(value, int) or isinstance(value, bool) or value < 0:
            raise SonarrTvError("Sonarr season number must be a non-negative integer")
        return value
