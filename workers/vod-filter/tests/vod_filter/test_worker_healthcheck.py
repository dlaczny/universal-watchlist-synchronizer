from __future__ import annotations

import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from healthcheck import check_heartbeat, write_heartbeat


NOW = datetime(2026, 7, 11, 8, 0, tzinfo=timezone.utc)


def test_healthcheck_accepts_recent_completed_heartbeat(tmp_path: Path):
    path = tmp_path / "last-run.json"
    write_heartbeat(path, status="completed", exit_code=0, written_at=NOW)

    assert check_heartbeat(path, max_age_seconds=7200, now=NOW) is True
    assert not (tmp_path / "last-run.json.tmp").exists()


def test_healthcheck_rejects_stale_failed_or_malformed_heartbeat(tmp_path: Path):
    stale = tmp_path / "stale.json"
    failed = tmp_path / "failed.json"
    malformed = tmp_path / "malformed.json"
    write_heartbeat(
        stale,
        status="completed",
        exit_code=0,
        written_at=NOW - timedelta(seconds=7201),
    )
    write_heartbeat(failed, status="failed", exit_code=1, written_at=NOW)
    malformed.write_text("not-json", encoding="utf-8")

    assert check_heartbeat(stale, max_age_seconds=7200, now=NOW) is False
    assert check_heartbeat(failed, max_age_seconds=7200, now=NOW) is False
    assert check_heartbeat(malformed, max_age_seconds=7200, now=NOW) is False
