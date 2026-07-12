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
        self._validate_file_deletion_authorization(decision)
        if decision.action in {"skip", "uncertain", "error"}:
            return "skipped"

        tmdb_id = decision.movie.tmdb_id
        if tmdb_id is None:
            return "skipped"
        if isinstance(tmdb_id, bool) or not isinstance(tmdb_id, int) or tmdb_id <= 0:
            raise RuntimeError("invalid TMDB identity")

        if decision.action == "remove":
            return self._execute_removal(decision, tmdb_id)

        if decision.area == "radarr":
            return self._execute_radarr(decision, tmdb_id)
        if decision.area == "plex_watchlist":
            return self._execute_plex_watchlist(decision, tmdb_id)

        return "skipped"

    def _execute_removal(
        self,
        decision: ReconciliationDecision,
        tmdb_id: int,
    ) -> str:
        self._validate_removal_authorization(decision)
        try:
            if decision.area == "radarr":
                status = self._execute_radarr(decision, tmdb_id)
            elif decision.area == "plex_watchlist":
                status = self._execute_plex_watchlist(decision, tmdb_id)
            else:
                return "skipped"
        except Exception as error:
            self._record_cleanup_attempt(decision, tmdb_id, "error", str(error))
            raise

        self._record_cleanup_attempt(decision, tmdb_id, "completed", None)
        return status

    @staticmethod
    def _validate_removal_authorization(
        decision: ReconciliationDecision,
    ) -> None:
        MovieSyncExecutor._validate_file_deletion_authorization(decision)
        event_id = decision.authorization_event_id
        valid_watched_event = isinstance(event_id, str) and bool(event_id.strip())

        if decision.authorization is None:
            return

        if decision.authorization == "letterboxd_watched":
            valid_destination = decision.area in {"radarr", "plex_watchlist"}
            expected_file_deletion = decision.area == "radarr"
            if (
                not valid_destination
                or not valid_watched_event
                or decision.delete_files != expected_file_deletion
            ):
                raise RuntimeError("invalid watched cleanup authorization")
            return

        if decision.authorization == "manual_radarr_removal":
            if (
                decision.area != "plex_watchlist"
                or decision.delete_files
                or decision.authorization_event_id is not None
            ):
                raise RuntimeError("invalid manual cleanup authorization")
            return

        raise RuntimeError("invalid cleanup authorization")

    @staticmethod
    def _validate_file_deletion_authorization(
        decision: ReconciliationDecision,
    ) -> None:
        if not decision.delete_files:
            return
        event_id = decision.authorization_event_id
        if not (
            decision.area == "radarr"
            and decision.action == "remove"
            and decision.authorization == "letterboxd_watched"
            and isinstance(event_id, str)
            and bool(event_id.strip())
        ):
            raise RuntimeError("invalid file deletion authorization")

    def _record_cleanup_attempt(
        self,
        decision: ReconciliationDecision,
        tmdb_id: int,
        status: str,
        error: str | None,
    ) -> None:
        if decision.authorization is None:
            return
        self.cache_service.record_cleanup_attempt(
            authorization=decision.authorization,
            authorization_event_id=decision.authorization_event_id,
            destination=decision.area,
            tmdb_id=tmdb_id,
            delete_files=decision.delete_files,
            status=status,
            error=error,
        )

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
            self.radarr_client.remove_movie(
                tmdb_id,
                delete_files=decision.delete_files,
            )
            if decision.authorization == "letterboxd_watched":
                self.cache_service.mark_radarr_removed_by_worker(
                    tmdb_id,
                    decision.authorization_event_id,
                )
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
