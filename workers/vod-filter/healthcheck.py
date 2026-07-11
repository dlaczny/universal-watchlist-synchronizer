"""Worker heartbeat writer and container health check."""

from __future__ import annotations

import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path


HEALTHY_STATUSES = {"completed", "partial", "reconciliation"}


def write_heartbeat(
    path: Path,
    *,
    status: str,
    exit_code: int,
    written_at: datetime | None = None,
) -> Path:
    """Atomically write the latest worker run status."""
    path.parent.mkdir(parents=True, exist_ok=True)
    timestamp = written_at or datetime.now(timezone.utc)
    temp_path = path.with_name(path.name + ".tmp")
    temp_path.write_text(
        json.dumps(
            {
                "status": status,
                "exit_code": exit_code,
                "written_at": timestamp.isoformat(),
            },
            sort_keys=True,
        ),
        encoding="utf-8",
    )
    temp_path.replace(path)
    return path


def check_heartbeat(
    path: Path,
    *,
    max_age_seconds: int,
    now: datetime | None = None,
) -> bool:
    """Return whether the worker has a recent accepted heartbeat."""
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
        written_at = datetime.fromisoformat(payload["written_at"])
        if written_at.tzinfo is None:
            return False
        current_time = now or datetime.now(timezone.utc)
        age_seconds = (current_time - written_at).total_seconds()
        return (
            payload.get("status") in HEALTHY_STATUSES
            and 0 <= age_seconds <= max_age_seconds
        )
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError):
        return False


def main() -> int:
    path = Path(os.getenv("WORKER_HEARTBEAT_PATH", "/app/data/last-run.json"))
    max_age = int(os.getenv("WORKER_HEALTH_MAX_AGE_SECONDS", "7500"))
    return 0 if check_heartbeat(path, max_age_seconds=max_age) else 1


if __name__ == "__main__":
    sys.exit(main())
