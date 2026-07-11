from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.services.cache_service import CacheService


def test_managed_destinations_are_migration_safe_and_persistent(tmp_path: Path):
    database_path = tmp_path / "vod-filter.db"
    cache = CacheService(database_path=str(database_path))

    cache.mark_managed("radarr", 101, "add")
    cache.mark_managed("plex_watchlist", 202, "adopt")
    cache.mark_managed("radarr", 101, "keep")

    restarted = CacheService(database_path=str(database_path))
    assert restarted.get_managed_destinations() == [
        {
            "destination": "plex_watchlist",
            "tmdb_id": 202,
            "last_action": "adopt",
        },
        {
            "destination": "radarr",
            "tmdb_id": 101,
            "last_action": "keep",
        },
    ]


def test_release_managed_removes_only_requested_destination(tmp_path: Path):
    cache = CacheService(database_path=str(tmp_path / "vod-filter.db"))
    cache.mark_managed("radarr", 101, "add")
    cache.mark_managed("plex_watchlist", 101, "add")

    cache.release_managed("radarr", 101)

    assert cache.get_managed_destinations() == [
        {
            "destination": "plex_watchlist",
            "tmdb_id": 101,
            "last_action": "add",
        }
    ]
