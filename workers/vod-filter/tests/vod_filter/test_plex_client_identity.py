from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.clients.plex_client import PlexClient


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


def test_remove_uses_tmdb_identity_not_title_fallback() -> None:
    same_title_wrong_id = PlexMovie("Shared title", 2026, 101)
    expected = PlexMovie("Shared title", 2026, 202)
    client = client_with(same_title_wrong_id, expected)

    removed = client.remove_from_watchlist(202, "Shared title")

    assert removed is True
    assert same_title_wrong_id.removed is False
    assert expected.removed is True
