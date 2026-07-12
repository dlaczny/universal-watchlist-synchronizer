"""Read-only reconciliation between backend, worker cache, Radarr, and Plex state."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable, Mapping


@dataclass(frozen=True)
class ReconciliationMovie:
    """Normalized movie shape used by the reconciliation report."""

    title: str
    year: int | None = None
    tmdb_id: int | None = None
    imdb_id: str | None = None
    source: str | None = None
    source_id: str | None = None
    availability_status: str | None = None
    library_membership: str | None = None
    owned_service_availability: tuple[str, ...] = ()
    has_file: bool | None = None
    on_radarr: bool | None = None
    on_plex: bool | None = None
    sync_error: str | None = None
    metadata_status: str | None = None
    radarr_eligible: bool | None = None
    watched_at: datetime | None = None
    lifecycle_version: int | None = None
    lifecycle_event_id: str | None = None
    present: bool | None = None
    disappearance_cause: str | None = None
    source_event_id: str | None = None


@dataclass(frozen=True)
class ReconciliationDecision:
    """One read-only finding from comparing desired and live state."""

    area: str
    action: str
    movie: ReconciliationMovie
    reason: str
    managed: bool = False
    execution_status: str = "planned"
    delete_files: bool = False
    authorization: str | None = None
    authorization_event_id: str | None = None


@dataclass(frozen=True)
class SyncReconciliationReport:
    """Complete reconciliation result."""

    generated_at: datetime
    source_counts: dict[str, int]
    decisions: list[ReconciliationDecision] = field(default_factory=list)
    source_snapshot_at: datetime | None = None
    source_last_successful_sync_at: datetime | None = None
    backend_snapshot_provided: bool = False
    managed_destination_count: int = 0
    source_snapshot_id: str | None = None


def reconcile_sync_state(
    *,
    backend_watchlist_movies: Iterable[Any] | None = None,
    backend_radarr_export_movies: Iterable[Any] | None = None,
    backend_snapshot_movies: Iterable[Any] | None = None,
    backend_watched_movies: Iterable[Any] | None = None,
    cache_radarr_movies: Iterable[Any] | None = None,
    cache_sync_states: Iterable[Any] | None = None,
    radarr_movies: Iterable[Any] | None = None,
    radarr_observations: Iterable[Any] | None = None,
    radarr_exclusions: Iterable[Any] | None = None,
    plex_watchlist_movies: Iterable[Any] | None = None,
    plex_library_movies: Iterable[Any] | None = None,
    managed_destinations: Iterable[Any] | None = None,
    collection_errors: Iterable[str] | None = None,
    source_snapshot_at: datetime | None = None,
    source_last_successful_sync_at: datetime | None = None,
    source_snapshot_id: str | None = None,
) -> SyncReconciliationReport:
    """Build a read-only report of sync actions and uncertain state.

    The report uses backend watchlist/export data as desired state, then
    compares it with live Radarr/Plex reads and local worker cache flags.
    """

    backend_watchlist_raw = list(backend_watchlist_movies or [])
    backend_radarr_export_raw = list(backend_radarr_export_movies or [])
    backend_snapshot_raw = list(backend_snapshot_movies or [])
    backend_watched_raw = list(backend_watched_movies or [])
    cache_radarr_raw = list(cache_radarr_movies or [])
    cache_sync_raw = list(cache_sync_states or [])
    radarr_raw = list(radarr_movies or [])
    radarr_observation_raw = list(radarr_observations or [])
    radarr_exclusion_raw = list(radarr_exclusions or [])
    plex_watchlist_raw = list(plex_watchlist_movies or [])
    plex_library_raw = list(plex_library_movies or [])
    collection_error_raw = list(collection_errors or [])
    managed_destination_raw = list(managed_destinations or [])

    if backend_snapshot_movies is not None:
        backend_watchlist_raw = backend_snapshot_raw
        backend_radarr_export_raw = [
            item
            for item in backend_snapshot_raw
            if _to_bool(_read(item, "radarr_eligible", "radarrEligible")) is True
        ]

    backend_watchlist = _coerce_collection(backend_watchlist_raw, "backend_watchlist")
    backend_radarr_export = _coerce_collection(
        backend_radarr_export_raw,
        "backend_radarr_export",
    )
    backend_watched = _coerce_collection(backend_watched_raw, "backend_watched")
    cache_radarr = _coerce_collection(cache_radarr_raw, "worker_cache_radarr")
    cache_sync = _coerce_collection(cache_sync_raw, "worker_cache_sync")
    radarr_live = _coerce_collection(radarr_raw, "radarr")
    radarr_observation_state = _coerce_collection(
        radarr_observation_raw,
        "radarr_observations",
    )
    plex_watchlist = _coerce_collection(plex_watchlist_raw, "plex_watchlist")
    plex_library = _coerce_collection(plex_library_raw, "plex_library")

    source_counts = {
        "backend_watchlist": len(backend_watchlist),
        "backend_radarr_export": len(backend_radarr_export),
        "backend_snapshot": len(backend_snapshot_raw),
        "backend_watched": len(backend_watched),
        "worker_cache_radarr": len(cache_radarr),
        "worker_cache_sync": len(cache_sync),
        "radarr": len(radarr_live),
        "radarr_observations": len(radarr_observation_state),
        "radarr_exclusions": len(radarr_exclusion_raw),
        "plex_watchlist": len(plex_watchlist),
        "plex_library": len(plex_library),
        "collection_errors": len(collection_error_raw),
    }

    decisions: list[ReconciliationDecision] = []
    managed_by_destination = _managed_ids(managed_destination_raw)
    backend_watchlist_by_id = _index_by_tmdb(
        backend_watchlist,
        "backend_watchlist",
        decisions,
    )
    backend_radarr_by_id = _index_by_tmdb(
        backend_radarr_export,
        "backend_radarr_export",
        decisions,
    )
    backend_watched_by_id = _index_by_tmdb(
        backend_watched,
        "backend_watched",
        decisions,
        missing_reason="watched_movie_missing_tmdb_identity",
    )
    for tmdb_id, movie in list(backend_watched_by_id.items()):
        if movie.lifecycle_event_id:
            continue
        decisions.append(
            ReconciliationDecision(
                area="source_identity",
                action="skip",
                movie=movie,
                reason="watched_movie_missing_lifecycle_event_id",
            )
        )
        backend_watched_by_id.pop(tmdb_id)
    cache_radarr_by_id = _index_by_tmdb(cache_radarr, "worker_cache_radarr", decisions)
    cache_sync_by_id = _index_by_tmdb(cache_sync, "worker_cache_sync", decisions)
    radarr_by_id = _index_by_tmdb(radarr_live, "radarr", decisions)
    radarr_observations_by_id = _index_by_tmdb(
        radarr_observation_state,
        "radarr_observations",
        decisions,
    )
    radarr_excluded_ids = {
        tmdb_id
        for item in radarr_exclusion_raw
        if (tmdb_id := _to_int(_read(item, "tmdb_id", "tmdbId"))) is not None
    }
    plex_watchlist_by_id = _index_by_tmdb(
        plex_watchlist,
        "plex_watchlist",
        decisions,
    )
    plex_library_by_id = _index_by_tmdb(plex_library, "plex_library", decisions)

    active_tmdb_ids = set(backend_watchlist_by_id)
    conflict_ids = active_tmdb_ids & set(backend_watched_by_id)
    for tmdb_id in sorted(conflict_ids):
        decisions.append(
            ReconciliationDecision(
                area="source_identity",
                action="uncertain",
                movie=backend_watched_by_id[tmdb_id],
                reason="active_watched_tmdb_identity_conflict",
            )
        )
        backend_watchlist_by_id.pop(tmdb_id, None)
        backend_radarr_by_id.pop(tmdb_id, None)
        backend_watched_by_id.pop(tmdb_id, None)

    manual_disappearances_by_id = {
        tmdb_id: observation
        for tmdb_id, observation in radarr_observations_by_id.items()
        if observation.present is False
        and observation.disappearance_cause == "manual"
        and tmdb_id not in active_tmdb_ids
        and tmdb_id not in backend_watched_by_id
        and tmdb_id not in conflict_ids
    }
    source_counts["watched_authorizations"] = len(backend_watched_by_id)
    source_counts["manual_radarr_disappearances"] = len(
        manual_disappearances_by_id
    )

    decisions.extend(
        _watched_cleanup_decisions(
            backend_watched_by_id,
            radarr_by_id,
            plex_watchlist_by_id,
            managed_by_destination,
        )
    )
    decisions.extend(
        _manual_radarr_cleanup_decisions(
            manual_disappearances_by_id,
            plex_watchlist_by_id,
            managed_by_destination["plex_watchlist"],
        )
    )

    suppressed_destination_ids = (
        set(backend_watched_by_id)
        | set(manual_disappearances_by_id)
        | conflict_ids
    )
    normal_radarr_by_id = {
        tmdb_id: movie
        for tmdb_id, movie in radarr_by_id.items()
        if tmdb_id not in suppressed_destination_ids
    }
    normal_plex_watchlist_by_id = {
        tmdb_id: movie
        for tmdb_id, movie in plex_watchlist_by_id.items()
        if tmdb_id not in suppressed_destination_ids
    }
    normal_plex_library_by_id = {
        tmdb_id: movie
        for tmdb_id, movie in plex_library_by_id.items()
        if tmdb_id not in suppressed_destination_ids
    }

    if backend_snapshot_movies is not None:
        decisions.extend(_metadata_decisions(backend_watchlist_by_id))

    if backend_radarr_export_movies is not None or backend_snapshot_movies is not None:
        decisions.extend(
            _radarr_decisions(
                backend_radarr_by_id,
                normal_radarr_by_id,
                managed_by_destination["radarr"],
                radarr_excluded_ids,
                active_source_ids=(
                    active_tmdb_ids
                    if backend_snapshot_movies is not None
                    else None
                ),
            )
        )

    if backend_radarr_export_movies is not None and cache_radarr_movies is not None:
        decisions.extend(_cache_radarr_drift_decisions(backend_radarr_by_id, cache_radarr_by_id))

    decisions.extend(
        _plex_watchlist_decisions(
            backend_watchlist_by_id,
            normal_radarr_by_id,
            normal_plex_watchlist_by_id,
            normal_plex_library_by_id,
            managed_by_destination["plex_watchlist"],
        )
    )

    if cache_sync_states is not None:
        decisions.extend(
            _cache_decisions(
                cache_sync_by_id,
                radarr_by_id if radarr_movies is not None else None,
                plex_watchlist_by_id if plex_watchlist_movies is not None else None,
            )
        )

    for error in collection_error_raw:
        decisions.append(
            ReconciliationDecision(
                area="collection",
                action="error",
                movie=ReconciliationMovie(title="Collection error"),
                reason=str(error),
            )
        )

    return SyncReconciliationReport(
        generated_at=datetime.now(timezone.utc),
        source_counts=source_counts,
        decisions=sorted(decisions, key=_decision_sort_key),
        source_snapshot_at=source_snapshot_at,
        source_last_successful_sync_at=source_last_successful_sync_at,
        backend_snapshot_provided=backend_snapshot_movies is not None,
        managed_destination_count=len(managed_destination_raw),
        source_snapshot_id=source_snapshot_id,
    )


def render_markdown_report(report: SyncReconciliationReport) -> str:
    """Render a reconciliation report as Markdown."""

    lines = [
        "# Sync reconciliation report",
        "",
        f"Generated: {report.generated_at.isoformat(timespec='seconds')}",
        f"Source snapshot ID: {report.source_snapshot_id or 'unknown'}",
        f"Source snapshot: {_format_datetime(report.source_snapshot_at)}",
        "Last successful movie sync: "
        f"{_format_datetime(report.source_last_successful_sync_at)}",
        "",
        "## Inputs",
        "",
    ]

    for source, count in report.source_counts.items():
        lines.append(f"- {source}: {count}")

    lines.extend(["", "## Summary", ""])
    for area, action in _decision_groups(report.decisions):
        count = len(_filter_decisions(report.decisions, area, action))
        lines.append(f"- {area}/{action}: {count}")

    if not report.decisions:
        lines.append("- No decisions")

    lines.append("")

    for area, action in _decision_groups(report.decisions):
        decisions = _filter_decisions(report.decisions, area, action)
        lines.extend([f"## {_area_title(area)} - {action} ({len(decisions)})", ""])
        for decision in decisions:
            lines.append(
                f"- {_format_movie(decision.movie)} - {decision.reason} - "
                f"{_format_authorization(decision)}"
            )
        lines.append("")

    return "\n".join(lines)


def write_markdown_report(report: SyncReconciliationReport, path: Path) -> Path:
    """Write a Markdown reconciliation report and return the path."""

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(render_markdown_report(report), encoding="utf-8")
    return path


def _watched_cleanup_decisions(
    watched_by_id: dict[int, ReconciliationMovie],
    radarr_by_id: dict[int, ReconciliationMovie],
    plex_watchlist_by_id: dict[int, ReconciliationMovie],
    managed_by_destination: dict[str, set[int]],
) -> list[ReconciliationDecision]:
    decisions: list[ReconciliationDecision] = []
    for tmdb_id, movie in watched_by_id.items():
        authorization_fields = {
            "authorization": "letterboxd_watched",
            "authorization_event_id": movie.lifecycle_event_id,
        }
        if tmdb_id in radarr_by_id:
            decisions.append(
                ReconciliationDecision(
                    area="radarr",
                    action="remove",
                    movie=movie,
                    reason="watched_letterboxd_movie_remove_from_radarr",
                    managed=tmdb_id in managed_by_destination["radarr"],
                    delete_files=True,
                    **authorization_fields,
                )
            )
        else:
            decisions.append(
                ReconciliationDecision(
                    area="radarr",
                    action="skip",
                    movie=movie,
                    reason="watched_letterboxd_movie_absent_from_radarr",
                    managed=tmdb_id in managed_by_destination["radarr"],
                    **authorization_fields,
                )
            )

        if tmdb_id in plex_watchlist_by_id:
            decisions.append(
                ReconciliationDecision(
                    area="plex_watchlist",
                    action="remove",
                    movie=movie,
                    reason="watched_letterboxd_movie_remove_from_plex_watchlist",
                    managed=tmdb_id in managed_by_destination["plex_watchlist"],
                    **authorization_fields,
                )
            )
        else:
            decisions.append(
                ReconciliationDecision(
                    area="plex_watchlist",
                    action="skip",
                    movie=movie,
                    reason="watched_letterboxd_movie_absent_from_plex_watchlist",
                    managed=tmdb_id in managed_by_destination["plex_watchlist"],
                    **authorization_fields,
                )
            )

    return decisions


def _manual_radarr_cleanup_decisions(
    manual_disappearances_by_id: dict[int, ReconciliationMovie],
    plex_watchlist_by_id: dict[int, ReconciliationMovie],
    managed_ids: set[int],
) -> list[ReconciliationDecision]:
    decisions: list[ReconciliationDecision] = []
    for tmdb_id, movie in manual_disappearances_by_id.items():
        if tmdb_id in plex_watchlist_by_id:
            action = "remove"
            reason = "manually_removed_radarr_movie_remove_from_plex_watchlist"
        else:
            action = "skip"
            reason = "manually_removed_radarr_movie_absent_from_plex_watchlist"
        decisions.append(
            ReconciliationDecision(
                area="plex_watchlist",
                action=action,
                movie=movie,
                reason=reason,
                managed=tmdb_id in managed_ids,
                authorization="manual_radarr_removal",
            )
        )
    return decisions


def _radarr_decisions(
    backend_radarr_by_id: dict[int, ReconciliationMovie],
    radarr_by_id: dict[int, ReconciliationMovie],
    managed_ids: set[int],
    excluded_ids: set[int],
    active_source_ids: set[int] | None = None,
) -> list[ReconciliationDecision]:
    decisions = []

    for tmdb_id, movie in backend_radarr_by_id.items():
        if tmdb_id in radarr_by_id:
            decisions.append(
                ReconciliationDecision(
                    area="radarr",
                    action="keep",
                    movie=movie,
                    reason=(
                        "desired_radarr_movie_present"
                        if tmdb_id in managed_ids
                        else "desired_radarr_movie_present_adopt_managed"
                    ),
                    managed=tmdb_id in managed_ids,
                )
            )
        else:
            title_year = _title_year_key(movie)
            has_path_collision = title_year is not None and any(
                _title_year_key(live_movie) == title_year
                for live_movie in radarr_by_id.values()
            )
            if has_path_collision:
                decisions.append(
                    ReconciliationDecision(
                        area="radarr",
                        action="skip",
                        movie=movie,
                        reason="radarr_title_year_collision_requires_manual_review",
                        managed=tmdb_id in managed_ids,
                    )
                )
            else:
                decisions.append(
                    ReconciliationDecision(
                        area="radarr",
                        action="add",
                        movie=movie,
                        reason=(
                            "desired_radarr_movie_missing_override_exclusion"
                            if tmdb_id in excluded_ids
                            else "desired_radarr_movie_missing"
                        ),
                        managed=tmdb_id in managed_ids,
                    )
                )

    for tmdb_id, movie in radarr_by_id.items():
        if tmdb_id not in backend_radarr_by_id:
            if active_source_ids is not None and tmdb_id not in active_source_ids:
                decisions.append(
                    ReconciliationDecision(
                        area="radarr",
                        action="skip",
                        movie=movie,
                        reason="radarr_movie_without_source_authorization_preserved",
                        managed=tmdb_id in managed_ids,
                    )
                )
            elif tmdb_id not in managed_ids:
                decisions.append(
                    ReconciliationDecision(
                        area="radarr",
                        action="skip",
                        movie=movie,
                        reason="unmanaged_radarr_movie_preserved",
                    )
                )
            elif movie.has_file:
                decisions.append(
                    ReconciliationDecision(
                        area="radarr",
                        action="skip",
                        movie=movie,
                        reason="downloaded_file_requires_manual_review",
                        managed=True,
                    )
                )
            else:
                decisions.append(
                    ReconciliationDecision(
                        area="radarr",
                        action="remove",
                        movie=movie,
                        reason="managed_radarr_movie_no_longer_desired",
                        managed=True,
                    )
                )

    return decisions


def _title_year_key(movie: ReconciliationMovie) -> tuple[str, int] | None:
    if movie.year is None:
        return None
    normalized_title = " ".join(movie.title.casefold().split())
    return normalized_title, movie.year


def _cache_radarr_drift_decisions(
    backend_radarr_by_id: dict[int, ReconciliationMovie],
    cache_radarr_by_id: dict[int, ReconciliationMovie],
) -> list[ReconciliationDecision]:
    decisions = []

    for tmdb_id, movie in backend_radarr_by_id.items():
        if tmdb_id not in cache_radarr_by_id:
            decisions.append(
                ReconciliationDecision(
                    area="worker_cache",
                    action="uncertain",
                    movie=movie,
                    reason="backend_radarr_export_missing_from_worker_cache_radarr_candidates",
                )
            )

    for tmdb_id, movie in cache_radarr_by_id.items():
        if tmdb_id not in backend_radarr_by_id:
            decisions.append(
                ReconciliationDecision(
                    area="worker_cache",
                    action="uncertain",
                    movie=movie,
                    reason="worker_cache_radarr_candidate_missing_from_backend_radarr_export",
                )
            )

    return decisions


def _plex_watchlist_decisions(
    backend_watchlist_by_id: dict[int, ReconciliationMovie],
    radarr_by_id: dict[int, ReconciliationMovie],
    plex_watchlist_by_id: dict[int, ReconciliationMovie],
    plex_library_by_id: dict[int, ReconciliationMovie],
    managed_ids: set[int],
) -> list[ReconciliationDecision]:
    decisions = []
    expected_plex: dict[int, tuple[ReconciliationMovie, str]] = {}

    for tmdb_id, movie in backend_watchlist_by_id.items():
        if _backend_item_expected_in_plex(movie):
            expected_plex[tmdb_id] = (movie, "backend_watchlist_expected_in_plex")

    for tmdb_id, movie in radarr_by_id.items():
        if movie.has_file:
            expected_plex.setdefault(tmdb_id, (movie, "downloaded_radarr_movie_missing_from_plex"))

    for tmdb_id, movie in plex_library_by_id.items():
        expected_plex.setdefault(tmdb_id, (movie, "plex_library_movie_missing_from_watchlist"))

    for tmdb_id, (movie, reason) in expected_plex.items():
        if tmdb_id in plex_watchlist_by_id:
            decisions.append(
                ReconciliationDecision(
                    area="plex_watchlist",
                    action="keep",
                    movie=movie,
                    reason=(
                        "desired_plex_watchlist_movie_present"
                        if tmdb_id in managed_ids
                        else "desired_plex_watchlist_movie_present_adopt_managed"
                    ),
                    managed=tmdb_id in managed_ids,
                )
            )
        else:
            decisions.append(
                ReconciliationDecision(
                    area="plex_watchlist",
                    action="add",
                    movie=movie,
                    reason=reason,
                    managed=tmdb_id in managed_ids,
                )
            )

    for tmdb_id, movie in backend_watchlist_by_id.items():
        if tmdb_id in expected_plex:
            continue
        if tmdb_id in plex_watchlist_by_id:
            decisions.append(
                ReconciliationDecision(
                    area="plex_watchlist",
                    action="keep",
                    movie=movie,
                    reason="backend_watchlist_movie_already_in_plex_watchlist",
                )
            )
        else:
            decisions.append(
                ReconciliationDecision(
                    area="plex_watchlist",
                    action="skip",
                    movie=movie,
                    reason="backend_watchlist_movie_not_ready_for_plex",
                )
            )

    protected_plex_ids = set(backend_watchlist_by_id) | set(plex_library_by_id) | {
        tmdb_id for tmdb_id, movie in radarr_by_id.items() if movie.has_file
    }
    for tmdb_id, movie in plex_watchlist_by_id.items():
        if tmdb_id not in protected_plex_ids:
            if tmdb_id in managed_ids:
                decisions.append(
                    ReconciliationDecision(
                        area="plex_watchlist",
                        action="remove",
                        movie=movie,
                        reason="managed_plex_watchlist_movie_no_longer_desired",
                        managed=True,
                    )
                )
            else:
                decisions.append(
                    ReconciliationDecision(
                        area="plex_watchlist",
                        action="skip",
                        movie=movie,
                        reason="unmanaged_plex_watchlist_movie_preserved",
                    )
                )

    return decisions


def _cache_decisions(
    cache_sync_by_id: dict[int, ReconciliationMovie],
    radarr_by_id: dict[int, ReconciliationMovie] | None,
    plex_watchlist_by_id: dict[int, ReconciliationMovie] | None,
) -> list[ReconciliationDecision]:
    decisions = []

    for tmdb_id, movie in cache_sync_by_id.items():
        if radarr_by_id is not None:
            live_on_radarr = tmdb_id in radarr_by_id
            if movie_has_flag(movie, "on_radarr") and not live_on_radarr:
                decisions.append(
                    ReconciliationDecision(
                        area="worker_cache",
                        action="uncertain",
                        movie=movie,
                        reason="cache_on_radarr_true_but_live_radarr_missing",
                    )
                )
            if not movie_has_flag(movie, "on_radarr") and live_on_radarr:
                decisions.append(
                    ReconciliationDecision(
                        area="worker_cache",
                        action="uncertain",
                        movie=movie,
                        reason="cache_on_radarr_false_but_live_radarr_present",
                    )
                )

        if plex_watchlist_by_id is not None:
            live_on_plex = tmdb_id in plex_watchlist_by_id
            if movie_has_flag(movie, "on_plex") and not live_on_plex:
                decisions.append(
                    ReconciliationDecision(
                        area="worker_cache",
                        action="uncertain",
                        movie=movie,
                        reason="cache_on_plex_true_but_live_plex_missing",
                    )
                )
            if not movie_has_flag(movie, "on_plex") and live_on_plex:
                decisions.append(
                    ReconciliationDecision(
                        area="worker_cache",
                        action="uncertain",
                        movie=movie,
                        reason="cache_on_plex_false_but_live_plex_present",
                    )
                )

        if movie.sync_error:
            decisions.append(
                ReconciliationDecision(
                    area="worker_cache",
                    action="error",
                    movie=movie,
                    reason="cache_sync_error_present",
                )
            )

    return decisions


def _backend_item_expected_in_plex(movie: ReconciliationMovie) -> bool:
    if movie.owned_service_availability:
        return True
    return movie.availability_status == "available_on_plex"


def _metadata_decisions(
    backend_movies_by_id: dict[int, ReconciliationMovie],
) -> list[ReconciliationDecision]:
    return [
        ReconciliationDecision(
            area="source_identity",
            action="uncertain",
            movie=movie,
            reason="backend_metadata_not_enriched",
        )
        for movie in backend_movies_by_id.values()
        if movie.metadata_status not in (None, "enriched")
    ]


def _coerce_collection(items: Iterable[Any], source: str) -> list[ReconciliationMovie]:
    return [_coerce_movie(item, source) for item in items]


def _coerce_movie(item: Any, source: str) -> ReconciliationMovie:
    if isinstance(item, ReconciliationMovie):
        return item

    tmdb_id = _to_int(_read(item, "tmdb_id", "tmdbId"))
    title = _read(item, "title") or (f"TMDB {tmdb_id}" if tmdb_id is not None else "Unknown")
    owned = _read(item, "owned_service_availability", "ownedServiceAvailability") or ()

    movie = ReconciliationMovie(
        title=str(title),
        year=_to_int(_read(item, "year", "release_year", "releaseYear")),
        tmdb_id=tmdb_id,
        imdb_id=_none_or_str(_read(item, "imdb_id", "imdbId")),
        source=_none_or_str(_read(item, "source")) or source,
        source_id=_none_or_str(_read(item, "source_id", "sourceId")),
        availability_status=_none_or_str(
            _read(item, "availability_status", "availabilityStatus"),
        ),
        library_membership=_none_or_str(
            _read(item, "library_membership", "libraryMembership"),
        ),
        owned_service_availability=tuple(str(value) for value in owned),
        has_file=_to_bool(_read(item, "has_file", "hasFile")),
        on_radarr=_to_bool(_read(item, "on_radarr")),
        on_plex=_to_bool(_read(item, "on_plex")),
        sync_error=_none_or_str(_read(item, "sync_error", "syncError")),
        metadata_status=_none_or_str(
            _read(item, "metadata_status", "metadataStatus"),
        ),
        radarr_eligible=_to_bool(
            _read(item, "radarr_eligible", "radarrEligible"),
        ),
        watched_at=_to_datetime(_read(item, "watched_at", "watchedAt")),
        lifecycle_version=_to_int(
            _read(item, "lifecycle_version", "lifecycleVersion")
        ),
        lifecycle_event_id=_none_or_str(
            _read(item, "lifecycle_event_id", "lifecycleEventId")
        ),
        present=_to_bool(_read(item, "present")),
        disappearance_cause=_none_or_str(
            _read(item, "disappearance_cause", "disappearanceCause")
        ),
        source_event_id=_none_or_str(
            _read(item, "source_event_id", "sourceEventId")
        ),
    )
    return movie


def _index_by_tmdb(
    movies: list[ReconciliationMovie],
    source_name: str,
    decisions: list[ReconciliationDecision],
    missing_reason: str | None = None,
) -> dict[int, ReconciliationMovie]:
    by_id: dict[int, ReconciliationMovie] = {}
    seen: dict[int, list[ReconciliationMovie]] = {}

    for movie in movies:
        if movie.tmdb_id is None:
            decisions.append(
                ReconciliationDecision(
                    area="source_identity",
                    action="skip",
                    movie=movie,
                    reason=missing_reason or f"missing_tmdb_id_in_{source_name}",
                )
            )
            continue

        seen.setdefault(movie.tmdb_id, []).append(movie)
        by_id.setdefault(movie.tmdb_id, movie)

    for tmdb_id, duplicate_movies in seen.items():
        if len(duplicate_movies) <= 1:
            continue
        for movie in duplicate_movies:
            decisions.append(
                ReconciliationDecision(
                    area="source_identity",
                    action="uncertain",
                    movie=movie,
                    reason=f"duplicate_tmdb_id_in_{source_name}",
                )
            )
        by_id.pop(tmdb_id, None)

    return by_id


def movie_has_flag(movie: ReconciliationMovie, flag: str) -> bool:
    return bool(getattr(movie, flag, False))


def _managed_ids(items: Iterable[Any]) -> dict[str, set[int]]:
    result = {"radarr": set(), "plex_watchlist": set()}
    for item in items:
        destination = _none_or_str(_read(item, "destination"))
        tmdb_id = _to_int(_read(item, "tmdb_id", "tmdbId"))
        if destination in result and tmdb_id is not None:
            result[destination].add(tmdb_id)
    return result


def _read(item: Any, *keys: str) -> Any:
    for key in keys:
        if isinstance(item, Mapping) and key in item:
            return item[key]
        if hasattr(item, key):
            return getattr(item, key)
    return None


def _to_int(value: Any) -> int | None:
    if value in (None, ""):
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def _to_datetime(value: Any) -> datetime | None:
    if isinstance(value, datetime):
        return value
    if not isinstance(value, str) or not value.strip():
        return None
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None


def _to_bool(value: Any) -> bool | None:
    if value is None:
        return None
    if isinstance(value, bool):
        return value
    if isinstance(value, int):
        return value != 0
    if isinstance(value, str):
        return value.strip().lower() in {"1", "true", "yes", "on"}
    return bool(value)


def _none_or_str(value: Any) -> str | None:
    if value in (None, ""):
        return None
    return str(value)


def _decision_sort_key(decision: ReconciliationDecision) -> tuple[int, int, str, str]:
    area_order = {
        "radarr": 0,
        "plex_watchlist": 1,
        "source_identity": 2,
        "worker_cache": 3,
        "collection": 4,
    }
    action_order = {
        "add": 0,
        "keep": 1,
        "remove": 2,
        "skip": 3,
        "uncertain": 4,
        "error": 5,
    }
    return (
        area_order.get(decision.area, 99),
        action_order.get(decision.action, 99),
        decision.movie.title.lower(),
        decision.reason,
    )


def _decision_groups(decisions: list[ReconciliationDecision]) -> list[tuple[str, str]]:
    groups = []
    for decision in decisions:
        group = (decision.area, decision.action)
        if group not in groups:
            groups.append(group)
    return groups


def _filter_decisions(
    decisions: list[ReconciliationDecision],
    area: str,
    action: str,
) -> list[ReconciliationDecision]:
    return [
        decision
        for decision in decisions
        if decision.area == area and decision.action == action
    ]


def _area_title(area: str) -> str:
    return " ".join(part.capitalize() for part in area.split("_"))


def _format_movie(movie: ReconciliationMovie) -> str:
    year = f" ({movie.year})" if movie.year else ""
    identifiers = []
    if movie.tmdb_id is not None:
        identifiers.append(
            f"[TMDB {movie.tmdb_id}](https://www.themoviedb.org/movie/{movie.tmdb_id})"
        )
    if movie.imdb_id:
        identifiers.append(f"IMDb {movie.imdb_id}")
    identifier_text = f" - {', '.join(identifiers)}" if identifiers else ""
    return f"{movie.title}{year}{identifier_text}"


def _format_authorization(decision: ReconciliationDecision) -> str:
    authorization = decision.authorization or "none"
    event_id = decision.authorization_event_id or "none"
    delete_files = str(decision.delete_files).lower()
    return (
        f"authorization={authorization}, "
        f"authorization_event_id={event_id}, "
        f"delete_files={delete_files}"
    )


def _format_datetime(value: datetime | None) -> str:
    return value.isoformat(timespec="seconds") if value is not None else "unknown"
