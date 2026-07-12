from __future__ import annotations

import sys
from datetime import datetime, timezone
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.services.movie_sync_executor import MovieSyncExecutor
from src.services.sync_reconciliation import (
    ReconciliationDecision,
    ReconciliationMovie,
    SyncReconciliationReport,
)


def decision(area: str, action: str, tmdb_id: int, *, reason: str = "test"):
    return ReconciliationDecision(
        area=area,
        action=action,
        movie=ReconciliationMovie(
            title=f"Movie {tmdb_id}",
            year=2024,
            tmdb_id=tmdb_id,
        ),
        reason=reason,
    )


def report(*decisions: ReconciliationDecision):
    return SyncReconciliationReport(
        generated_at=datetime(2026, 7, 11, 8, 0, tzinfo=timezone.utc),
        source_counts={},
        decisions=list(decisions),
    )


class FakeRadarr:
    def __init__(self, fail_add: bool = False):
        self.calls = []
        self.fail_add = fail_add

    def add_movie(
        self,
        tmdb_id: int,
        title: str,
        year: int,
        override_exclusion: bool = False,
    ):
        self.calls.append(("add", tmdb_id, title, year, override_exclusion))
        if self.fail_add:
            raise RuntimeError("radarr failed")
        return {"id": tmdb_id}

    def remove_movie(self, tmdb_id: int, delete_files: bool):
        self.calls.append(("remove", tmdb_id, delete_files))
        return True


class FakePlex:
    def __init__(self, add_result: bool = True):
        self.calls = []
        self.add_result = add_result

    def add_to_watchlist(self, tmdb_id: int, title: str, year: int):
        self.calls.append(("add", tmdb_id, title, year))
        return self.add_result

    def remove_from_watchlist(self, tmdb_id: int, title: str):
        self.calls.append(("remove", tmdb_id, title))
        return True

    def delete_from_library(self, *args, **kwargs):
        raise AssertionError("automatic sync must never delete Plex library media")


class FakeCache:
    def __init__(self):
        self.calls = []

    def mark_managed(self, destination: str, tmdb_id: int, action: str):
        self.calls.append(("mark", destination, tmdb_id, action))

    def release_managed(self, destination: str, tmdb_id: int):
        self.calls.append(("release", destination, tmdb_id))


def test_executor_dry_run_calls_no_mutating_client_method():
    radarr = FakeRadarr()
    plex = FakePlex()
    cache = FakeCache()
    executor = MovieSyncExecutor(radarr, plex, cache)

    result = executor.execute(
        report(
            decision("radarr", "add", 1),
            decision("plex_watchlist", "remove", 2),
        ),
        blockers=[],
        apply=False,
    )

    assert radarr.calls == []
    assert plex.calls == []
    assert cache.calls == []
    assert {item.execution_status for item in result.report.decisions} == {"dry_run"}


def test_executor_policy_blockers_call_no_mutating_client_method():
    radarr = FakeRadarr()
    plex = FakePlex()
    cache = FakeCache()
    executor = MovieSyncExecutor(radarr, plex, cache)

    result = executor.execute(
        report(decision("radarr", "add", 1)),
        blockers=["source_snapshot_stale"],
        apply=True,
    )

    assert radarr.calls == []
    assert plex.calls == []
    assert cache.calls == []
    assert result.report.decisions[0].execution_status == "blocked"


def test_executor_applies_safe_actions_and_updates_ownership():
    radarr = FakeRadarr()
    plex = FakePlex()
    cache = FakeCache()
    executor = MovieSyncExecutor(radarr, plex, cache)

    result = executor.execute(
        report(
            decision("radarr", "add", 1),
            decision("radarr", "remove", 2),
            decision(
                "radarr",
                "keep",
                3,
                reason="desired_radarr_movie_present_adopt_managed",
            ),
            decision("plex_watchlist", "add", 4),
            decision("plex_watchlist", "remove", 5),
        ),
        blockers=[],
        apply=True,
    )

    assert result.errors == ()
    assert radarr.calls == [
        ("add", 1, "Movie 1", 2024, False),
        ("remove", 2, False),
    ]
    assert plex.calls == [
        ("add", 4, "Movie 4", 2024),
        ("remove", 5, "Movie 5"),
    ]
    assert cache.calls == [
        ("mark", "radarr", 1, "add"),
        ("release", "radarr", 2),
        ("mark", "radarr", 3, "adopt"),
        ("mark", "plex_watchlist", 4, "add"),
        ("release", "plex_watchlist", 5),
    ]
    assert {item.execution_status for item in result.report.decisions} == {"completed"}


def test_executor_overrides_only_a_planned_radarr_exclusion():
    radarr = FakeRadarr()
    executor = MovieSyncExecutor(radarr, FakePlex(), FakeCache())

    result = executor.execute(
        report(
            decision(
                "radarr",
                "add",
                101,
                reason="desired_radarr_movie_missing_override_exclusion",
            )
        ),
        blockers=[],
        apply=True,
    )

    assert result.errors == ()
    assert radarr.calls == [("add", 101, "Movie 101", 2024, True)]


def test_executor_records_error_and_continues_with_independent_action():
    radarr = FakeRadarr(fail_add=True)
    plex = FakePlex()
    cache = FakeCache()
    executor = MovieSyncExecutor(radarr, plex, cache)

    result = executor.execute(
        report(
            decision("radarr", "add", 1),
            decision("plex_watchlist", "add", 2),
        ),
        blockers=[],
        apply=True,
    )

    assert result.errors == ("radarr/add/1: radarr failed",)
    assert [item.execution_status for item in result.report.decisions] == [
        "error",
        "completed",
    ]
    assert plex.calls == [("add", 2, "Movie 2", 2024)]


def test_executor_turns_unresolved_plex_identity_into_reported_skip():
    radarr = FakeRadarr()
    plex = FakePlex(add_result=False)
    cache = FakeCache()
    executor = MovieSyncExecutor(radarr, plex, cache)

    result = executor.execute(
        report(decision("plex_watchlist", "add", 101)),
        blockers=[],
        apply=True,
    )

    resolved = result.report.decisions[0]
    assert result.errors == ()
    assert resolved.action == "skip"
    assert resolved.reason == "plex_discovery_identity_not_found"
    assert resolved.execution_status == "skipped"
    assert cache.calls == []
