from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.models.movie import Movie
from src.services.plex_service import PlexService


class FakePlexClient:
    def __init__(self):
        self.watchlist_calls = 0
        self.is_in_watchlist_calls = []

    def get_watchlist(self):
        self.watchlist_calls += 1
        return [{"tmdb_id": 101, "title": "Already There", "year": 2020}]

    def is_in_watchlist(self, tmdb_id, title):
        self.is_in_watchlist_calls.append((tmdb_id, title))
        return False


class FakeCacheService:
    def update_sync_state(self, **kwargs):
        pass

    def is_cache_valid(self, cache_name, ttl_minutes):
        return False

    def get_plex_watchlist_cache(self):
        return []

    def refresh_plex_watchlist_cache(self, watchlist):
        pass


def test_sync_to_plex_dry_run_reuses_single_watchlist_fetch_for_add_checks():
    client = FakePlexClient()
    service = PlexService(client, FakeCacheService(), cache_ttl_minutes=0)

    stats = service.sync_to_plex(
        letterboxd_movies=[
            Movie(tmdb_id=101, title="Already There", year=2020),
            Movie(tmdb_id=202, title="New Movie", year=2021),
        ],
        vod_status={101: True, 202: True},
        dry_run=True,
    )

    assert stats["added"] == 1
    assert stats["skipped"] == 1
    assert client.watchlist_calls == 1
    assert client.is_in_watchlist_calls == []

