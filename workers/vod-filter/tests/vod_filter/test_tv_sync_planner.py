from __future__ import annotations

import sys
from datetime import datetime, timezone
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.clients.plex_tv_client import PlexTvShow, VerifiedTvIdentity
from src.clients.sonarr_tv_client import SonarrSeries
from src.models.tv_destination import TvCollectedState, TvOwnership
from src.models.tv_sync import TvAvailability, TvEpisode, TvSeason, TvShow, TvSnapshot
from src.services.tv_sync_planner import build_tv_plan


NOW = datetime(2026, 7, 26, tzinfo=timezone.utc)


def episode(
    season_number: int,
    episode_number: int,
    watched: bool = False,
    *,
    first_aired: datetime | None = NOW,
    tvdb_id: int | None = None,
) -> TvEpisode:
    return TvEpisode(
        trakt_episode_id=season_number * 100 + episode_number,
        season_number=season_number,
        episode_number=episode_number,
        tvdb_id=tvdb_id if tvdb_id is not None else season_number * 1000 + episode_number,
        first_aired=first_aired,
        last_watched_at=NOW if watched else None,
    )


def season(
    number: int,
    availability: str,
    watched: bool = False,
    *,
    episodes: tuple[TvEpisode, ...] | None = None,
) -> TvSeason:
    return TvSeason(
        season_number=number,
        availability=TvAvailability(availability, "PL", NOW),
        episodes=episodes if episodes is not None else (episode(number, 1, watched),),
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
    sonarr_episode_ids_by_tvdb: tuple[tuple[int, int, int], ...] = (),
    plex_watchlist: tuple[PlexTvShow, ...] = (),
    ownership: tuple[TvOwnership, ...] = (),
    errors: tuple[str, ...] = (),
) -> TvCollectedState:
    return TvCollectedState(
        snapshot=TvSnapshot("2", "generation-1", NOW, NOW, "scheduled_full", False, (show_value,)),
        sonarr_series=sonarr_series,
        sonarr_episode_ids_by_tvdb=sonarr_episode_ids_by_tvdb,
        plex_watchlist=plex_watchlist,
        plex_library_identities=frozenset(),
        ownership=ownership,
        collection_errors=errors,
    )


def test_unstarted_unavailable_show_selects_season_one_and_sonarr_add() -> None:
    plan = build_tv_plan(collected(show(seasons=(season(1, "confirmed_unavailable"),))))

    assert plan.selected_season_by_tvdb == {100: 1}
    assert [(decision.action, decision.action_id) for decision in plan.decisions_for("sonarr")] == [
        ("skip", "generation-1:sonarr:100:1:skip"),
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


def test_existing_real_plex_watchlist_show_keeps_desired_row_without_duplicate_add() -> None:
    existing = PlexTvShow("plex-show-100", VerifiedTvIdentity(100, None, None))

    plan = build_tv_plan(
        collected(
            show(seasons=(season(1, "available"),)),
            plex_watchlist=(existing,),
        )
    )

    assert [decision.action for decision in plan.decisions_for("plex_watchlist")] == ["keep"]


def test_existing_real_plex_watchlist_show_is_removed_when_no_longer_desired() -> None:
    existing = PlexTvShow("plex-show-100", VerifiedTvIdentity(100, None, None))

    plan = build_tv_plan(
        collected(
            show(seasons=(season(1, "confirmed_unavailable"),)),
            plex_watchlist=(existing,),
        )
    )

    assert [decision.action for decision in plan.decisions_for("plex_watchlist")] == ["plex_remove"]


def test_selected_unavailable_season_skips_search_without_existing_sonarr_episode_ids() -> None:
    selected = season(
        1,
        "confirmed_unavailable",
        episodes=(
            episode(1, 1, watched=True, tvdb_id=1001),
            episode(1, 2, tvdb_id=1002),
            episode(1, 3, first_aired=NOW.replace(year=2027), tvdb_id=1003),
        ),
    )

    plan = build_tv_plan(collected(show(seasons=(selected,))))

    assert [(decision.action, decision.reason) for decision in plan.decisions_for("sonarr")] == [
        ("skip", "sonarr_episode_ids_unavailable"),
        ("sonarr_add", "selected_season_confirmed_unavailable"),
        ("sonarr_monitor_season", "selected_season_confirmed_unavailable"),
    ]


def test_completed_show_fallback_does_not_select_a_future_only_numbered_season() -> None:
    plan = build_tv_plan(
        collected(
            show(
                seasons=(
                    season(1, "available", watched=True),
                    season(
                        2,
                        "confirmed_unavailable",
                        episodes=(episode(2, 1, first_aired=NOW.replace(year=2027)),),
                    ),
                    season(3, "confirmed_unavailable"),
                )
            )
        )
    )

    assert plan.selected_season_by_tvdb == {}


def test_owned_unmonitored_sonarr_series_is_monitored_when_no_season_is_eligible() -> None:
    existing = SonarrSeries(1, 100, "Example", False, {1: True, 2: False}, {"id": 1, "tvdbId": 100})
    plan = build_tv_plan(
        collected(
            show(
                seasons=(
                    season(1, "available", watched=True),
                    season(
                        2,
                        "confirmed_unavailable",
                        episodes=(episode(2, 1, first_aired=NOW.replace(year=2027)),),
                    ),
                )
            ),
            sonarr_series=(existing,),
            ownership=(TvOwnership("sonarr", 100, "worker"),),
        )
    )

    assert [decision.action for decision in plan.decisions_for("sonarr")] == [
        "sonarr_monitor_series",
        "uncertain",
    ]


def test_selected_unavailable_season_searches_sonarr_internal_ids_not_tvdb_ids() -> None:
    existing = SonarrSeries(1, 100, "Example", True, {1: True}, {"id": 1, "tvdbId": 100})
    selected = season(
        1,
        "confirmed_unavailable",
        episodes=(episode(1, 1, tvdb_id=1001), episode(1, 2, tvdb_id=1002)),
    )
    plan = build_tv_plan(
        collected(
            show(seasons=(selected,)),
            sonarr_series=(existing,),
            sonarr_episode_ids_by_tvdb=((100, 1001, 9001), (100, 1002, 9002)),
            ownership=(TvOwnership("sonarr", 100, "worker"),),
        )
    )

    search = next(
        decision for decision in plan.decisions_for("sonarr") if decision.action == "sonarr_search_episodes"
    )
    assert search.episode_numbers == (9001, 9002)


def test_selected_unavailable_season_skips_search_when_sonarr_episode_ids_are_unavailable() -> None:
    existing = SonarrSeries(1, 100, "Example", True, {1: True}, {"id": 1, "tvdbId": 100})
    plan = build_tv_plan(
        collected(
            show(seasons=(season(1, "confirmed_unavailable"),)),
            sonarr_series=(existing,),
            ownership=(TvOwnership("sonarr", 100, "worker"),),
        )
    )

    decisions = plan.decisions_for("sonarr")
    assert "sonarr_search_episodes" not in [decision.action for decision in decisions]
    assert [(decision.action, decision.reason) for decision in decisions] == [
        ("skip", "sonarr_episode_ids_unavailable"),
    ]
