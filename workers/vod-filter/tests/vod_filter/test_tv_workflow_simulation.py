"""Stateful boundary simulation for the reversible TV workflow."""

from __future__ import annotations

from datetime import datetime, timezone

from src.clients.plex_tv_client import PlexTvShow, VerifiedTvIdentity
from src.clients.sonarr_tv_client import SonarrSeries
from src.models.tv_destination import TvCollectedState, TvOwnership
from src.models.tv_sync import TvAvailability, TvEpisode, TvSeason, TvShow, TvSnapshot
from src.services.tv_destination_executor import TvDestinationExecutor
from src.services.tv_sync_planner import build_tv_plan
from src.services.tv_sync_policy import TvSyncPolicy, evaluate_tv_plan


NOW = datetime(2026, 7, 26, 12, tzinfo=timezone.utc)
TVDB_ID = 7101


class StateStore:
    def __init__(self) -> None:
        self.actions: list[tuple[str, str]] = []
        self.ownership: list[TvOwnership] = []

    def record_action(self, action_id: str, action: str, status: str) -> None:
        self.actions.append((action, status))

    def record_ownership(self, destination: str, tvdb_id: int, origin: str) -> None:
        self.ownership.append(TvOwnership(destination, tvdb_id, origin))


class Sonarr:
    def __init__(self, series: SonarrSeries | None = None) -> None:
        self.series = series
        self.calls: list[str] = []

    def lookup_by_tvdb(self, tvdb_id: int):
        assert tvdb_id == TVDB_ID
        self.calls.append("lookup")
        return object()

    def add_series(self, lookup, root_folder: str, quality_profile_id: int):
        assert root_folder == "/tv"
        assert quality_profile_id == 1
        self.calls.append("add")
        self.series = SonarrSeries(17, TVDB_ID, "Example", True, {1: True}, {"id": 17, "tvdbId": TVDB_ID})
        return self.series

    def get_series_by_tvdb(self, tvdb_id: int):
        assert tvdb_id == TVDB_ID
        return self.series

    def set_series_monitored(self, series, monitored: bool):
        self.calls.append("monitor_series")
        return series

    def set_season_monitored(self, series, season_number: int):
        self.calls.append(f"monitor_season:{season_number}")
        return series

    def search_episode_ids(self, series_id: int, episode_ids: list[int]) -> None:
        self.calls.append(f"search:{series_id}:{episode_ids}")


class Plex:
    def __init__(self) -> None:
        self.watchlist: set[int] = set()
        self.calls: list[str] = []

    def add_watchlist_show(self, identity: VerifiedTvIdentity, title: str) -> bool:
        assert title == "Example"
        self.watchlist.add(identity.tvdb_id)
        self.calls.append("add")
        return True

    def remove_watchlist_show(self, tvdb_id: int, tmdb_id: int | None, imdb_id: str | None) -> bool:
        self.watchlist.discard(tvdb_id)
        self.calls.append("remove")
        return True


def show(*, first: str, second: str | None = None, season_one_watched: bool = False) -> TvShow:
    def season(number: int, availability: str, watched: bool) -> TvSeason:
        return TvSeason(
            number,
            TvAvailability(availability, "PL", NOW),
            (TvEpisode(number * 100, number, 1, number * 1000, NOW, NOW if watched else None),),
        )

    seasons = [season(1, first, season_one_watched)]
    if second:
        seasons.append(season(2, second, False))
    return TvShow(
        1,
        TVDB_ID,
        "Example",
        TvAvailability("unknown", "PL", NOW),
        tuple(seasons),
        next_episode_season=2 if second and season_one_watched else None,
        tmdb_id=9001,
        imdb_id="tt0009001",
    )


def collected(
    show_value: TvShow,
    *,
    sonarr: SonarrSeries | None = None,
    plex_existing: bool = False,
    library: bool = False,
    ownership: tuple[TvOwnership, ...] = (),
    episode_mappings: tuple[tuple[int, int, int], ...] = (),
) -> TvCollectedState:
    identity = VerifiedTvIdentity(TVDB_ID, 9001, "tt0009001")
    return TvCollectedState(
        TvSnapshot("2", "generation-1", NOW, NOW, "scheduled_full", True, (show_value,)),
        (sonarr,) if sonarr else (),
        episode_mappings,
        (PlexTvShow("plex-7101", identity),) if plex_existing else (),
        frozenset({identity}) if library else frozenset(),
        ownership,
        (),
    )


