from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


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
