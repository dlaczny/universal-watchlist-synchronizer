from __future__ import annotations

import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.services.movie_sync_policy import SyncPolicy, evaluate_plan
from src.services.sync_reconciliation import reconcile_sync_state


NOW = datetime(2026, 7, 11, 8, 0, tzinfo=timezone.utc)


def movie(title: str, tmdb_id: int, **kwargs) -> dict:
    data = {"title": title, "year": 2024, "tmdb_id": tmdb_id}
    data.update(kwargs)
    return data


def policy(**kwargs) -> SyncPolicy:
    defaults = {
        "max_source_age_minutes": 120,
        "max_removal_count": 10,
        "max_removal_percent": 25.0,
        "allow_mutation": True,
    }
    defaults.update(kwargs)
    return SyncPolicy(**defaults)


def fresh_report(**kwargs):
    return reconcile_sync_state(
        backend_snapshot_movies=kwargs.pop("backend_snapshot_movies", [
            movie("Desired", 1, radarr_eligible=True, metadata_status="enriched")
        ]),
        radarr_movies=kwargs.pop("radarr_movies", []),
        plex_watchlist_movies=kwargs.pop("plex_watchlist_movies", []),
        plex_library_movies=kwargs.pop("plex_library_movies", []),
        source_snapshot_at=NOW,
        source_last_successful_sync_at=NOW - timedelta(minutes=5),
        **kwargs,
    )


def test_evaluate_plan_allows_fresh_unambiguous_plan():
    report = fresh_report()

    assert evaluate_plan(report, policy(), now=NOW) == []


def test_evaluate_plan_blocks_mutation_when_disabled():
    report = fresh_report()

    assert evaluate_plan(report, policy(allow_mutation=False), now=NOW) == [
        "mutation_disabled"
    ]


def test_evaluate_plan_blocks_stale_or_missing_source_freshness():
    stale = reconcile_sync_state(
        backend_snapshot_movies=[movie("Desired", 1, radarr_eligible=True)],
        source_snapshot_at=NOW,
        source_last_successful_sync_at=NOW - timedelta(minutes=121),
    )
    missing = reconcile_sync_state(
        backend_snapshot_movies=[movie("Desired", 1, radarr_eligible=True)],
        source_snapshot_at=NOW,
        source_last_successful_sync_at=None,
    )

    assert "source_snapshot_stale" in evaluate_plan(stale, policy(), now=NOW)
    assert "source_freshness_unknown" in evaluate_plan(missing, policy(), now=NOW)


def test_evaluate_plan_blocks_collection_and_identity_errors():
    report = fresh_report(
        backend_snapshot_movies=[{"title": "Missing ID"}],
        collection_errors=["plex_watchlist_unavailable"],
    )

    blockers = evaluate_plan(report, policy(), now=NOW)

    assert "collection_errors" in blockers
    assert "invalid_source_identity" in blockers


def test_evaluate_plan_blocks_unexpected_empty_source_with_managed_movies():
    report = fresh_report(
        backend_snapshot_movies=[],
        managed_destinations=[{"destination": "radarr", "tmdb_id": 101}],
    )

    assert "unexpected_empty_source" in evaluate_plan(report, policy(), now=NOW)


def test_evaluate_plan_blocks_removal_count_and_percentage_thresholds():
    report = fresh_report(
        backend_snapshot_movies=[],
        radarr_movies=[
            movie("One", 1, has_file=False),
            movie("Two", 2, has_file=False),
        ],
        managed_destinations=[
            {"destination": "radarr", "tmdb_id": 1},
            {"destination": "radarr", "tmdb_id": 2},
        ],
    )

    blockers = evaluate_plan(
        report,
        policy(max_removal_count=1, max_removal_percent=50.0),
        now=NOW,
    )

    assert "removal_count_exceeded" in blockers
    assert "removal_percentage_exceeded" in blockers
