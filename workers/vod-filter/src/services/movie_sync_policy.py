"""Safety policy for applying a computed movie sync plan."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone

from src.services.sync_reconciliation import SyncReconciliationReport


@dataclass(frozen=True)
class SyncPolicy:
    """Limits that must be satisfied before any external mutation."""

    max_source_age_minutes: int = 120
    max_removal_count: int = 10
    max_removal_percent: float = 25.0
    allow_mutation: bool = False

    def __post_init__(self) -> None:
        if self.max_source_age_minutes < 1:
            raise ValueError("max_source_age_minutes must be at least 1")
        if self.max_removal_count < 0:
            raise ValueError("max_removal_count cannot be negative")
        if not 0 <= self.max_removal_percent <= 100:
            raise ValueError("max_removal_percent must be between 0 and 100")


def evaluate_plan(
    report: SyncReconciliationReport,
    policy: SyncPolicy,
    *,
    now: datetime | None = None,
) -> list[str]:
    """Return stable reason codes that block mutation for this plan."""

    current_time = now or datetime.now(timezone.utc)
    blockers: list[str] = []

    if not policy.allow_mutation:
        blockers.append("mutation_disabled")

    if report.source_last_successful_sync_at is None:
        blockers.append("source_freshness_unknown")
    else:
        source_age = current_time - report.source_last_successful_sync_at
        if source_age.total_seconds() > policy.max_source_age_minutes * 60:
            blockers.append("source_snapshot_stale")

    if any(
        decision.area == "collection" and decision.action == "error"
        for decision in report.decisions
    ):
        blockers.append("collection_errors")

    if any(
        decision.area == "source_identity"
        and decision.action in {"skip", "uncertain", "error"}
        for decision in report.decisions
    ):
        blockers.append("invalid_source_identity")

    if (
        report.backend_snapshot_provided
        and report.source_counts.get("backend_snapshot", 0) == 0
        and report.managed_destination_count > 0
    ):
        blockers.append("unexpected_empty_source")

    removals = [
        decision for decision in report.decisions if decision.action == "remove"
    ]
    if len(removals) > policy.max_removal_count:
        blockers.append("removal_count_exceeded")

    live_destination_count = (
        report.source_counts.get("radarr", 0)
        + report.source_counts.get("plex_watchlist", 0)
    )
    removal_percent = (
        len(removals) / live_destination_count * 100
        if live_destination_count > 0
        else 0.0
    )
    if removal_percent > policy.max_removal_percent:
        blockers.append("removal_percentage_exceeded")

    return blockers
