from __future__ import annotations

import importlib
import os
import sys
from pathlib import Path

import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))


@pytest.fixture()
def run_all_syncs(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.chdir(VOD_FILTER_ROOT)
    module = importlib.import_module("run_all_syncs")
    return importlib.reload(module)


def test_dry_run_flag_is_propagated_to_child_syncs(
    monkeypatch: pytest.MonkeyPatch,
    run_all_syncs,
) -> None:
    seen = []

    def fake_cleanup():
        seen.append(("cleanup", os.environ.get("DRY_RUN")))
        return ("Cleanup", True, None)

    def fake_main():
        seen.append(("main", os.environ.get("DRY_RUN")))
        return ("Main Sync", True, None)

    def fake_library():
        seen.append(("library", os.environ.get("DRY_RUN")))
        return ("Library Sync", True, None)

    monkeypatch.setattr(run_all_syncs, "run_cleanup", fake_cleanup)
    monkeypatch.setattr(run_all_syncs, "run_main_sync", fake_main)
    monkeypatch.setattr(run_all_syncs, "run_library_sync", fake_library)
    monkeypatch.setattr(
        run_all_syncs,
        "run_with_history",
        lambda workflow, operation: operation(),
    )
    monkeypatch.setattr(sys, "argv", ["run_all_syncs.py", "--dry-run"])
    monkeypatch.delenv("DRY_RUN", raising=False)

    exit_code = run_all_syncs.main()

    assert exit_code == 0
    assert seen == [
        ("cleanup", "true"),
        ("main", "true"),
        ("library", "true"),
    ]


def test_combined_runner_is_sequential_by_default(
    monkeypatch: pytest.MonkeyPatch,
    run_all_syncs,
) -> None:
    calls = []

    def fake_cleanup():
        calls.append("cleanup")
        return ("Cleanup", True, None)

    def fake_main():
        calls.append("main")
        return ("Main Sync", True, None)

    def fake_library():
        calls.append("library")
        return ("Library Sync", True, None)

    monkeypatch.setattr(run_all_syncs, "run_cleanup", fake_cleanup)
    monkeypatch.setattr(run_all_syncs, "run_main_sync", fake_main)
    monkeypatch.setattr(run_all_syncs, "run_library_sync", fake_library)
    monkeypatch.setattr(
        run_all_syncs,
        "run_with_history",
        lambda workflow, operation: operation(),
    )
    monkeypatch.setattr(sys, "argv", ["run_all_syncs.py"])

    exit_code = run_all_syncs.main()

    assert exit_code == 0
    assert calls == ["cleanup", "main", "library"]


def test_imported_runner_can_ignore_parent_process_arguments(
    monkeypatch: pytest.MonkeyPatch,
    run_all_syncs,
) -> None:
    calls = []

    def fake_cleanup():
        calls.append("cleanup")
        return ("Cleanup", True, None)

    def fake_main():
        calls.append("main")
        return ("Main Sync", True, None)

    def fake_library():
        calls.append("library")
        return ("Library Sync", True, None)

    monkeypatch.setattr(run_all_syncs, "run_cleanup", fake_cleanup)
    monkeypatch.setattr(run_all_syncs, "run_main_sync", fake_main)
    monkeypatch.setattr(run_all_syncs, "run_library_sync", fake_library)
    monkeypatch.setattr(
        run_all_syncs,
        "run_with_history",
        lambda workflow, operation: operation(),
    )
    monkeypatch.setattr(sys, "argv", ["continuous_sync.py", "--continuous", "--interval", "3600"])

    exit_code = run_all_syncs.main(argv=[])

    assert exit_code == 0
    assert calls == ["cleanup", "main", "library"]


def test_runner_records_child_operation_history(monkeypatch: pytest.MonkeyPatch, run_all_syncs):
    records = []

    class FakeCacheService:
        def __init__(self, database_path):
            self.database_path = database_path

        def start_run(self, workflow, dry_run, trigger):
            records.append(("start", workflow, dry_run, trigger))
            return len(records)

        def finish_run(self, run_id, status, exit_code, summary=None, error=None):
            records.append(("finish", run_id, status, exit_code, summary, error))

    monkeypatch.setattr(run_all_syncs, "CacheService", FakeCacheService)
    monkeypatch.setenv("DATABASE_PATH", "data/test.db")
    monkeypatch.setenv("DRY_RUN", "true")

    result = run_all_syncs.run_with_history(
        workflow="cleanup",
        operation=lambda: ("Cleanup", True, None),
    )

    assert result == ("Cleanup", True, None)
    assert records == [
        ("start", "cleanup", True, "run_all_syncs"),
        ("finish", 1, "success", 0, {"name": "Cleanup"}, None),
    ]

