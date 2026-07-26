from __future__ import annotations

import json
from types import MappingProxyType

from src.models.tv_destination import TvDecision, TvPlan
from src.services.tv_destination_executor import TvDestinationExecutionResult
from src.services.tv_sync_report import write_tv_sync_reports


def test_tv_report_is_redacted_json_and_markdown(tmp_path) -> None:
    plan = TvPlan(
        "generation-1",
        (TvDecision("a", "plex_watchlist", "plex_add", 100, 1, "safe", tmdb_id=200, imdb_id="tt0000200"),),
        MappingProxyType({100: 1}),
        (),
        True,
    )
    execution = TvDestinationExecutionResult(plan, ("completed",), ())

    paths = write_tv_sync_reports(
        execution,
        blockers=("tv_apply_disabled",),
        report_dir=tmp_path,
        run_id=7,
        mode="report_only",
    )

    payload = json.loads(paths.json_path.read_text(encoding="utf-8"))
    assert payload["generation_id"] == "generation-1"
    assert payload["actions"][0]["tvdb_id"] == 100
    assert "secret" not in paths.json_path.read_text(encoding="utf-8").lower()
    assert "token" not in paths.markdown_path.read_text(encoding="utf-8").lower()
    assert "TV synchronization report" in paths.markdown_path.read_text(encoding="utf-8")
