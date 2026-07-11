from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.services.sync_reconciliation import (
    ReconciliationMovie,
    reconcile_sync_state,
    render_markdown_report,
    write_markdown_report,
)


def movie(title: str, tmdb_id: int, year: int = 2024, **kwargs) -> dict:
    data = {"title": title, "year": year, "tmdb_id": tmdb_id}
    data.update(kwargs)
    return data


def managed(destination: str, tmdb_id: int) -> dict:
    return {"destination": destination, "tmdb_id": tmdb_id}


def decision_titles(report, area: str, action: str) -> list[str]:
    return [
        decision.movie.title
        for decision in report.decisions
        if decision.area == area and decision.action == action
    ]


def test_reconcile_sync_state_reports_radarr_add_keep_and_remove():
    report = reconcile_sync_state(
        backend_radarr_export_movies=[
            movie("Needs Radarr", 101),
            movie("Already In Radarr", 202),
        ],
        radarr_movies=[
            {"title": "Already In Radarr", "year": 2024, "tmdbId": 202, "hasFile": False},
            {"title": "Stale Radarr", "year": 2023, "tmdbId": 303, "hasFile": False},
        ],
        managed_destinations=[managed("radarr", 303)],
    )

    assert decision_titles(report, "radarr", "add") == ["Needs Radarr"]
    assert decision_titles(report, "radarr", "keep") == ["Already In Radarr"]
    assert decision_titles(report, "radarr", "remove") == ["Stale Radarr"]


def test_reconcile_sync_state_reports_plex_add_keep_skip_and_remove():
    report = reconcile_sync_state(
        backend_watchlist_movies=[
            movie("Already In Plex", 101, availability_status="available_on_plex"),
            movie("Streaming Candidate", 202, owned_service_availability=["Netflix"]),
            movie("Unavailable Candidate", 303, availability_status="not_on_plex"),
        ],
        radarr_movies=[
            {"title": "Downloaded Radarr", "year": 2024, "tmdbId": 404, "hasFile": True},
        ],
        plex_library_movies=[
            movie("Library Movie", 505),
        ],
        plex_watchlist_movies=[
            movie("Already In Plex", 101),
            movie("Stale Plex", 606),
        ],
        managed_destinations=[managed("plex_watchlist", 606)],
    )

    assert decision_titles(report, "plex_watchlist", "keep") == ["Already In Plex"]
    assert decision_titles(report, "plex_watchlist", "add") == [
        "Downloaded Radarr",
        "Library Movie",
        "Streaming Candidate",
    ]
    assert decision_titles(report, "plex_watchlist", "skip") == ["Unavailable Candidate"]
    assert decision_titles(report, "plex_watchlist", "remove") == ["Stale Plex"]


def test_reconcile_sync_state_preserves_unmanaged_destination_movies():
    report = reconcile_sync_state(
        backend_snapshot_movies=[],
        radarr_movies=[movie("Manual Radarr", 101, has_file=False)],
        plex_watchlist_movies=[movie("Manual Plex", 202)],
        managed_destinations=[],
    )

    assert decision_titles(report, "radarr", "skip") == ["Manual Radarr"]
    assert decision_titles(report, "plex_watchlist", "skip") == ["Manual Plex"]
    assert any(
        decision.reason == "unmanaged_radarr_movie_preserved"
        for decision in report.decisions
    )
    assert any(
        decision.reason == "unmanaged_plex_watchlist_movie_preserved"
        for decision in report.decisions
    )


def test_reconcile_sync_state_skips_downloaded_managed_radarr_removal():
    report = reconcile_sync_state(
        backend_snapshot_movies=[],
        radarr_movies=[movie("Downloaded", 101, has_file=True)],
        managed_destinations=[managed("radarr", 101)],
    )

    assert decision_titles(report, "radarr", "skip") == ["Downloaded"]
    assert any(
        decision.reason == "downloaded_file_requires_manual_review"
        for decision in report.decisions
    )


