from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from types import MappingProxyType

from src.models.tv_destination import TvCollectedState, TvDecision, TvPlan
from src.models.tv_sync import TvSnapshot
from sync_tv import execute_tv_sync, parse_args


NOW = datetime(2026, 7, 26, 12, tzinfo=timezone.utc)


class FakeCollector:
    def collect(self):
        return TvCollectedState(
            TvSnapshot("2", "generation-1", NOW, NOW, "scheduled_full", True, ()),
            (), (), (), frozenset(), (), (),
        )


class FakeStateStore:
    def __init__(self) -> None:
        self.finished: list[tuple[int, str]] = []

    def start_run(self, generation_id: str) -> int:
        assert generation_id == "generation-1"
        return 3

    def finish_run(self, run_id: int, status: str) -> None:
        self.finished.append((run_id, status))


class FakeExecutor:
    def execute(self, plan, blockers, apply, adopt):
        from src.services.tv_destination_executor import TvDestinationExecutionResult
        return TvDestinationExecutionResult(plan, ("dry_run",), ())


def test_execute_tv_sync_allows_report_only_apply_blocker_and_writes_reports(tmp_path: Path) -> None:
    store = FakeStateStore()
    result = execute_tv_sync(
        collector=FakeCollector(),
            planner=lambda collected: TvPlan(
                "generation-1",
                (
                    TvDecision(
                        "a",
                        "plex_watchlist",
                        "plex_add",
                        100,
                        1,
                        "safe",
                        title="Example",
                    ),
                ),
                MappingProxyType({}),
                (),
                True,
            ),
        executor=FakeExecutor(),
        state_store=store,
        policy=__import__("src.services.tv_sync_policy", fromlist=["TvSyncPolicy"]).TvSyncPolicy(enabled=True, apply_enabled=False),
        apply_requested=False,
        report_dir=tmp_path,
        now=NOW,
    )

    assert result.exit_code == 0
    assert result.blockers == ("tv_apply_disabled",)
    assert result.json_path.exists()
    assert store.finished == [(3, "completed")]


def test_parse_args_supports_required_tv_flags() -> None:
    args = parse_args(["--apply", "--report-dir", "reports", "--quiet", "--once"])

    assert args.apply is True
    assert args.report_dir == "reports"
    assert args.quiet is True
    assert args.once is True
