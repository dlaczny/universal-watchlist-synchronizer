from __future__ import annotations

import sqlite3
import sys
from pathlib import Path

import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.services.cache_service import CacheService


def radarr_movie(tmdb_id: int, title: str = "Movie", year: int = 2024) -> dict:
    return {"tmdbId": tmdb_id, "title": title, "year": year}


def by_tmdb(rows: list[dict]) -> dict[int, dict]:
    return {row["tmdb_id"]: row for row in rows}


def test_first_radarr_observation_is_baseline_only_and_persists(tmp_path: Path):
    database_path = tmp_path / "vod-filter.db"
    cache = CacheService(database_path=str(database_path))

    observed = cache.observe_radarr_movies(
        [radarr_movie(101, "Existing")],
        active_tmdb_ids=set(),
        watched_events_by_tmdb={},
    )

    assert by_tmdb(observed)[101]["present"] == 1
    assert by_tmdb(observed)[101]["disappearance_cause"] is None
    restarted = CacheService(database_path=str(database_path))
    assert by_tmdb(restarted.get_radarr_observations())[101]["present"] == 1


def test_later_unclassified_radarr_disappearance_is_manual(tmp_path: Path):
    cache = CacheService(database_path=str(tmp_path / "vod-filter.db"))
    cache.observe_radarr_movies([radarr_movie(101)], set(), {})

    observed = cache.observe_radarr_movies([], set(), {})

    assert by_tmdb(observed)[101]["present"] == 0
    assert by_tmdb(observed)[101]["disappearance_cause"] == "manual"
    assert by_tmdb(observed)[101]["source_event_id"] is None


@pytest.mark.parametrize(
    ("active_tmdb_ids", "watched_events", "expected_cause", "expected_event"),
    [
        ({101}, {}, "active_source", None),
        (set(), {101: "movie-101:watched:2"}, "watched", "movie-101:watched:2"),
    ],
)
def test_radarr_disappearance_is_classified_from_published_source_state(
    tmp_path: Path,
    active_tmdb_ids,
    watched_events,
    expected_cause,
    expected_event,
):
    cache = CacheService(database_path=str(tmp_path / "vod-filter.db"))
    cache.observe_radarr_movies([radarr_movie(101)], {101}, {})

    observed = cache.observe_radarr_movies([], active_tmdb_ids, watched_events)

    row = by_tmdb(observed)[101]
    assert row["present"] == 0
    assert row["disappearance_cause"] == expected_cause
    assert row["source_event_id"] == expected_event


def test_worker_marked_removal_records_exact_watched_authorization(tmp_path: Path):
    cache = CacheService(database_path=str(tmp_path / "vod-filter.db"))
    cache.observe_radarr_movies([radarr_movie(101)], {101}, {})

    cache.mark_radarr_removed_by_worker(101, "movie-101:watched:2")

    row = by_tmdb(cache.get_radarr_observations())[101]
    assert row["present"] == 0
    assert row["disappearance_cause"] == "watched"
    assert row["source_event_id"] == "movie-101:watched:2"


def test_radarr_reappearance_clears_prior_disappearance(tmp_path: Path):
    cache = CacheService(database_path=str(tmp_path / "vod-filter.db"))
    cache.observe_radarr_movies([radarr_movie(101)], set(), {})
    cache.observe_radarr_movies([], set(), {})

    observed = cache.observe_radarr_movies([radarr_movie(101)], set(), {})

    row = by_tmdb(observed)[101]
    assert row["present"] == 1
    assert row["disappearance_cause"] is None
    assert row["source_event_id"] is None


def test_cleanup_attempt_is_persisted_without_credentials(tmp_path: Path):
    database_path = tmp_path / "vod-filter.db"
    cache = CacheService(database_path=str(database_path))

    cache.record_cleanup_attempt(
        authorization="letterboxd_watched",
        authorization_event_id="movie-101:watched:2",
        destination="radarr",
        tmdb_id=101,
        delete_files=True,
        status="completed",
        error=None,
    )

    with sqlite3.connect(database_path) as connection:
        connection.row_factory = sqlite3.Row
        row = connection.execute("SELECT * FROM movie_cleanup_history").fetchone()

    stored = dict(row)
    assert stored.pop("attempted_at")
    assert stored == {
        "id": 1,
        "authorization": "letterboxd_watched",
        "authorization_event_id": "movie-101:watched:2",
        "destination": "radarr",
        "tmdb_id": 101,
        "delete_files": 1,
        "status": "completed",
        "error": None,
    }