def test_reconcile_sync_state_uses_snapshot_eligibility_for_radarr():
    report = reconcile_sync_state(
        backend_snapshot_movies=[
            movie("Eligible", 101, radarr_eligible=True, metadata_status="enriched"),
            movie(
                "Streaming",
                202,
                radarr_eligible=False,
                metadata_status="enriched",
                owned_service_availability=["Netflix"],
            ),
            movie("Unknown", 303, radarr_eligible=False, metadata_status="failed"),
        ],
        radarr_movies=[],
        plex_watchlist_movies=[],
        plex_library_movies=[],
    )

    assert decision_titles(report, "radarr", "add") == ["Eligible"]
    assert "Streaming" not in decision_titles(report, "radarr", "add")
    assert any(
        decision.area == "source_identity"
        and decision.action == "uncertain"
        and decision.movie.title == "Unknown"
        and decision.reason == "backend_metadata_not_enriched"
        for decision in report.decisions
    )


def test_reconcile_sync_state_reports_cache_mismatches_and_previous_errors():
    report = reconcile_sync_state(
        cache_sync_states=[
            {
                "tmdb_id": 101,
                "title": "Cached State",
                "on_radarr": True,
                "on_plex": False,
                "sync_error": "previous failure",
            }
        ],
        radarr_movies=[],
        plex_watchlist_movies=[movie("Cached State", 101)],
    )

    cache_decisions = [
        (decision.action, decision.reason)
        for decision in report.decisions
        if decision.area == "worker_cache"
    ]

    assert ("uncertain", "cache_on_radarr_true_but_live_radarr_missing") in cache_decisions
    assert ("uncertain", "cache_on_plex_false_but_live_plex_present") in cache_decisions
    assert ("error", "cache_sync_error_present") in cache_decisions


def test_reconcile_sync_state_reports_worker_radarr_cache_drift():
    report = reconcile_sync_state(
        backend_radarr_export_movies=[
            movie("Backend Only", 101),
            movie("Shared", 202),
        ],
        cache_radarr_movies=[
            movie("Shared", 202),
            movie("Cache Only", 303),
        ],
    )

    cache_decisions = [
        (decision.movie.title, decision.action, decision.reason)
        for decision in report.decisions
        if decision.area == "worker_cache"
    ]

    assert (
        "Backend Only",
        "uncertain",
        "backend_radarr_export_missing_from_worker_cache_radarr_candidates",
    ) in cache_decisions
    assert (
        "Cache Only",
        "uncertain",
        "worker_cache_radarr_candidate_missing_from_backend_radarr_export",
    ) in cache_decisions


def test_reconcile_sync_state_flags_missing_and_duplicate_ids():
    report = reconcile_sync_state(
        backend_radarr_export_movies=[
            {"title": "Missing Id", "year": 2024},
            movie("Duplicate A", 101),
            movie("Duplicate B", 101),
        ],
    )

    assert any(
        decision.action == "skip"
        and decision.area == "source_identity"
        and decision.movie.title == "Missing Id"
        for decision in report.decisions
    )
    assert any(
        decision.action == "uncertain"
        and decision.area == "source_identity"
        and decision.reason == "duplicate_tmdb_id_in_backend_radarr_export"
        for decision in report.decisions
    )


def test_render_markdown_report_includes_summary_and_decision_sections():
    report = reconcile_sync_state(
        backend_radarr_export_movies=[movie("Needs Radarr", 101)],
        radarr_movies=[],
    )

    content = render_markdown_report(report)

    assert "# Sync reconciliation report" in content
    assert "- backend_radarr_export: 1" in content
    assert "## Radarr - add (1)" in content
    assert "[TMDB 101](https://www.themoviedb.org/movie/101)" in content


def test_write_markdown_report_creates_file(tmp_path: Path):
    report = reconcile_sync_state()

    output_path = write_markdown_report(report, tmp_path / "sync-report.md")

    assert output_path.exists()
    assert output_path.read_text(encoding="utf-8").startswith("# Sync reconciliation report")
