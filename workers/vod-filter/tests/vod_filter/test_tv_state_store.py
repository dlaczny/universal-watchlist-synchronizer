from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.services.tv_state_store import TvStateStore


def test_tv_state_store_keeps_ownership_runs_actions_and_lease_separate_from_movies(
    tmp_path: Path,
) -> None:
    database_path = tmp_path / "vod-filter.db"
    store = TvStateStore(database_path)

    run_id = store.start_run("generation-1")
    store.finish_run(run_id, "completed")
    store.record_ownership("sonarr", 100, "worker")
    store.record_ownership("plex_watchlist", 101, "manual")
    store.record_action("generation-1:sonarr:100:1:sonarr_add", "sonarr_add", "completed")

    assert store.get_ownership() == (
        {"destination": "plex_watchlist", "tvdb_id": 101, "origin": "manual"},
        {"destination": "sonarr", "tvdb_id": 100, "origin": "worker"},
    )
    assert store.get_actions() == (
        {
            "action_id": "generation-1:sonarr:100:1:sonarr_add",
            "action": "sonarr_add",
            "status": "completed",
        },
    )
    assert store.get_runs() == ((run_id, "generation-1", "completed"),)

    with store.connection() as connection:
        assert connection.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'movies'"
        ).fetchone() is None


def test_tv_state_store_lease_is_exclusive_until_released(tmp_path: Path) -> None:
    store = TvStateStore(tmp_path / "vod-filter.db")

    assert store.acquire_lease("worker-a") is True
    assert store.acquire_lease("worker-b") is False
    store.release_lease("worker-a")
    assert store.acquire_lease("worker-b") is True
