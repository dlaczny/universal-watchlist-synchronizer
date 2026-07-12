from __future__ import annotations

import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from types import SimpleNamespace


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

import sync_movies as sync_movies_module
from sync_movies import execute_movie_sync
from src.services.movie_sync_collector import CollectedMovieSyncState
from src.services.movie_sync_policy import SyncPolicy


NOW = datetime(2026, 7, 11, 8, 0, tzinfo=timezone.utc)


class FakeCollector:
    def collect(self, *, sync_first: bool):
        assert sync_first is True
        return CollectedMovieSyncState(
            backend_movies=(
                {
                    "tmdb_id": 101,
                    "title": "Desired",
                    "year": 2024,
                    "metadata_status": "enriched",
                    "radarr_eligible": True,
                },
            ),
            backend_watched_movies=(),
            source_snapshot_id="letterboxd-42",
            source_snapshot_at=NOW,
            source_last_successful_sync_at=NOW,
            radarr_movies=(),
            radarr_observations=(),
            plex_watchlist_movies=(),
            plex_library_movies=(),
            managed_destinations=(),
            collection_errors=(),
        )


class FakeExecutor:
    def __init__(self):
        self.calls = []

    def execute(self, report, blockers, apply):
        self.calls.append((blockers, apply))
        from src.services.movie_sync_executor import MovieSyncExecutionResult

        return MovieSyncExecutionResult(report=report, errors=())


class FakeCache:
    def __init__(self):
        self.finished = []

    def start_run(self, workflow: str, dry_run: bool, trigger: str):
        assert (workflow, trigger) == ("movie_sync", "sync_movies")
        return 7

    def finish_run(self, run_id, status, exit_code, summary=None, error=None):
        self.finished.append((run_id, status, exit_code, summary, error))


def test_execute_movie_sync_writes_json_and_markdown_dry_run(tmp_path: Path):
    executor = FakeExecutor()
    cache = FakeCache()

    result = execute_movie_sync(
        collector=FakeCollector(),
        executor=executor,
        cache_service=cache,
        policy=SyncPolicy(allow_mutation=False),
        sync_first=True,
        apply=False,
        report_dir=tmp_path,
        now=NOW,
    )

    assert result.exit_code == 0
    assert result.json_path.exists()
    assert result.markdown_path.exists()
    payload = json.loads(result.json_path.read_text(encoding="utf-8"))
    assert payload["blockers"] == ["mutation_disabled"]
    assert payload["decisions"][0]["action"] == "add"
    assert executor.calls == [(["mutation_disabled"], False)]
    assert cache.finished[0][1:3] == ("success", 0)


def test_execute_movie_sync_returns_safety_code_for_stale_apply(tmp_path: Path):
    class StaleCollector(FakeCollector):
        def collect(self, *, sync_first: bool):
            state = super().collect(sync_first=sync_first)
            return CollectedMovieSyncState(
                **{
                    **state.__dict__,
                    "source_last_successful_sync_at": datetime(
                        2026, 7, 10, 8, 0, tzinfo=timezone.utc
                    ),
                }
            )

    executor = FakeExecutor()

    result = execute_movie_sync(
        collector=StaleCollector(),
        executor=executor,
        cache_service=FakeCache(),
        policy=SyncPolicy(allow_mutation=True, max_source_age_minutes=120),
        sync_first=True,
        apply=True,
        report_dir=tmp_path,
        now=NOW,
    )

    assert result.exit_code == 3
    assert "source_snapshot_stale" in result.blockers
    assert executor.calls == [(["source_snapshot_stale"], True)]


def test_execute_movie_sync_reports_watched_authorization_contract(tmp_path: Path):
    class WatchedCollector:
        def collect(self, *, sync_first: bool):
            return CollectedMovieSyncState(
                backend_movies=(),
                backend_watched_movies=(
                    {
                        "tmdb_id": 101,
                        "title": "Watched",
                        "year": 2024,
                        "source_id": "101",
                        "watched_at": NOW,
                        "lifecycle_version": 2,
                        "lifecycle_event_id": "movie-101:watched:2",
                    },
                ),
                source_snapshot_id="letterboxd-42",
                source_snapshot_at=NOW,
                source_last_successful_sync_at=NOW,
                radarr_movies=(
                    {"tmdbId": 101, "title": "Watched", "year": 2024, "hasFile": True},
                ),
                radarr_observations=(),
                plex_watchlist_movies=(),
                plex_library_movies=(),
                managed_destinations=(),
                collection_errors=(),
            )

    result = execute_movie_sync(
        collector=WatchedCollector(),
        executor=FakeExecutor(),
        cache_service=FakeCache(),
        policy=SyncPolicy(
            allow_mutation=False,
            allow_watched_file_deletion=True,
            max_removal_percent=100.0,
        ),
        sync_first=True,
        apply=False,
        report_dir=tmp_path,
        now=NOW,
    )

    payload = json.loads(result.json_path.read_text(encoding="utf-8"))
    radarr = next(
        item
        for item in payload["decisions"]
        if item["area"] == "radarr" and item["action"] == "remove"
    )
    assert payload["source_snapshot_id"] == "letterboxd-42"
    assert radarr["authorization"] == "letterboxd_watched"
    assert radarr["authorization_event_id"] == "movie-101:watched:2"
    assert radarr["delete_files"] is True
    markdown = result.markdown_path.read_text(encoding="utf-8")
    assert "Source snapshot ID: letterboxd-42" in markdown
    assert "authorization=letterboxd_watched" in markdown
    assert "authorization_event_id=movie-101:watched:2" in markdown
    assert "delete_files=true" in markdown
    assert "MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION" not in markdown


def test_build_movie_sync_policy_passes_watched_file_deletion_gate():
    config = SimpleNamespace(
        movie_sync_max_source_age_minutes=120,
        movie_sync_max_removal_count=10,
        movie_sync_max_removal_percent=25.0,
        movie_sync_allow_watched_file_deletion=True,
    )

    policy = sync_movies_module.build_movie_sync_policy(config, apply=True)

    assert policy.allow_mutation is True
    assert policy.allow_watched_file_deletion is True
