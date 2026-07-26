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


def test_rollout_ledger_does_not_claim_unperformed_destination_stages() -> None:
    ledger = (ROOT / "docs/reports/tv_integration_rollout.md").read_text(encoding="utf-8")

    assert "No report-only, adoption, reversible apply," in ledger
    assert "| Report-only TV collection |" in ledger
    assert "| Supervised existing-Sonarr adoption |" in ledger
    assert "| Supervised reversible apply |" in ledger
    assert "| Second convergence pass |" in ledger
