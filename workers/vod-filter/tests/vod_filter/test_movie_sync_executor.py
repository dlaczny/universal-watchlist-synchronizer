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


def decision(
    area: str,
    action: str,
    tmdb_id: int,
    *,
    reason: str = "test",
    **kwargs,
):
    return ReconciliationDecision(
        area=area,
        action=action,
        movie=ReconciliationMovie(
            title=f"Movie {tmdb_id}",
            year=2024,
            tmdb_id=tmdb_id,
        ),
        reason=reason,
        **kwargs,
    )


def report(*decisions: ReconciliationDecision):
    return SyncReconciliationReport(
        generated_at=datetime(2026, 7, 11, 8, 0, tzinfo=timezone.utc),
        source_counts={},
        decisions=list(decisions),
    )


class FakeRadarr:
    def __init__(self, fail_add: bool = False, fail_remove: bool = False):
        self.calls = []
        self.fail_add = fail_add
        self.fail_remove = fail_remove

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
        if self.fail_remove:
            raise RuntimeError("radarr remove failed")
        return True


class FakePlex:
    def __init__(self, add_result: bool = True, fail_remove: bool = False):
        self.calls = []
        self.add_result = add_result
        self.fail_remove = fail_remove

    def add_to_watchlist(self, tmdb_id: int, title: str, year: int):
        self.calls.append(("add", tmdb_id, title, year))
        return self.add_result

    def remove_from_watchlist(self, tmdb_id: int, title: str):
        self.calls.append(("remove", tmdb_id, title))
        if self.fail_remove:
            raise RuntimeError("plex remove failed")
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

    def mark_radarr_removed_by_worker(self, tmdb_id: int, source_event_id: str):
        self.calls.append(("radarr_removed", tmdb_id, source_event_id))

    def record_cleanup_attempt(self, **kwargs):
        self.calls.append(("cleanup", kwargs))
        return 1


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


def test_executor_applies_exact_watched_radarr_removal_with_files_and_audit():
    radarr = FakeRadarr()
    cache = FakeCache()
    executor = MovieSyncExecutor(radarr, FakePlex(), cache)
    watched = decision(
        "radarr",
        "remove",
        101,
        reason="watched_letterboxd_movie_remove_from_radarr",
        delete_files=True,
        authorization="letterboxd_watched",
        authorization_event_id="movie-101:watched:2",
    )

    result = executor.execute(report(watched), blockers=[], apply=True)

    assert result.errors == ()
    assert radarr.calls == [("remove", 101, True)]
    assert cache.calls == [
        ("radarr_removed", 101, "movie-101:watched:2"),
        ("release", "radarr", 101),
        (
            "cleanup",
            {
                "authorization": "letterboxd_watched",
                "authorization_event_id": "movie-101:watched:2",
                "destination": "radarr",
                "tmdb_id": 101,
                "delete_files": True,
                "status": "completed",
                "error": None,
            },
        ),
    ]


def test_executor_rejects_invalid_file_deletion_before_radarr_call():
    radarr = FakeRadarr()
    cache = FakeCache()
    executor = MovieSyncExecutor(radarr, FakePlex(), cache)
    invalid = decision(
        "radarr",
        "remove",
        101,
        delete_files=True,
        authorization="manual_radarr_removal",
    )

    result = executor.execute(report(invalid), blockers=[], apply=True)

    assert result.errors == (
        "radarr/remove/101: invalid file deletion authorization",
    )
    assert radarr.calls == []
    assert cache.calls == []


def test_executor_rejects_file_deletion_flag_on_non_remove_decision():
    radarr = FakeRadarr()
    plex = FakePlex()
    cache = FakeCache()
    executor = MovieSyncExecutor(radarr, plex, cache)
    invalid = decision(
        "plex_watchlist",
        "skip",
        101,
        delete_files=True,
        authorization="letterboxd_watched",
        authorization_event_id="movie-101:watched:2",
    )

    result = executor.execute(report(invalid), blockers=[], apply=True)

    assert result.errors == (
        "plex_watchlist/skip/101: invalid file deletion authorization",
    )
    assert radarr.calls == []
    assert plex.calls == []
    assert cache.calls == []


def test_executor_watched_plex_cleanup_uses_watchlist_only_and_records_audit():
    plex = FakePlex()
    cache = FakeCache()
    executor = MovieSyncExecutor(FakeRadarr(), plex, cache)
    watched = decision(
        "plex_watchlist",
        "remove",
        101,
        reason="watched_letterboxd_movie_remove_from_plex_watchlist",
        authorization="letterboxd_watched",
        authorization_event_id="movie-101:watched:2",
    )

    result = executor.execute(report(watched), blockers=[], apply=True)

    assert result.errors == ()
    assert plex.calls == [("remove", 101, "Movie 101")]
    assert cache.calls[0] == ("release", "plex_watchlist", 101)
    assert cache.calls[1][0] == "cleanup"
    assert cache.calls[1][1]["destination"] == "plex_watchlist"


def test_executor_records_failed_cleanup_and_continues_independent_destination():
    radarr = FakeRadarr(fail_remove=True)
    plex = FakePlex()
    cache = FakeCache()
    executor = MovieSyncExecutor(radarr, plex, cache)
    watched_radarr = decision(
        "radarr",
        "remove",
        101,
        delete_files=True,
        authorization="letterboxd_watched",
        authorization_event_id="movie-101:watched:2",
    )
    watched_plex = decision(
        "plex_watchlist",
        "remove",
        101,
        authorization="letterboxd_watched",
        authorization_event_id="movie-101:watched:2",
    )

    result = executor.execute(
        report(watched_radarr, watched_plex),
        blockers=[],
        apply=True,
    )

    assert result.errors == ("radarr/remove/101: radarr remove failed",)
    assert plex.calls == [("remove", 101, "Movie 101")]
    cleanup_attempts = [call[1] for call in cache.calls if call[0] == "cleanup"]
    assert [attempt["status"] for attempt in cleanup_attempts] == [
        "error",
        "completed",
    ]
    assert cleanup_attempts[0]["error"] == "radarr remove failed"
