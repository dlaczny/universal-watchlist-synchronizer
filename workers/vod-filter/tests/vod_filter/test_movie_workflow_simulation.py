from __future__ import annotations

import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.main import EXIT_SUCCESS, run_movie_sync_workflow
from src.models.movie import Movie
from src.services.movie_sync_policy import SyncPolicy, evaluate_plan
from src.services.sync_reconciliation import reconcile_sync_state


class FakeLogger:
    def info(self, *args, **kwargs):
        pass

    def warning(self, *args, **kwargs):
        pass

    def error(self, *args, **kwargs):
        pass

    def debug(self, *args, **kwargs):
        pass


@dataclass
class FakeConfig:
    vod_providers: list[int]
    force_refresh: bool = False
    dry_run: bool = True
    radarr_delete_files_on_removal: bool = True
    radarr_remove_when_vod_available: bool = True
    radarr_delete_files_when_vod_available: bool = False
    watchlist_source: str = "letterboxd"


class FakeLetterboxdService:
    def __init__(self):
        self.watchlist = [
            {"title": "Already Streaming", "year": 2020},
            {"title": "Needs Download", "year": 2021},
        ]

    def fetch_watchlist(self):
        return self.watchlist


class FakeTMDBService:
    def __init__(self):
        self.available_movie = Movie(tmdb_id=101, title="Already Streaming", year=2020)
        self.unavailable_movie = Movie(tmdb_id=202, title="Needs Download", year=2021)
        self.vod_checks = []

    def batch_resolve_movies(self, watchlist):
        assert len(watchlist) == 2
        return [self.available_movie, self.unavailable_movie]

    def check_vod_availability(self, tmdb_id, configured_providers, force_refresh=False):
        self.vod_checks.append(
            {
                "tmdb_id": tmdb_id,
                "providers": configured_providers,
                "force_refresh": force_refresh,
            }
        )
        return (tmdb_id == 101, [])


class FakeRadarrService:
    def __init__(self):
        self.client = object()
        self.synced_movies = None
        self.vod_status = None
        self.dry_run = None
        self.detected_against = None
        self.removal_calls = []
        self.vod_cleanup_callback_results = []
        self.removed_movie = Movie(tmdb_id=303, title="Removed From Letterboxd", year=2019)
        self.downloaded_vod_movie = Movie(tmdb_id=404, title="Downloaded Now Streaming", year=2018)

    def sync_movies(self, movies, vod_status, dry_run=False):
        self.synced_movies = list(movies)
        self.vod_status = dict(vod_status)
        self.dry_run = dry_run
        return {"added": len(self.synced_movies)}

    def detect_removals(self, resolved_movies):
        self.detected_against = list(resolved_movies)
        return [self.removed_movie]

    def sync_removals(self, movies_to_remove, delete_files=False, dry_run=False):
        movies = list(movies_to_remove)
        self.removal_calls.append(
            {
                "tmdb_ids": [movie.tmdb_id for movie in movies],
                "delete_files": delete_files,
                "dry_run": dry_run,
            }
        )
        return {"removed": len(movies)}

    def detect_vod_available_movies(self, vod_check):
        self.vod_cleanup_callback_results.append(vod_check(self.downloaded_vod_movie.tmdb_id))
        return [self.downloaded_vod_movie]


class FakePlexService:
    def __init__(self):
        self.synced_movies = None
        self.vod_status = None
        self.dry_run = None
        self.downloaded_radarr_client = None
        self.downloaded_dry_run = None

    def sync_to_plex(self, letterboxd_movies, vod_status, dry_run=False):
        self.synced_movies = list(letterboxd_movies)
        self.vod_status = dict(vod_status)
        self.dry_run = dry_run
        return {"added": 1, "removed": 0, "skipped": 1}

    def sync_downloaded_radarr_movies(self, radarr_client, dry_run=False):
        self.downloaded_radarr_client = radarr_client
        self.downloaded_dry_run = dry_run
        return {"added": 1, "skipped": 0}


