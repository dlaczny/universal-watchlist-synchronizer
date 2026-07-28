"""Execute only explicitly approved reversible TV destination operations."""

from __future__ import annotations

from dataclasses import dataclass

from src.clients.plex_tv_client import VerifiedTvIdentity
from src.models.tv_destination import TvDecision, TvPlan


@dataclass(frozen=True)
class TvDestinationExecutionResult:
    plan: TvPlan
    statuses: tuple[str, ...]
    errors: tuple[str, ...]


class TvDestinationExecutor:
    """Apply a fresh deterministic plan; no delete action exists in this surface."""

    executable_actions = frozenset(
        {
            "sonarr_add",
            "sonarr_monitor_series",
            "sonarr_monitor_season",
            "sonarr_search_episodes",
            "plex_add",
            "plex_remove",
        }
    )

    def __init__(
        self,
        state_store,
        sonarr_client,
        plex_client,
        *,
        sonarr_root_folder: str,
        sonarr_quality_profile_id: int,
    ) -> None:
        self.state_store = state_store
        self.sonarr_client = sonarr_client
        self.plex_client = plex_client
        self.sonarr_root_folder = sonarr_root_folder
        self.sonarr_quality_profile_id = sonarr_quality_profile_id
        self._series_by_tvdb: dict[int, object] = {}

    def execute(
        self,
        plan: TvPlan,
        blockers: tuple[str, ...] | list[str],
        apply: bool,
        adopt: bool,
    ) -> TvDestinationExecutionResult:
        if blockers and apply:
            return TvDestinationExecutionResult(plan, tuple("blocked" for _ in plan.decisions), ())
        if not apply:
            return TvDestinationExecutionResult(plan, tuple("dry_run" for _ in plan.decisions), ())

        statuses: list[str] = []
        errors: list[str] = []
        for decision in plan.decisions:
            try:
                status = self._execute_decision(decision, adopt=adopt)
                statuses.append(status)
                if status == "completed":
                    self.state_store.record_action(decision.action_id, decision.action, "completed")
            except Exception:
                statuses.append("error")
                # Do not place a client exception (which can contain headers or URLs) in reports.
                errors.append(f"{decision.action_id}: failed")
                self.state_store.record_action(decision.action_id, decision.action, "failed")
        return TvDestinationExecutionResult(plan, tuple(statuses), tuple(errors))

    def _execute_decision(self, decision: TvDecision, *, adopt: bool) -> str:
        if decision.action in {"keep", "skip", "uncertain"}:
            return "skipped"
        tvdb_id = self._tvdb_id(decision)
        if decision.action == "sonarr_adoption_candidate":
            if not adopt:
                return "skipped"
            self.state_store.record_ownership("sonarr", tvdb_id, "manual")
            return "completed"
        if decision.action == "sonarr_add":
            lookup = self.sonarr_client.lookup_by_tvdb(tvdb_id)
            series = self.sonarr_client.add_series(
                lookup, self.sonarr_root_folder, self.sonarr_quality_profile_id
            )
            self._series_by_tvdb[tvdb_id] = series
            self.state_store.record_ownership("sonarr", tvdb_id, "worker")
            return "completed"
        if decision.action == "sonarr_monitor_series":
            series = self._series(tvdb_id)
            self._series_by_tvdb[tvdb_id] = self.sonarr_client.set_series_monitored(series, monitored=True)
            return "completed"
        if decision.action == "sonarr_monitor_season":
            if decision.selected_season_number is None:
                raise ValueError("selected season is required")
            series = self._series(tvdb_id)
            self._series_by_tvdb[tvdb_id] = self.sonarr_client.set_season_monitored(
                series, decision.selected_season_number
            )
            return "completed"
        if decision.action == "sonarr_search_episodes":
            series = self._series(tvdb_id)
            self.sonarr_client.search_episode_ids(series.series_id, list(decision.episode_numbers))
            return "completed"
        if decision.action == "plex_add":
            added = self.plex_client.add_watchlist_show(
                VerifiedTvIdentity(tvdb_id, decision.tmdb_id, decision.imdb_id),
                decision.title or "",
            )
            if not added:
                raise RuntimeError("Plex discovery identity was not found")
            self.state_store.record_ownership("plex_watchlist", tvdb_id, "worker")
            return "completed"
        if decision.action == "plex_remove":
            self.plex_client.remove_watchlist_show(tvdb_id, decision.tmdb_id, decision.imdb_id)
            return "completed"
        raise RuntimeError("TV action is not executable")

    def _series(self, tvdb_id: int):
        series = self._series_by_tvdb.get(tvdb_id)
        if series is None:
            series = self.sonarr_client.get_series_by_tvdb(tvdb_id)
        if series is None:
            raise RuntimeError("exact Sonarr series is unavailable")
        return series

    @staticmethod
    def _tvdb_id(decision: TvDecision) -> int:
        value = decision.tvdb_id
        if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
            raise ValueError("positive TVDB identity is required")
        return value