def apply(plan, state: StateStore, sonarr: Sonarr, plex: Plex, *, adopt: bool = False):
    snapshot = TvSnapshot("2", "generation-1", NOW, NOW, "scheduled_full", True, ())
    blockers = evaluate_tv_plan(
        plan,
        TvSyncPolicy(enabled=True, apply_enabled=True, adoption_enabled=adopt),
        snapshot=snapshot,
        apply_requested=True,
        now=NOW,
    )
    result = TvDestinationExecutor(
        state, sonarr, plex, sonarr_root_folder="/tv", sonarr_quality_profile_id=1
    ).execute(plan, blockers, apply=True, adopt=adopt)
    return blockers, result


def test_unavailable_season_adds_monitors_and_library_presence_adds_plex_watchlist() -> None:
    state, sonarr, plex = StateStore(), Sonarr(), Plex()
    plan = build_tv_plan(
        collected(
            show(first="confirmed_unavailable"),
            library=True,
            episode_mappings=((TVDB_ID, 1000, 97001),),
        )
    )

    blockers, result = apply(plan, state, sonarr, plex)

    assert blockers == []
    assert result.errors == ()
    assert sonarr.calls == ["lookup", "add", "monitor_season:1", "search:17:[97001]"]
    assert plex.watchlist == {TVDB_ID}
    assert {(item.destination, item.origin) for item in state.ownership} == {
        ("sonarr", "worker"),
        ("plex_watchlist", "worker"),
    }


def test_provider_available_skips_sonarr_and_completion_advances_to_unavailable_second_season() -> None:
    available = build_tv_plan(collected(show(first="available")))
    advanced = build_tv_plan(collected(show(first="available", second="confirmed_unavailable", season_one_watched=True)))

    assert available.decisions_for("sonarr") == ()
    assert advanced.selected_season_by_tvdb == {TVDB_ID: 2}
    assert {item.action for item in advanced.decisions_for("sonarr")} == {"sonarr_add", "sonarr_monitor_season", "skip"}


def test_no_provider_or_library_removes_exact_plex_row_and_adopts_manual_sonarr_only_when_requested() -> None:
    existing = SonarrSeries(17, TVDB_ID, "Example", True, {1: True}, {"id": 17, "tvdbId": TVDB_ID})
    removal_plan = build_tv_plan(collected(show(first="confirmed_unavailable"), plex_existing=True))
    adoption_plan = build_tv_plan(collected(show(first="confirmed_unavailable"), sonarr=existing))
    state, sonarr, plex = StateStore(), Sonarr(existing), Plex()
    plex.watchlist.add(TVDB_ID)

    removal_blockers, _ = apply(removal_plan, state, sonarr, plex)
    sonarr.calls.clear()
    adoption_blockers, _ = apply(adoption_plan, state, sonarr, plex, adopt=True)

    assert removal_blockers == []
    assert plex.calls == ["remove"]
    assert plex.watchlist == set()
    assert adoption_blockers == []
    assert TvOwnership("sonarr", TVDB_ID, "manual") in state.ownership
    assert sonarr.calls == []


def test_unknown_provider_blocks_sonarr_and_second_apply_converges_to_keep_or_skip() -> None:
    unknown_plan = build_tv_plan(collected(show(first="unknown")))
    existing = SonarrSeries(17, TVDB_ID, "Example", True, {1: True}, {"id": 17, "tvdbId": TVDB_ID})
    converged_plan = build_tv_plan(
        collected(
            show(first="available"),
            sonarr=existing,
            plex_existing=True,
            ownership=(TvOwnership("sonarr", TVDB_ID, "worker"), TvOwnership("plex_watchlist", TVDB_ID, "worker")),
        )
    )
    state, sonarr, plex = StateStore(), Sonarr(), Plex()

    blockers, unknown_result = apply(unknown_plan, state, sonarr, plex)
    convergence_blockers, convergence_result = apply(converged_plan, state, sonarr, plex)

    assert "sonarr_provider_availability_uncertain" in blockers
    assert unknown_result.statuses == tuple("blocked" for _ in unknown_plan.decisions)
    assert sonarr.calls == [] and plex.calls == []
    assert convergence_blockers == []
    assert convergence_result.errors == ()
    assert {item.action for item in converged_plan.decisions} <= {"keep", "skip"}
