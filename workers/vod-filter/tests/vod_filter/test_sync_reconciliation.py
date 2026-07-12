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


def find_decision(report, area: str, action: str, tmdb_id: int):
    return next(
        decision
        for decision in report.decisions
        if decision.area == area
        and decision.action == action
        and decision.movie.tmdb_id == tmdb_id
    )


def watched_movie(title: str, tmdb_id: int | None, event_id: str) -> dict:
    return {
        "title": title,
        "year": 2024,
        "tmdb_id": tmdb_id,
        "source_id": str(tmdb_id or "missing"),
        "watched_at": "2026-07-11T07:50:00+00:00",
        "lifecycle_version": 2,
        "lifecycle_event_id": event_id,
    }


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


def test_reconcile_sync_state_skips_radarr_title_year_path_collision():
    report = reconcile_sync_state(
        backend_radarr_export_movies=[movie("Resurrection", 878608, year=2025)],
        radarr_movies=[movie("Resurrection", 1279580, year=2025, has_file=False)],
    )

    assert decision_titles(report, "radarr", "add") == []
    assert any(
        decision.area == "radarr"
        and decision.action == "skip"
        and decision.movie.tmdb_id == 878608
        and decision.reason == "radarr_title_year_collision_requires_manual_review"
        for decision in report.decisions
    )


def test_reconcile_sync_state_explains_radarr_exclusion_override():
    report = reconcile_sync_state(
        backend_radarr_export_movies=[movie("Desired Again", 101)],
        radarr_movies=[],
        radarr_exclusions=[{"id": 55, "tmdbId": 101, "movieTitle": "Desired Again"}],
    )

    decision = next(
        item
        for item in report.decisions
        if item.area == "radarr" and item.movie.tmdb_id == 101
    )
    assert decision.action == "add"
    assert decision.reason == "desired_radarr_movie_missing_override_exclusion"
    assert report.source_counts["radarr_exclusions"] == 1


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
        decision.reason == "radarr_movie_without_source_authorization_preserved"
        for decision in report.decisions
    )
    assert any(
        decision.reason == "unmanaged_plex_watchlist_movie_preserved"
        for decision in report.decisions
    )


def test_complete_snapshot_preserves_managed_radarr_movie_without_source_authorization():
    report = reconcile_sync_state(
        backend_snapshot_movies=[],
        backend_watched_movies=[],
        radarr_movies=[movie("Never In Letterboxd", 101, has_file=False)],
        managed_destinations=[managed("radarr", 101)],
    )

    assert decision_titles(report, "radarr", "remove") == []
    decision = find_decision(report, "radarr", "skip", 101)
    assert decision.reason == "radarr_movie_without_source_authorization_preserved"


def test_reconcile_sync_state_skips_downloaded_managed_radarr_removal():
    report = reconcile_sync_state(
        backend_snapshot_movies=[
            movie(
                "Downloaded",
                101,
                radarr_eligible=False,
                metadata_status="enriched",
            )
        ],
        radarr_movies=[movie("Downloaded", 101, has_file=True)],
        managed_destinations=[managed("radarr", 101)],
    )

    assert decision_titles(report, "radarr", "skip") == ["Downloaded"]
    assert any(
        decision.reason == "downloaded_file_requires_manual_review"
        for decision in report.decisions
    )


def test_watched_movie_authorizes_exact_radarr_and_plex_removal_despite_protections():
    report = reconcile_sync_state(
        backend_snapshot_movies=[],
        backend_watched_movies=[
            watched_movie("Watched", 101, "movie-101:watched:2")
        ],
        radarr_movies=[movie("Watched", 101, has_file=True)],
        plex_watchlist_movies=[movie("Watched", 101)],
        plex_library_movies=[movie("Watched", 101)],
        managed_destinations=[],
        source_snapshot_id="letterboxd-42",
    )

    radarr = find_decision(report, "radarr", "remove", 101)
    assert radarr.reason == "watched_letterboxd_movie_remove_from_radarr"
    assert radarr.delete_files is True
    assert radarr.authorization == "letterboxd_watched"
    assert radarr.authorization_event_id == "movie-101:watched:2"
    assert radarr.managed is False

    plex = find_decision(report, "plex_watchlist", "remove", 101)
    assert plex.reason == "watched_letterboxd_movie_remove_from_plex_watchlist"
    assert plex.delete_files is False
    assert plex.authorization == "letterboxd_watched"
    assert plex.authorization_event_id == "movie-101:watched:2"
    assert report.source_snapshot_id == "letterboxd-42"
    assert report.source_counts["watched_authorizations"] == 1


