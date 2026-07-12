from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.services.cache_service import CacheService


def test_run_history_records_start_and_finish(tmp_path: Path) -> None:
    cache = CacheService(database_path=str(tmp_path / "vod-filter.db"))

    run_id = cache.start_run(
        workflow="cleanup",
        dry_run=True,
        trigger="manual",
    )
    cache.finish_run(
        run_id=run_id,
        status="success",
        exit_code=0,
        summary={"removed_from_radarr": 2},
    )

    runs = cache.get_recent_runs(limit=1)

    assert len(runs) == 1
    assert runs[0]["id"] == run_id
    assert runs[0]["workflow"] == "cleanup"
    assert runs[0]["dry_run"] == 1
    assert runs[0]["status"] == "success"
    assert runs[0]["exit_code"] == 0
    assert runs[0]["summary"] == '{"removed_from_radarr": 2}'
    assert runs[0]["started_at"]
    assert runs[0]["finished_at"]


def test_cleanup_history_records_success_and_error_without_credentials(tmp_path: Path):
    cache = CacheService(database_path=str(tmp_path / "vod-filter.db"))
    cache.record_cleanup_attempt(
        authorization="letterboxd_watched",
        authorization_event_id="movie-101:watched:2",
        destination="radarr",
        tmdb_id=101,
        delete_files=True,
        status="completed",
        error=None,
    )
    cache.record_cleanup_attempt(
        authorization="manual_radarr_removal",
        authorization_event_id=None,
        destination="plex_watchlist",
        tmdb_id=202,
        delete_files=False,
        status="error",
        error="plex unavailable",
    )

    attempts = cache.get_recent_cleanup_attempts(limit=10)

    assert [attempt["status"] for attempt in attempts] == ["error", "completed"]
    assert attempts[0]["error"] == "plex unavailable"
    assert all("token" not in str(attempt).lower() for attempt in attempts)

