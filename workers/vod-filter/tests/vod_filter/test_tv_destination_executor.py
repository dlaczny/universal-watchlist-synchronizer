from __future__ import annotations

from types import MappingProxyType

from src.models.tv_destination import TvDecision, TvPlan
from src.services.tv_destination_executor import TvDestinationExecutor


class FakeStateStore:
    def __init__(self) -> None:
        self.actions: list[tuple[str, str]] = []
        self.ownership: list[tuple[str, int, str]] = []

    def record_action(self, action_id: str, action: str, status: str) -> None:
        self.actions.append((action, status))

    def record_ownership(self, destination: str, tvdb_id: int, origin: str) -> None:
        self.ownership.append((destination, tvdb_id, origin))


class FakeSonarr:
    def __init__(self) -> None:
        self.calls: list[str] = []

    def lookup_by_tvdb(self, tvdb_id: int):
        self.calls.append("lookup")
        return object()

    def add_series(self, lookup, root_folder: str, quality_profile_id: int):
        self.calls.append("add")
        return type("Series", (), {"series_id": 9})()

    def get_series_by_tvdb(self, tvdb_id: int):
        self.calls.append("get")
        return type("Series", (), {"series_id": 9})()

    def set_series_monitored(self, series, monitored: bool):
        self.calls.append("monitor_series")
        return series

    def set_season_monitored(self, series, season_number: int):
        self.calls.append("monitor_season")
        return series

    def search_episode_ids(self, series_id: int, episode_ids: list[int]) -> None:
        self.calls.append("search")


class FakePlex:
    def __init__(self) -> None:
        self.calls: list[str] = []

    def add_watchlist_show(self, identity) -> bool:
        self.calls.append("add")
        return True

    def remove_watchlist_show(self, tvdb_id: int, tmdb_id: int | None, imdb_id: str | None) -> bool:
        self.calls.append("remove")
        return True


def decision(action: str, *, tvdb_id: int = 100, destination: str = "plex_watchlist") -> TvDecision:
    return TvDecision(action, destination, action, tvdb_id, 1, "test", tmdb_id=200, imdb_id="tt0000200")


def plan_with(*decisions: TvDecision) -> TvPlan:
    return TvPlan("generation-1", decisions, MappingProxyType({}), (), True)


def test_executor_records_plex_removal_before_next_action() -> None:
    state = FakeStateStore()
    result = TvDestinationExecutor(state, FakeSonarr(), FakePlex(), sonarr_root_folder="/tv", sonarr_quality_profile_id=1).execute(
        plan_with(decision("plex_remove"), decision("plex_add")), blockers=(), apply=True, adopt=False
    )

    assert state.actions == [("plex_remove", "completed"), ("plex_add", "completed")]
    assert result.errors == ()


def test_executor_adoption_only_records_manual_ownership_without_monitoring() -> None:
    state = FakeStateStore()
    sonarr = FakeSonarr()

    result = TvDestinationExecutor(state, sonarr, FakePlex(), sonarr_root_folder="/tv", sonarr_quality_profile_id=1).execute(
        plan_with(decision("sonarr_adoption_candidate", destination="sonarr")), blockers=(), apply=True, adopt=True
    )

    assert result.errors == ()
    assert state.ownership == [("sonarr", 100, "manual")]
    assert sonarr.calls == []


def test_executor_blocks_all_mutation_and_records_no_audit_when_policy_blocks() -> None:
    state = FakeStateStore()
    plex = FakePlex()

    result = TvDestinationExecutor(state, FakeSonarr(), plex, sonarr_root_folder="/tv", sonarr_quality_profile_id=1).execute(
        plan_with(decision("plex_add")), blockers=("tv_snapshot_stale",), apply=True, adopt=False
    )

    assert result.errors == ()
    assert state.actions == []
    assert plex.calls == []


def test_executor_never_exposes_sonarr_delete_action() -> None:
    executor = TvDestinationExecutor(FakeStateStore(), FakeSonarr(), FakePlex(), sonarr_root_folder="/tv", sonarr_quality_profile_id=1)

    assert "delete" not in executor.executable_actions
