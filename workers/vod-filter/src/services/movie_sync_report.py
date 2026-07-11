"""Machine-readable and operator-readable movie sync reports."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from src.services.sync_reconciliation import (
    SyncReconciliationReport,
    render_markdown_report,
)


@dataclass(frozen=True)
class MovieSyncReportPaths:
    json_path: Path
    markdown_path: Path


def write_movie_sync_reports(
    report: SyncReconciliationReport,
    blockers: list[str],
    report_dir: Path,
    run_id: int,
) -> MovieSyncReportPaths:
    """Write one redaction-safe JSON/Markdown report pair."""
    report_dir.mkdir(parents=True, exist_ok=True)
    base_name = f"movie-sync-{run_id}"
    json_path = report_dir / f"{base_name}.json"
    markdown_path = report_dir / f"{base_name}.md"

    payload = {
        "generated_at": report.generated_at.isoformat(),
        "source_snapshot_at": _iso(report.source_snapshot_at),
        "source_last_successful_sync_at": _iso(
            report.source_last_successful_sync_at
        ),
        "source_counts": report.source_counts,
        "blockers": blockers,
        "decisions": [
            {
                "area": decision.area,
                "action": decision.action,
                "reason": decision.reason,
                "managed": decision.managed,
                "execution_status": decision.execution_status,
                "movie": {
                    "tmdb_id": decision.movie.tmdb_id,
                    "imdb_id": decision.movie.imdb_id,
                    "title": decision.movie.title,
                    "year": decision.movie.year,
                },
            }
            for decision in report.decisions
        ],
    }
    json_path.write_text(
        json.dumps(payload, indent=2, sort_keys=True),
        encoding="utf-8",
    )

    blocker_lines = ["## Safety blockers", ""]
    blocker_lines.extend(
        [f"- {blocker}" for blocker in blockers] if blockers else ["- None"]
    )
    markdown = render_markdown_report(report)
    markdown_path.write_text(
        "\n".join(blocker_lines) + "\n\n" + markdown,
        encoding="utf-8",
    )
    return MovieSyncReportPaths(json_path, markdown_path)


def _iso(value):
    return value.isoformat() if value is not None else None
