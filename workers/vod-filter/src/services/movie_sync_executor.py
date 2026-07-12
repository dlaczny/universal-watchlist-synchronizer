"""Apply policy-approved movie sync decisions to Radarr and Plex."""

from __future__ import annotations

from dataclasses import dataclass, replace

from src.services.sync_reconciliation import (
    ReconciliationDecision,
    SyncReconciliationReport,
)


@dataclass(frozen=True)
class MovieSyncExecutionResult:
    report: SyncReconciliationReport
    errors: tuple[str, ...]


class _DecisionSkipped(Exception):
    """Convert a destination limitation into an explicit reported skip."""

    def __init__(self, reason: str):
        super().__init__(reason)
        self.reason = reason


class MovieSyncExecutor:
    """Execute one already-computed plan without changing its decisions."""

    def __init__(self, radarr_client, plex_client, cache_service):
        self.radarr_client = radarr_client
        self.plex_client = plex_client
        self.cache_service = cache_service

    def execute(
        self,
        report: SyncReconciliationReport,
        blockers: list[str],
        apply: bool,
    ) -> MovieSyncExecutionResult:
        if blockers and apply:
            return MovieSyncExecutionResult(
                report=replace(
                    report,
                    decisions=[
                        replace(decision, execution_status="blocked")
                        for decision in report.decisions
                    ],
                ),
                errors=(),
            )

        if not apply:
            return MovieSyncExecutionResult(
                report=replace(
                    report,
                    decisions=[
                        replace(decision, execution_status="dry_run")
                        for decision in report.decisions
                    ],
                ),
                errors=(),
            )

        executed: list[ReconciliationDecision] = []
        errors: list[str] = []
        for decision in report.decisions:
            try:
                status = self._execute_decision(decision)
                executed.append(replace(decision, execution_status=status))
            except _DecisionSkipped as skipped:
                executed.append(
                    replace(
                        decision,
                        action="skip",
                        reason=skipped.reason,
                        execution_status="skipped",
                    )
                )
            except Exception as error:
                tmdb_id = decision.movie.tmdb_id or "unknown"
                errors.append(
                    f"{decision.area}/{decision.action}/{tmdb_id}: {error}"
                )
                executed.append(replace(decision, execution_status="error"))

        return MovieSyncExecutionResult(
            report=replace(report, decisions=executed),
            errors=tuple(errors),
        )

    def _execute_decision(self, decision: ReconciliationDecision) -> str:
        if decision.action in {"skip", "uncertain", "error"}:
            return "skipped"

        tmdb_id = decision.movie.tmdb_id
        if tmdb_id is None:
            return "skipped"

        if decision.area == "radarr":
            return self._execute_radarr(decision, tmdb_id)
        if decision.area == "plex_watchlist":
            return self._execute_plex_watchlist(decision, tmdb_id)

        return "skipped"

    def _execute_radarr(self, decision: ReconciliationDecision, tmdb_id: int) -> str:
        if decision.action == "add":
            if decision.movie.year is None:
                raise RuntimeError("movie year is required for Radarr add")
            result = self.radarr_client.add_movie(
                tmdb_id,
                decision.movie.title,
                decision.movie.year,
                override_exclusion=(
                    decision.reason
                    == "desired_radarr_movie_missing_override_exclusion"
                ),
            )
            if result is None:
                raise RuntimeError("Radarr add returned no result")
            self.cache_service.mark_managed("radarr", tmdb_id, "add")
            return "completed"

        if decision.action == "remove":
            self.radarr_client.remove_movie(tmdb_id, delete_files=False)
            self.cache_service.release_managed("radarr", tmdb_id)
            return "completed"

        if decision.action == "keep":
            action = "keep" if decision.managed else "adopt"
            self.cache_service.mark_managed("radarr", tmdb_id, action)
            return "completed"

        return "skipped"

    def _execute_plex_watchlist(
        self,
        decision: ReconciliationDecision,
        tmdb_id: int,
    ) -> str:
        if decision.action == "add":
            if decision.movie.year is None:
                raise RuntimeError("movie year is required for Plex watchlist add")
            added = self.plex_client.add_to_watchlist(
                tmdb_id,
                decision.movie.title,
                decision.movie.year,
            )
            if not added:
                raise _DecisionSkipped("plex_discovery_identity_not_found")
            self.cache_service.mark_managed("plex_watchlist", tmdb_id, "add")
            return "completed"

        if decision.action == "remove":
            self.plex_client.remove_from_watchlist(tmdb_id, decision.movie.title)
            self.cache_service.release_managed("plex_watchlist", tmdb_id)
            return "completed"

        if decision.action == "keep":
            action = "keep" if decision.managed else "adopt"
            self.cache_service.mark_managed("plex_watchlist", tmdb_id, action)
            return "completed"

        return "skipped"
