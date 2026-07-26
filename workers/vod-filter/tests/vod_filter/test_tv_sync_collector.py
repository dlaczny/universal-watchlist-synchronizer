from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.services.tv_sync_collector import TvSyncCollector
from src.clients.sonarr_tv_client import SonarrSeries


class Backend:
    def __init__(self) -> None:
        self.calls = 0

    def fetch_tv_sync_snapshot(self):
        self.calls += 1
        return "snapshot"


class Sonarr:
    def get_all_series(self):
        return [SonarrSeries(1, 100, "Example", True, {1: True}, {"id": 1, "tvdbId": 100})]

    def get_episode_ids_by_tvdb(self, series):
        assert series.tvdb_id == 100
        return {1001: 9001}


class Plex:
    def get_watchlist_shows(self):
        return ["watchlist"]

    def get_library_show_identities(self, name: str):
        assert name == "TV"
        return {"library"}


class State:
    def get_ownership(self):
        return [{"destination": "sonarr", "tvdb_id": 100, "origin": "worker"}]


def test_collector_reads_one_snapshot_and_all_destination_boundaries() -> None:
    backend = Backend()
    collected = TvSyncCollector(
        backend_client=backend,
        sonarr_client=Sonarr(),
        plex_client=Plex(),
        state_store=State(),
        plex_library_name="TV",
    ).collect()

    assert backend.calls == 1
    assert collected.snapshot == "snapshot"
    assert collected.sonarr_series[0].tvdb_id == 100
    assert collected.sonarr_episode_ids_by_tvdb == ((100, 1001, 9001),)
    assert collected.plex_watchlist == ("watchlist",)
    assert collected.plex_library_identities == frozenset({"library"})
    assert collected.ownership[0].origin == "worker"
    assert collected.collection_errors == ()


def test_collector_preserves_named_errors_from_every_failed_boundary() -> None:
    class FailedSonarr(Sonarr):
        def get_all_series(self):
            raise RuntimeError("unavailable")

    collected = TvSyncCollector(
        backend_client=Backend(),
        sonarr_client=FailedSonarr(),
        plex_client=Plex(),
        state_store=State(),
        plex_library_name="TV",
    ).collect()

    assert collected.sonarr_series == ()
    assert collected.collection_errors == ("sonarr: unavailable",)
