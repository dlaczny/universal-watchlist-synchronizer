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

