from __future__ import annotations

import sys
from pathlib import Path

import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from cleanup_removed_movies import (
    CleanupSafetyError,
    CleanupReport,
    CleanupReportMovie,
    detect_removed_movie_states,
    destructive_cleanup_confirmed,
    resolve_current_letterboxd_ids,
)
from src.models.movie import Movie


class FakeTMDBService:
    def __init__(self):
        self.batch_calls = []

    def batch_resolve_movies(self, movies):
        self.batch_calls.append(list(movies))
        return [
            Movie(tmdb_id=101, title="Still Listed", year=2020),
            None,
        ]


def test_resolve_current_letterboxd_ids_resolves_raw_watchlist_entries():
    tmdb_service = FakeTMDBService()
    watchlist = [
        {"title": "Still Listed", "year": 2020, "letterboxd_id": "still-listed"},
        {"title": "Unresolved", "year": 2021, "letterboxd_id": "unresolved"},
        {"title": "Already Has Id", "year": 2022, "tmdb_id": 202},
    ]

    current_ids = resolve_current_letterboxd_ids(watchlist, tmdb_service)

    assert current_ids == {101, 202}
    assert tmdb_service.batch_calls == [
        [
            {"title": "Still Listed", "year": 2020, "letterboxd_id": "still-listed"},
            {"title": "Unresolved", "year": 2021, "letterboxd_id": "unresolved"},
        ]
    ]


def test_resolve_current_letterboxd_ids_aborts_when_non_empty_watchlist_has_no_ids():
    class UnresolvedTMDBService:
        def batch_resolve_movies(self, movies):
            return [None for _ in movies]

    with pytest.raises(CleanupSafetyError):
        resolve_current_letterboxd_ids(
            [{"title": "Could Not Resolve", "year": 2020}],
            UnresolvedTMDBService(),
        )


def test_detect_removed_movie_states_compares_tmdb_ids_not_raw_letterboxd_dicts():
    previous = [
        {"tmdb_id": 101, "on_letterboxd": 1, "on_plex": 0, "on_radarr": 1},
        {"tmdb_id": 303, "on_letterboxd": 1, "on_plex": 1, "on_radarr": 0},
    ]

    removed = detect_removed_movie_states(previous, {101})

    assert removed == [{"tmdb_id": 303, "on_letterboxd": 1, "on_plex": 1, "on_radarr": 0}]


def test_cleanup_report_groups_movies_by_planned_action(tmp_path: Path):
    report = CleanupReport(dry_run=True)
    report.add(
        "delete_from_library",
        CleanupReportMovie(tmdb_id=101, title="Library Movie", year=2020),
    )
    report.add(
        "remove_from_watchlist",
        CleanupReportMovie(tmdb_id=101, title="Library Movie", year=2020),
    )
    report.add(
        "remove_from_radarr",
        CleanupReportMovie(tmdb_id=202, title="Radarr Movie", year=2021),
    )

    output_path = report.write(tmp_path / "cleanup-report.md")

    content = output_path.read_text(encoding="utf-8")
    assert "# VOD Filter cleanup dry-run report" in content
    assert "## Delete from Plex library (1)" in content
    assert (
        "- Library Movie (2020) "
        "[TMDB 101](https://www.themoviedb.org/movie/101)"
    ) in content
    assert "## Remove from Plex watchlist (1)" in content
    assert "## Remove from Radarr (1)" in content
    assert (
        "- Radarr Movie (2021) "
        "[TMDB 202](https://www.themoviedb.org/movie/202)"
    ) in content


def test_destructive_cleanup_requires_explicit_confirmation(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.delenv("CONFIRM_DESTRUCTIVE_CLEANUP", raising=False)

    assert destructive_cleanup_confirmed([]) is False
    assert destructive_cleanup_confirmed(["--confirm-destructive-cleanup"]) is True

    monkeypatch.setenv("CONFIRM_DESTRUCTIVE_CLEANUP", "true")

    assert destructive_cleanup_confirmed([]) is True

