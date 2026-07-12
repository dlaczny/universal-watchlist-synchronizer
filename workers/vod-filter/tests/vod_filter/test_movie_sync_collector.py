from __future__ import annotations

import sys
from datetime import datetime, timezone
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.services.movie_sync_collector import MovieSyncCollector


class FakeBackend:
    def __init__(self, error: Exception | None = None):
        self.error = error

    def fetch_movie_sync_snapshot(self, sync_first: bool):
        if self.error:
            raise self.error
        return {
            "source_snapshot_id": "letterboxd-42",
            "generated_at": datetime(2026, 7, 11, 8, 0, tzinfo=timezone.utc),
            "last_successful_movie_sync_at": datetime(
                2026, 7, 11, 7, 55, tzinfo=timezone.utc
            ),
            "movies": [{"tmdb_id": 101, "title": "Desired", "radarr_eligible": True}],
            "watched_movies": [
                {
                    "tmdb_id": 202,
                    "title": "Watched",
                    "lifecycle_event_id": "movie-202:watched:2",
                }
            ],
        }


class FakeRadarr:
    def __init__(self, error: Exception | None = None):
        self.error = error

    def get_all_movies(self):
        if self.error:
            raise self.error
        return [{"tmdbId": 101, "title": "Desired", "hasFile": False}]

    def get_exclusions(self):
        if self.error:
            raise self.error
        return [{"id": 55, "tmdbId": 202, "movieTitle": "Excluded"}]


class FakePlex:
    def __init__(self, watchlist_error: Exception | None = None):
        self.watchlist_error = watchlist_error

    def get_watchlist(self):
        if self.watchlist_error:
            raise self.watchlist_error
        return []

    def get_library_movies(self, library_name: str):
        assert library_name == "Movies"
        return [{"tmdb_id": 202, "title": "Library"}]


class FakeCache:
    def __init__(self):
        self.observation_calls = []

    def get_managed_destinations(self):
        return [{"destination": "radarr", "tmdb_id": 101, "last_action": "add"}]

    def observe_radarr_movies(self, movies, active_tmdb_ids, watched_events_by_tmdb):
        self.observation_calls.append(
            (list(movies), set(active_tmdb_ids), dict(watched_events_by_tmdb))
        )
        return [
            {
                "tmdb_id": 101,
                "title": "Desired",
                "present": 1,
                "disappearance_cause": None,
            }
        ]


def test_collector_returns_complete_immutable_boundary_state():
    cache = FakeCache()
    collector = MovieSyncCollector(
        backend_client=FakeBackend(),
        radarr_client=FakeRadarr(),
        plex_client=FakePlex(),
        cache_service=cache,
        library_name="Movies",
    )

    state = collector.collect(sync_first=True)

    assert state.source_snapshot_id == "letterboxd-42"
    assert state.backend_movies[0]["tmdb_id"] == 101
    assert state.backend_watched_movies[0]["lifecycle_event_id"] == "movie-202:watched:2"
    assert state.radarr_movies[0]["tmdbId"] == 101
    assert state.radarr_exclusions[0]["tmdbId"] == 202
    assert state.plex_library_movies[0]["tmdb_id"] == 202
    assert state.managed_destinations[0]["destination"] == "radarr"
    assert state.radarr_observations[0]["present"] == 1
    assert cache.observation_calls == [
        (
            [{"tmdbId": 101, "title": "Desired", "hasFile": False}],
            {101},
            {202: "movie-202:watched:2"},
        )
    ]
    assert state.collection_errors == ()


def test_collector_keeps_collecting_and_reports_each_boundary_failure():
    cache = FakeCache()
    collector = MovieSyncCollector(
        backend_client=FakeBackend(RuntimeError("backend down")),
        radarr_client=FakeRadarr(RuntimeError("radarr down")),
        plex_client=FakePlex(RuntimeError("plex down")),
        cache_service=cache,
        library_name="Movies",
    )

    state = collector.collect(sync_first=True)

    assert state.backend_movies == ()
    assert state.radarr_movies == ()
    assert state.radarr_exclusions == ()
    assert state.plex_watchlist_movies == ()
    assert state.plex_library_movies[0]["tmdb_id"] == 202
    assert state.radarr_observations == ()
    assert cache.observation_calls == []
    assert state.collection_errors == (
        "backend_snapshot: backend down",
        "radarr: radarr down",
        "radarr_exclusions: radarr down",
        "plex_watchlist: plex down",
    )


def test_collector_does_not_advance_observations_after_failed_radarr_read():
    cache = FakeCache()
    collector = MovieSyncCollector(
        backend_client=FakeBackend(),
        radarr_client=FakeRadarr(RuntimeError("radarr down")),
        plex_client=FakePlex(),
        cache_service=cache,
        library_name="Movies",
    )

    state = collector.collect(sync_first=True)

    assert state.radarr_movies == ()
    assert state.radarr_observations == ()
    assert cache.observation_calls == []
