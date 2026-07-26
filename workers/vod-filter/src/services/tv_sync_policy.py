"""Fail-closed policy for the isolated reversible TV destination workflow."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone

from src.models.tv_destination import TvPlan
from src.models.tv_sync import TvSnapshot


@dataclass(frozen=True)
class TvSyncPolicy:
    """Host gates and bounded limits required before destination writes."""

    enabled: bool = False
    apply_enabled: bool = False
    adoption_enabled: bool = False
    max_snapshot_age_minutes: int = 30
    max_action_count: int = 100

    def __post_init__(self) -> None:
        if self.max_snapshot_age_minutes < 1:
            raise ValueError("max_snapshot_age_minutes must be at least 1")
        if self.max_action_count < 0:
            raise ValueError("max_action_count cannot be negative")


def evaluate_tv_plan(
    plan: TvPlan,
    policy: TvSyncPolicy,
    *,
    snapshot: TvSnapshot | None,
    apply_requested: bool,
    now: datetime | None = None,
) -> list[str]:
    """Return stable blockers; report-only deliberately retains apply gating."""
    blockers: list[str] = []
    current_time = now or datetime.now(timezone.utc)

    if not policy.enabled:
        blockers.append("tv_sync_disabled")
    if not policy.apply_enabled:
        blockers.append("tv_apply_disabled")
    if snapshot is None or not snapshot.mutation_capable:
        blockers.append("tv_snapshot_incapable")
    if snapshot is not None and current_time - snapshot.published_at > _minutes(policy.max_snapshot_age_minutes):
        blockers.append("tv_snapshot_stale")
    if plan.collection_errors or not plan.applyable:
        blockers.append("tv_collection_errors")

    executable = [
        item
        for item in plan.decisions
        if item.action not in {"keep", "skip", "uncertain", "sonarr_adoption_candidate"}
    ]
    action_ids = [item.action_id for item in plan.decisions]
    if len(set(action_ids)) != len(action_ids):
        blockers.append("tv_duplicate_actions")
    mutation_actions = {
        "sonarr_add",
        "sonarr_monitor_series",
        "sonarr_monitor_season",
        "sonarr_search_episodes",
        "sonarr_adoption_candidate",
        "plex_add",
        "plex_remove",
    }
    invalid_identity_actions = [
        item for item in plan.decisions if item.action in mutation_actions and _invalid_identity(item)
    ]
    if invalid_identity_actions:
        blockers.append("tv_action_identity_invalid")
    if any(item.action in {"plex_add", "plex_remove"} for item in invalid_identity_actions):
        blockers.append("plex_identity_missing")
    if any(
        item.destination == "sonarr"
        and item.action == "uncertain"
        and item.reason in {
            "sonarr_provider_availability_unknown",
            "sonarr_provider_availability_stale",
        }
        for item in plan.decisions
    ):
        blockers.append("sonarr_provider_availability_uncertain")
    if len(executable) > policy.max_action_count:
        blockers.append("tv_action_count_exceeded")
    if apply_requested and any(
        item.action == "sonarr_adoption_candidate" for item in plan.decisions
    ) and not policy.adoption_enabled:
        blockers.append("tv_adoption_disabled")
    return blockers


def report_only_blockers(blockers: list[str] | tuple[str, ...]) -> tuple[str, ...]:
    """The host apply switch alone must not fail a report-only run."""
    return tuple(
        blocker
        for blocker in blockers
        if blocker not in {"tv_apply_disabled", "tv_adoption_disabled"}
    )


def _minutes(value: int):
    from datetime import timedelta

    return timedelta(minutes=value)


def _invalid_identity(decision) -> bool:
    """Validate every identity that would cross a destination boundary."""
    tvdb_id = decision.tvdb_id
    if not isinstance(tvdb_id, int) or isinstance(tvdb_id, bool) or tvdb_id <= 0:
        return True
    if decision.action not in {"plex_add", "plex_remove"}:
        return False
    if decision.tmdb_id is not None and (
        not isinstance(decision.tmdb_id, int)
        or isinstance(decision.tmdb_id, bool)
        or decision.tmdb_id <= 0
    ):
        return True
    imdb_id = decision.imdb_id
    return imdb_id is not None and (
        not isinstance(imdb_id, str)
        or not imdb_id.startswith("tt")
        or not imdb_id[2:].isdigit()
        or int(imdb_id[2:]) <= 0
    )