def test_movie_workflow_simulation_exercises_radarr_and_plex_decisions():
    config = FakeConfig(vod_providers=[119, 384])
    letterboxd_service = FakeLetterboxdService()
    tmdb_service = FakeTMDBService()
    radarr_service = FakeRadarrService()
    plex_service = FakePlexService()

    result = run_movie_sync_workflow(
        config=config,
        letterboxd_service=letterboxd_service,
        tmdb_service=tmdb_service,
        radarr_service=radarr_service,
        plex_service=plex_service,
        logger=FakeLogger(),
    )

    assert result["exit_code"] == EXIT_SUCCESS
    assert result["summary"] == {
        "watchlist_source": "letterboxd",
        "watchlist_total": 2,
        "resolved": 2,
        "vod_available": 1,
        "radarr_candidates": 1,
        "radarr_added": 1,
        "radarr_removed": 1,
        "radarr_vod_cleanup": 1,
        "plex_added": 1,
        "plex_removed": 0,
        "plex_skipped": 1,
        "radarr_plex_added": 1,
        "radarr_plex_skipped": 0,
    }
    assert [movie.tmdb_id for movie in radarr_service.synced_movies] == [202]
    assert radarr_service.vod_status == {101: True, 202: False}
    assert radarr_service.dry_run is True
    assert [movie.tmdb_id for movie in radarr_service.detected_against] == [101, 202]
    assert radarr_service.removal_calls == [
        {"tmdb_ids": [303], "delete_files": True, "dry_run": True},
        {"tmdb_ids": [404], "delete_files": False, "dry_run": True},
    ]
    assert radarr_service.vod_cleanup_callback_results == [(False, [])]
    assert [movie.tmdb_id for movie in plex_service.synced_movies] == [101, 202]
    assert plex_service.vod_status == {101: True, 202: False}
    assert plex_service.dry_run is True
    assert plex_service.downloaded_radarr_client is radarr_service.client
    assert plex_service.downloaded_dry_run is True
    assert tmdb_service.vod_checks == [
        {"tmdb_id": 101, "providers": [119, 384], "force_refresh": False},
        {"tmdb_id": 202, "providers": [119, 384], "force_refresh": False},
        {"tmdb_id": 404, "providers": [119, 384], "force_refresh": False},
    ]


def test_movie_workflow_reports_watchlist_source_in_summary():
    config = FakeConfig(vod_providers=[119], watchlist_source="watchlist_app")

    result = run_movie_sync_workflow(
        config=config,
        letterboxd_service=FakeLetterboxdService(),
        tmdb_service=FakeTMDBService(),
        radarr_service=FakeRadarrService(),
        plex_service=FakePlexService(),
        logger=FakeLogger(),
    )

    assert result["summary"]["watchlist_source"] == "watchlist_app"


def test_watched_and_manual_cleanup_plan_is_policy_approved_only_with_file_gate():
    report = reconcile_sync_state(
        backend_snapshot_movies=[],
        backend_watched_movies=[
            {
                "title": "Watched",
                "tmdb_id": 101,
                "source_id": "101",
                "watched_at": "2026-07-11T07:50:00+00:00",
                "lifecycle_version": 2,
                "lifecycle_event_id": "movie-101:watched:2",
            }
        ],
        radarr_movies=[{"title": "Watched", "tmdbId": 101, "hasFile": True}],
        plex_watchlist_movies=[
            {"title": "Watched", "tmdb_id": 101},
            {"title": "Manual", "tmdb_id": 202},
        ],
        radarr_observations=[
            {
                "title": "Manual",
                "tmdb_id": 202,
                "present": False,
                "disappearance_cause": "manual",
            }
        ],
        source_last_successful_sync_at=datetime.now(timezone.utc),
    )

    blockers = evaluate_plan(
        report,
        SyncPolicy(
            allow_mutation=True,
            allow_watched_file_deletion=True,
            max_removal_percent=100.0,
        ),
    )

    assert blockers == []
    assert {
        (decision.area, decision.movie.tmdb_id)
        for decision in report.decisions
        if decision.action == "remove"
    } == {("radarr", 101), ("plex_watchlist", 101), ("plex_watchlist", 202)}

