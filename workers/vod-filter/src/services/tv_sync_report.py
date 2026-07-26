"""Redacted local reports for an isolated TV destination run."""

from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from src.services.tv_destination_executor import TvDestinationExecutionResult


@dataclass(frozen=True)
class TvSyncReportPaths:
    json_path: Path
    markdown_path: Path


def write_tv_sync_reports(
    execution: TvDestinationExecutionResult,
    *,
    blockers: tuple[str, ...] | list[str],
    report_dir: Path,
    run_id: int,
    mode: str,
    written_at: datetime | None = None,
) -> TvSyncReportPaths:
    """Write only stable identifiers, outcomes, and reasons -- never boundary payloads."""
    if mode not in {"report_only", "apply"}:
        raise ValueError("TV report mode is invalid")
    timestamp = written_at or datetime.now(timezone.utc)
    report_dir.mkdir(parents=True, exist_ok=True)
    stem = f"tv-sync-{run_id}-{timestamp.strftime('%Y%m%dT%H%M%SZ')}"
    rows = [
        {
            "action_id": decision.action_id,
            "destination": decision.destination,
            "action": decision.action,
            "tvdb_id": decision.tvdb_id,
            "tmdb_id": decision.tmdb_id,
            "imdb_id": decision.imdb_id,
            "selected_season_number": decision.selected_season_number,
            "reason": decision.reason,
            "status": status,
        }
        for decision, status in zip(execution.plan.decisions, execution.statuses, strict=True)
    ]
    payload = {
        "workflow": "tv_sync",
        "run_id": run_id,
        "generation_id": execution.plan.generation_id,
        "mode": mode,
        "blockers": list(blockers),
        "collection_errors": list(execution.plan.collection_errors),
        "action_counts": _counts(rows),
        "actions": rows,
    }
    json_path = report_dir / f"{stem}.json"
    markdown_path = report_dir / f"{stem}.md"
    json_path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    markdown_path.write_text(_markdown(payload), encoding="utf-8")
    return TvSyncReportPaths(json_path, markdown_path)


def _counts(rows: list[dict[str, object]]) -> dict[str, int]:
    counts: dict[str, int] = {}
    for row in rows:
        status = str(row["status"])
        counts[status] = counts.get(status, 0) + 1
    return dict(sorted(counts.items()))


def _markdown(payload: dict[str, object]) -> str:
    lines = [
        "# TV synchronization report",
        "",
        f"- Run: {payload['run_id']}",
        f"- Generation: {payload['generation_id']}",
        f"- Mode: {payload['mode']}",
        f"- Blockers: {', '.join(payload['blockers']) or 'none'}",
        "",
        "## Actions",
        "",
        "| Destination | Action | TVDB | Season | Status | Reason |",
        "| --- | --- | ---: | ---: | --- | --- |",
    ]
    for row in payload["actions"]:  # type: ignore[index]
        lines.append(
            "| {destination} | {action} | {tvdb_id} | {selected_season_number} | {status} | {reason} |".format(**row)
        )
    return "\n".join(lines) + "\n"
