from __future__ import annotations

import sys
from pathlib import Path

import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.clients.plex_client import PlexClient, PlexError


class Guid:
    def __init__(self, value: str):
        self.id = value


class PlexMovie:
    type = "movie"

    def __init__(self, title: str, year: int, tmdb_id: int | None):
        self.title = title
        self.year = year
        self.guids = [] if tmdb_id is None else [Guid(f"tmdb://{tmdb_id}")]
        self.removed = False

    def removeFromWatchlist(self):
        self.removed = True


class Account:
    def __init__(self, movies):
        self.movies = movies

    def searchDiscover(self, **_kwargs):
        return self.movies

    def watchlist(self):
        return self.movies


class QueryAccount:
    def __init__(self, results_by_query=None, error=None):
        self.results_by_query = results_by_query or {}
        self.error = error
        self.calls = []

    def searchDiscover(self, **kwargs):
        self.calls.append(kwargs)
        if self.error:
            raise self.error
        return self.results_by_query.get(kwargs["query"], [])


def client_with(*movies: PlexMovie) -> PlexClient:
    client = PlexClient.__new__(PlexClient)
    client.account = Account(list(movies))
    return client


def test_discovery_requires_the_expected_tmdb_identity() -> None:
    wrong = PlexMovie("Shared title", 2026, 101)
    expected = PlexMovie("Shared title", 2026, 202)
    client = client_with(wrong, expected)

    result = client._search_plex_discovery("Shared title", 2026, 202)

    assert result is expected


def test_discovery_rejects_title_year_match_without_tmdb_identity() -> None:
    client = client_with(PlexMovie("Shared title", 2026, None))

    result = client._search_plex_discovery("Shared title", 2026, 202)

    assert result is None


def test_discovery_uses_broad_search_but_requires_exact_tmdb_identity() -> None:
    wrong = [PlexMovie("Dreams", 2024, tmdb_id) for tmdb_id in range(1, 12)]
    expected = PlexMovie("Dreams", 2025, 1228682)
    client = PlexClient.__new__(PlexClient)
    client.account = QueryAccount({"Dreams": [*wrong, expected]})

    result = client._search_plex_discovery("Dreams", 2024, 1228682)

    assert result is expected
    assert client.account.calls[0] == {"query": "Dreams", "limit": 50}


def test_discovery_retries_with_ascii_title_variant() -> None:
    expected = PlexMovie("Brahmastra Part One: Shiva", 2022, 496331)
    client = PlexClient.__new__(PlexClient)
    client.account = QueryAccount({"Brahmastra Part One: Shiva": [expected]})

    result = client._search_plex_discovery(
        "Brahm\u0101stra Part One: Shiva",
        2022,
        496331,
    )

    assert result is expected


def test_discovery_propagates_transient_errors_for_outer_retry() -> None:
    client = PlexClient.__new__(PlexClient)
    client.account = QueryAccount(error=TimeoutError("Plex discovery timed out"))

    with pytest.raises(PlexError, match="Plex discovery search failed"):
        client._search_plex_discovery("Movie", 2024, 101)


def test_remove_uses_tmdb_identity_not_title_fallback() -> None:
    same_title_wrong_id = PlexMovie("Shared title", 2026, 101)
    expected = PlexMovie("Shared title", 2026, 202)
    client = client_with(same_title_wrong_id, expected)

    removed = client.remove_from_watchlist(202, "Shared title")

    assert removed is True
    assert same_title_wrong_id.removed is False
    assert expected.removed is True
