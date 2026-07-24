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
