from __future__ import annotations

import sys
from datetime import datetime, timezone
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.clients.sonarr_tv_client import SonarrSeries
from src.models.tv_destination import TvCollectedState, TvOwnership
from src.models.tv_sync import TvAvailability, TvEpisode, TvSeason, TvShow, TvSnapshot
from src.services.tv_sync_planner import build_tv_plan


NOW = datetime(2026, 7, 26, tzinfo=timezone.utc)


def episode(season_number: int, episode_number: int, watched: bool = False) -> TvEpisode:
    return TvEpisode(
        trakt_episode_id=season_number * 100 + episode_number,
        season_number=season_number,
        episode_number=episode_number,
        tvdb_id=season_number * 1000 + episode_number,
        first_aired=NOW,
        last_watched_at=NOW if watched else None,
    )


def season(number: int, availability: str, watched: bool = False) -> TvSeason:
    return TvSeason(
        season_number=number,
        availability=TvAvailability(availability, "PL", NOW),
        episodes=(episode(number, 1, watched),),
    )


def show(
    *,
    seasons: tuple[TvSeason, ...],
    next_episode_season: int | None = None,
) -> TvShow:
    return TvShow(
        trakt_id=10,
        tvdb_id=100,
        title="Example",
        availability=TvAvailability("unknown", "PL", None),
        seasons=seasons,
        next_episode_season=next_episode_season,
    )


def collected(
    show_value: TvShow,
    *,
    sonarr_series: tuple[SonarrSeries, ...] = (),
    errors: tuple[str, ...] = (),
) -> TvCollectedState:
    return TvCollectedState(
        snapshot=TvSnapshot("2", "generation-1", NOW, NOW, "scheduled_full", False, (show_value,)),
        sonarr_series=sonarr_series,
        plex_watchlist=(),
        plex_library_identities=frozenset(),
        ownership=(),
        collection_errors=errors,
    )


def test_unstarted_unavailable_show_selects_season_one_and_sonarr_add() -> None:
    plan = build_tv_plan(collected(show(seasons=(season(1, "confirmed_unavailable"),))))

    assert plan.selected_season_by_tvdb == {100: 1}
    assert [(decision.action, decision.action_id) for decision in plan.decisions_for("sonarr")] == [
        ("sonarr_add", "generation-1:sonarr:100:1:sonarr_add"),
        ("sonarr_monitor_season", "generation-1:sonarr:100:1:sonarr_monitor_season"),
    ]


def test_completed_first_season_selects_trakt_next_episode_season_only() -> None:
    plan = build_tv_plan(
        collected(
            show(
                seasons=(season(1, "available", watched=True), season(2, "confirmed_unavailable")),
                next_episode_season=2,
            )
        )
    )

    assert plan.selected_season_by_tvdb == {100: 2}


def test_existing_sonarr_series_is_adoption_candidate_not_auto_owned() -> None:
    existing = SonarrSeries(1, 100, "Example", False, {1: False}, {"id": 1, "tvdbId": 100})
    plan = build_tv_plan(collected(show(seasons=(season(1, "confirmed_unavailable"),)), sonarr_series=(existing,)))

    assert [decision.action for decision in plan.decisions_for("sonarr")] == [
        "sonarr_adoption_candidate"
    ]


def test_provider_unknown_blocks_sonarr_and_collection_error_blocks_applyable_plan() -> None:
    plan = build_tv_plan(
        collected(
            show(seasons=(season(1, "unknown"),)),
            errors=("plex_library: unavailable",),
        )
    )

    assert plan.applyable is False
    assert [(decision.action, decision.reason) for decision in plan.decisions_for("sonarr")] == [
        ("uncertain", "sonarr_provider_availability_unknown")
    ]
