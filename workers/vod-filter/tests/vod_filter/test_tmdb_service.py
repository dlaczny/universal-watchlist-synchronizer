from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.services.tmdb_service import TMDBService


class FakeTMDBClient:
    region = "PL"

    def __init__(self):
        self.watch_provider_calls = []

    def get_watch_providers(self, tmdb_id):
        self.watch_provider_calls.append(tmdb_id)
        return [
            {
                "provider_id": 1899,
                "provider_name": "HBO Max",
                "region": "PL",
                "availability_type": "flatrate",
            },
            {
                "provider_id": 10,
                "provider_name": "Rent Store",
                "region": "PL",
                "availability_type": "rent",
            },
        ]


class FakeCacheService:
    def __init__(self):
        self.cached_vod = []
        self.upserts = []

    def get_vod_availability(self, tmdb_id, cache_ttl_hours):
        return self.cached_vod

    def clear_vod_availability(self, tmdb_id):
        pass

    def upsert_vod_availability(self, **kwargs):
        self.upserts.append(kwargs)


def test_check_vod_availability_fetches_watch_providers_once_per_movie():
    service = TMDBService.__new__(TMDBService)
    service.cache_service = FakeCacheService()
    service.cache_ttl_hours = 48
    service.client = FakeTMDBClient()

    is_available, records = service.check_vod_availability(
        tmdb_id=101,
        configured_providers=[1899],
    )

    assert is_available is True
    assert [record.provider_id for record in records] == [1899]
    assert service.client.watch_provider_calls == [101]
    assert service.cache_service.upserts == [
        {
            "tmdb_id": 101,
            "provider_id": 1899,
            "provider_name": "HBO Max",
            "region": "PL",
            "availability_type": "flatrate",
        }
    ]

