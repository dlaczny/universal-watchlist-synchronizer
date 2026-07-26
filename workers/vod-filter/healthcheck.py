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
    workflow: str | None = None,
    written_at: datetime | None = None,
) -> Path:
    """Atomically update one workflow heartbeat while preserving legacy fields."""
    path.parent.mkdir(parents=True, exist_ok=True)
    timestamp = written_at or datetime.now(timezone.utc)
    temp_path = path.with_name(path.name + ".tmp")
    entry = {
        "status": status,
        "exit_code": exit_code,
        "written_at": timestamp.isoformat(),
    }
    payload = dict(entry)
    if workflow:
        try:
            existing = json.loads(path.read_text(encoding="utf-8"))
            workflows = existing.get("workflows", {}) if isinstance(existing, dict) else {}
        except (OSError, ValueError, TypeError, json.JSONDecodeError):
            workflows = {}
        if not isinstance(workflows, dict):
            workflows = {}
        workflows = dict(workflows)
        workflows[workflow] = entry
        payload["workflows"] = workflows
    temp_path.write_text(
        json.dumps(payload, sort_keys=True),
        encoding="utf-8",
    )
    temp_path.replace(path)
    return path


def check_heartbeat(
    path: Path,
    *,
    max_age_seconds: int,
    now: datetime | None = None,
    required_workflows: tuple[str, ...] = (),
) -> bool:
    """Return whether the worker has a recent accepted heartbeat."""
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
        current_time = now or datetime.now(timezone.utc)
        entries: list[object]
        if required_workflows:
            workflows = payload.get("workflows")
            if isinstance(workflows, dict):
                entries = [workflows.get(workflow) for workflow in required_workflows]
            elif required_workflows == ("movie_sync",):
                entries = [payload]
            else:
                return False
        else:
            entries = [payload]
        for entry in entries:
            if not isinstance(entry, dict):
                return False
            written_at = datetime.fromisoformat(entry["written_at"])
            if written_at.tzinfo is None:
                return False
            age_seconds = (current_time - written_at).total_seconds()
            if entry.get("status") not in HEALTHY_STATUSES or not 0 <= age_seconds <= max_age_seconds:
                return False
        return True
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError):
        return False


def main() -> int:
    path = Path(os.getenv("WORKER_HEARTBEAT_PATH", "/app/data/last-run.json"))
    max_age = int(os.getenv("WORKER_HEALTH_MAX_AGE_SECONDS", "7500"))
    required_workflows = ("movie_sync", "tv_sync") if os.getenv("TV_SYNC_ENABLED", "false").lower() == "true" else ("movie_sync",)
    return 0 if check_heartbeat(path, max_age_seconds=max_age, required_workflows=required_workflows) else 1


if __name__ == "__main__":
    sys.exit(main())
