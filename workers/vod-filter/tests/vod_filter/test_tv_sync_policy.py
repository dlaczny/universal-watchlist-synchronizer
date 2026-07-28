from __future__ import annotations

from datetime import datetime, timedelta, timezone
from types import MappingProxyType

from src.models.tv_destination import TvDecision, TvPlan
from src.models.tv_sync import TvSnapshot
from src.services.tv_sync_policy import TvSyncPolicy, evaluate_tv_plan


NOW = datetime(2026, 7, 26, 12, tzinfo=timezone.utc)


def snapshot(*, capable: bool = True, published_at: datetime = NOW) -> TvSnapshot:
    return TvSnapshot("2", "generation-1", published_at, NOW, "scheduled_full", capable, ())


def decision(action: str = "plex_add", *, tvdb_id: int | None = 100, action_id: str = "one") -> TvDecision:
    return TvDecision(
        action_id,
        "plex_watchlist",
        action,
        tvdb_id,
        1,
        "test",
        title="Example",
    )


def plan(*decisions: TvDecision, errors: tuple[str, ...] = ()) -> TvPlan:
    return TvPlan("generation-1", decisions, MappingProxyType({}), errors, not errors)


def test_apply_requires_cli_flag_and_host_gate() -> None:
    blockers = evaluate_tv_plan(
        plan(decision()),
        TvSyncPolicy(enabled=True, apply_enabled=False),
        snapshot=snapshot(),
        apply_requested=True,
        now=NOW,
    )

    assert blockers == ["tv_apply_disabled"]


def test_policy_blocks_stale_and_incomplete_destinations() -> None:
    blockers = evaluate_tv_plan(
        plan(decision(), errors=("sonarr: unavailable",)),
        TvSyncPolicy(enabled=True, apply_enabled=True, max_snapshot_age_minutes=10),
        snapshot=snapshot(capable=False, published_at=NOW - timedelta(minutes=11)),
        apply_requested=True,
        now=NOW,
    )

    assert blockers == ["tv_snapshot_stale", "tv_collection_errors"]


def test_policy_rejects_duplicate_actions_and_missing_plex_identity() -> None:
    blockers = evaluate_tv_plan(
        plan(decision(action_id="same"), decision(action_id="same", tvdb_id=None)),
        TvSyncPolicy(enabled=True, apply_enabled=True),
        snapshot=snapshot(),
        apply_requested=True,
        now=NOW,
    )

    assert blockers == ["tv_duplicate_actions", "tv_action_identity_invalid", "plex_identity_missing"]


def test_policy_blocks_unknown_provider_for_sonarr_and_adoption_without_gate() -> None:
    uncertain = TvDecision("sonarr", "sonarr", "uncertain", 100, 1, "sonarr_provider_availability_unknown")
    adoption = TvDecision("adopt", "sonarr", "sonarr_adoption_candidate", 200, 1, "existing_sonarr_series_not_owned")

    blockers = evaluate_tv_plan(
        plan(uncertain, adoption),
        TvSyncPolicy(enabled=True, apply_enabled=True, adoption_enabled=False),
        snapshot=snapshot(),
        apply_requested=True,
        now=NOW,
    )

    assert blockers == ["sonarr_provider_availability_uncertain", "tv_adoption_disabled"]


def test_report_only_keeps_apply_blocker_but_has_no_effective_failure() -> None:
    blockers = evaluate_tv_plan(
        plan(decision()),
        TvSyncPolicy(enabled=True, apply_enabled=False),
        snapshot=snapshot(),
        apply_requested=False,
        now=NOW,
    )

    assert blockers == ["tv_apply_disabled"]


def test_policy_does_not_treat_legacy_mutation_lock_as_destination_incapable() -> None:
    blockers = evaluate_tv_plan(
        plan(decision()),
        TvSyncPolicy(enabled=True, apply_enabled=False),
        snapshot=snapshot(capable=False),
        apply_requested=False,
        now=NOW,
    )

    assert blockers == ["tv_apply_disabled"]


def test_policy_rejects_plex_add_without_discovery_title() -> None:
    missing_title = TvDecision(
        "missing-title",
        "plex_watchlist",
        "plex_add",
        100,
        1,
        "test",
    )

    blockers = evaluate_tv_plan(
        plan(missing_title),
        TvSyncPolicy(enabled=True, apply_enabled=True),
        snapshot=snapshot(),
        apply_requested=True,
        now=NOW,
    )

    assert blockers == ["tv_action_identity_invalid", "plex_identity_missing"]