def test_watched_movie_with_absent_targets_emits_converged_skips():
    report = reconcile_sync_state(
        backend_snapshot_movies=[],
        backend_watched_movies=[
            watched_movie("Already Gone", 101, "movie-101:watched:2")
        ],
        radarr_movies=[],
        plex_watchlist_movies=[],
    )

    radarr = find_decision(report, "radarr", "skip", 101)
    plex = find_decision(report, "plex_watchlist", "skip", 101)
    assert radarr.reason == "watched_letterboxd_movie_absent_from_radarr"
    assert plex.reason == "watched_letterboxd_movie_absent_from_plex_watchlist"
    assert radarr.authorization_event_id == "movie-101:watched:2"
    assert plex.authorization_event_id == "movie-101:watched:2"


def test_watched_movie_without_tmdb_identity_cannot_authorize_mutation():
    report = reconcile_sync_state(
        backend_snapshot_movies=[],
        backend_watched_movies=[
            watched_movie("Missing Identity", None, "movie-missing:watched:2")
        ],
        radarr_movies=[],
        plex_watchlist_movies=[],
    )

    assert any(
        decision.area == "source_identity"
        and decision.action == "skip"
        and decision.reason == "watched_movie_missing_tmdb_identity"
        and decision.movie.title == "Missing Identity"
        for decision in report.decisions
    )
    assert not any(decision.authorization for decision in report.decisions)


def test_active_and_watched_tmdb_conflict_blocks_destination_decisions():
    report = reconcile_sync_state(
        backend_snapshot_movies=[
            movie("Active", 101, radarr_eligible=True, metadata_status="enriched")
        ],
        backend_watched_movies=[
            watched_movie("Watched Alias", 101, "movie-alias:watched:2")
        ],
        radarr_movies=[movie("Live", 101, has_file=True)],
        plex_watchlist_movies=[movie("Live", 101)],
    )

    assert any(
        decision.area == "source_identity"
        and decision.action == "uncertain"
        and decision.reason == "active_watched_tmdb_identity_conflict"
        for decision in report.decisions
    )
    assert not any(
        decision.movie.tmdb_id == 101 and decision.area in {"radarr", "plex_watchlist"}
        for decision in report.decisions
    )


def test_reactivated_movie_uses_active_rules_and_cancels_stale_watched_observation():
    report = reconcile_sync_state(
        backend_snapshot_movies=[
            movie("Reactivated", 101, radarr_eligible=True, metadata_status="enriched")
        ],
        backend_watched_movies=[],
        radarr_movies=[movie("Reactivated", 101, has_file=True)],
        plex_watchlist_movies=[movie("Reactivated", 101)],
        radarr_observations=[
            movie(
                "Reactivated",
                101,
                present=False,
                disappearance_cause="watched",
                source_event_id="movie-101:watched:1",
            )
        ],
    )

    assert find_decision(report, "radarr", "keep", 101).delete_files is False
    assert not any(
        decision.movie.tmdb_id == 101 and decision.action == "remove"
        for decision in report.decisions
    )


def test_manual_radarr_disappearance_removes_only_exact_plex_watchlist_identity():
    report = reconcile_sync_state(
        backend_snapshot_movies=[],
        backend_watched_movies=[],
        radarr_movies=[],
        plex_watchlist_movies=[movie("Manually Removed", 303)],
        plex_library_movies=[movie("Manually Removed", 303)],
        radarr_observations=[
            movie(
                "Manually Removed",
                303,
                present=False,
                disappearance_cause="manual",
            )
        ],
        managed_destinations=[],
    )

    plex = find_decision(report, "plex_watchlist", "remove", 303)
    assert plex.reason == "manually_removed_radarr_movie_remove_from_plex_watchlist"
    assert plex.authorization == "manual_radarr_removal"
    assert plex.authorization_event_id is None
    assert not any(
        decision.area == "radarr" and decision.movie.tmdb_id == 303
        for decision in report.decisions
    )
    assert report.source_counts["manual_radarr_disappearances"] == 1


def test_manual_radarr_disappearance_does_not_suppress_active_letterboxd_movie():
    report = reconcile_sync_state(
        backend_snapshot_movies=[
            movie("Still Active", 303, radarr_eligible=False, metadata_status="enriched")
        ],
        backend_watched_movies=[],
        radarr_movies=[],
        plex_watchlist_movies=[movie("Still Active", 303)],
        plex_library_movies=[movie("Still Active", 303)],
        radarr_observations=[
            movie(
                "Still Active",
                303,
                present=False,
                disappearance_cause="manual",
            )
        ],
    )

    plex = find_decision(report, "plex_watchlist", "keep", 303)
    assert plex.authorization is None
    assert plex.reason != "manually_removed_radarr_movie_remove_from_plex_watchlist"


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
