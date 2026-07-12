from __future__ import annotations

import sys
from pathlib import Path

import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.clients.radarr_client import RadarrClient, RadarrError


class FakeResponse:
    def raise_for_status(self) -> None:
        return None


class FakeHttpClient:
    def __init__(self) -> None:
        self.deleted = []

    def delete(self, path: str) -> FakeResponse:
        self.deleted.append(path)
        return FakeResponse()


class FakeRadarrApi:
    def __init__(self) -> None:
        self.added = []

    def get_movie(self):
        return []

    def lookup_movie(self, term: str):
        return [{"tmdbId": int(term.removeprefix("tmdb:")), "title": "Movie"}]

    def add_movie(self, movie_data, root_dir: str, quality_profile_id: int):
        self.added.append((movie_data.copy(), root_dir, quality_profile_id))
        return {"id": 42, **movie_data}


def client_with_exclusion(tmdb_id: int = 101) -> RadarrClient:
    client = RadarrClient.__new__(RadarrClient)
    client.url = "http://radarr.local"
    client.api_key = "test-key"
    client.root_folder = "/movies"
    client.quality_profile_id = 7
    client.client = FakeRadarrApi()
    client.http_client = FakeHttpClient()
    client._exclusions_cache = [
        {"id": 55, "tmdbId": tmdb_id, "movieTitle": "Movie"},
        {"id": 66, "tmdbId": 999, "movieTitle": "Other"},
    ]
    return client


def test_radarr_add_preserves_exclusion_without_explicit_override() -> None:
    client = client_with_exclusion()

    result = client.add_movie(101, "Movie", 2024)

    assert result is None
    assert client.http_client.deleted == []
    assert client.client.added == []


def test_radarr_add_removes_only_exact_exclusion_when_override_is_explicit() -> None:
    client = client_with_exclusion()

    result = client.add_movie(101, "Movie", 2024, override_exclusion=True)

    assert result["id"] == 42
    assert client.http_client.deleted == ["api/v3/exclusions/55"]
    assert client._exclusions_cache == [
        {"id": 66, "tmdbId": 999, "movieTitle": "Other"}
    ]
    assert len(client.client.added) == 1


def test_radarr_exclusion_check_propagates_unreadable_boundary() -> None:
    client = client_with_exclusion()

    def fail():
        raise RadarrError("exclusions unavailable")

    client.get_exclusions = fail

    with pytest.raises(RadarrError, match="exclusions unavailable"):
        RadarrClient.is_movie_excluded.__wrapped__(client, 101)
