from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def test_tv_operations_document_report_only_before_apply() -> None:
    runbook = (ROOT / "docs/runbooks/tv_sync_operations.md").read_text(encoding="utf-8")

    assert "TV_SYNC_ENABLED=true" in runbook
    assert "TV_SYNC_APPLY=false" in runbook
    assert "TV_SYNC_ADOPT_EXISTING_DESTINATIONS=true" in runbook
    assert "Sonarr deletion" not in runbook.split("Reversible destination rollout", 1)[1]


def test_tv_contract_docs_describe_schema_v2_and_exact_identity() -> None:
    export = (ROOT / "docs/apis/export_endpoints.md").read_text(encoding="utf-8")
    read_model = (ROOT / "docs/architecture/tv_sync_read_model.md").read_text(
        encoding="utf-8"
    )

    assert "schema-version-2" in export
    assert "destinationSync" in export
    assert "TVDB is mandatory for a Sonarr action" in export
    assert "An unstarted show selects Season 1" in read_model


def test_rollout_ledger_records_completed_reversible_destination_stages() -> None:
    ledger = (ROOT / "docs/reports/tv_integration_rollout.md").read_text(encoding="utf-8")

    assert "57f23f0805dcd0e98703e39bfb9cd57e84641192" in ledger
    assert "| Report-only TV collection |" in ledger
    assert "| Supervised existing-Sonarr adoption |" in ledger
    assert "| Supervised reversible apply |" in ledger
    assert "| Second convergence pass |" in ledger
    assert ledger.count("Completed 2026-07-29") == 8
    assert "`TRAKT_HISTORY_SYNC_APPLY=false`" in ledger
    assert "all three TV cleanup/deletion gates false" in ledger


def test_roadmap_points_to_current_reversible_destination_plan() -> None:
    roadmap = (ROOT / "docs" / "backlog" / "roadmap.md").read_text(encoding="utf-8")

    assert "2026-07-24-tv-reversible-destination-sync.md" in roadmap
    assert "2026-07-13-tv-phase-3-reversible-destinations.md" not in roadmap


def test_rollout_ledger_records_redacted_production_evidence() -> None:
    rollout_ledger = (ROOT / "docs" / "reports" / "tv_integration_rollout.md").read_text(
        encoding="utf-8"
    )

    for evidence in (
        "Trakt connected",
        "First complete generation published",
        "251/251",
        "The release remains read-only.",
    ):
        assert evidence in rollout_ledger


def test_rollout_ledger_allows_audit_pointers_but_prohibits_secret_material() -> None:
    rollout_ledger = (ROOT / "docs" / "reports" / "tv_integration_rollout.md").read_text(
        encoding="utf-8"
    )
    policy = " ".join(rollout_ledger.split())

    assert "non-secret generation identifiers or publish pointers" in policy
    for prohibited_material in (
        "access/refresh tokens",
        "client secrets",
        "authorization headers",
        "raw private upstream payloads",
    ):
        assert prohibited_material in rollout_ledger


def test_historical_phase_order_gate_does_not_block_active_destination_plan() -> None:
    program = (
        ROOT / "docs" / "superpowers" / "plans" / "2026-07-13-tv-integration-program.md"
    ).read_text(encoding="utf-8")

    assert "Historical Phase Order And Entry Gates (Superseded for Active Release Ordering)" in program
    assert "does not block the active 2026-07-24 destination plan" in program
    for blocked_phase in (
        "Phase 4: Concluded-Season Cleanup (blocked)",
        "Phase 5: Terminal-Series Cleanup And Revival (blocked)",
    ):
        assert blocked_phase in program


def test_active_destination_plan_allows_non_secret_audit_pointers() -> None:
    active_plan = (
        ROOT / "docs" / "superpowers" / "plans" / "2026-07-24-tv-reversible-destination-sync.md"
    ).read_text(encoding="utf-8")
    policy = " ".join(active_plan.split())

    assert "non-secret generation identifier or publish pointer" in policy
    for prohibited_material in (
        "credentials",
        "token material",
        "titles",
        "raw private upstream payloads",
    ):
        assert prohibited_material in active_plan
