"""Deterministic, side-effect-free planner for TV destination reconciliation."""

from __future__ import annotations

from src.clients.plex_tv_client import PlexTvShow
from src.clients.sonarr_tv_client import SonarrSeries
from src.models.tv_destination import TvCollectedState, TvDecision, TvPlan
from src.models.tv_sync import TvSeason, TvShow


def build_tv_plan(collected: TvCollectedState) -> TvPlan:
    """Plan exact destination actions; collection errors always make the plan non-applyable."""
    if collected.snapshot is None:
        return TvPlan(None, (), {}, collected.collection_errors, False)

    decisions: list[TvDecision] = []
    selected: dict[int, int] = {}
    sonarr_by_tvdb = _by_tvdb(collected.sonarr_series)
    plex_by_tvdb = _by_tvdb(collected.plex_watchlist)
    library_tvdb_ids = {
        identity.tvdb_id
        for identity in collected.plex_library_identities
        if hasattr(identity, "tvdb_id")
    }
    ownership = {(item.destination, item.tvdb_id): item for item in collected.ownership}

    for show in sorted(collected.snapshot.shows, key=lambda item: item.tvdb_id):
        season = _selected_season(show)
        if season is None:
            decisions.append(_decision(collected.snapshot.generation_id, "sonarr", show.tvdb_id, None, "uncertain", "selected_season_unavailable"))
            continue
        selected[show.tvdb_id] = season.season_number
        existing = sonarr_by_tvdb.get(show.tvdb_id)
        owner = ownership.get(("sonarr", show.tvdb_id))
        if existing is not None and owner is None:
            decisions.append(_decision(collected.snapshot.generation_id, "sonarr", show.tvdb_id, season.season_number, "sonarr_adoption_candidate", "existing_sonarr_series_not_owned"))
        elif season.availability.state in {"unknown", "stale"}:
            decisions.append(_decision(collected.snapshot.generation_id, "sonarr", show.tvdb_id, season.season_number, "uncertain", f"sonarr_provider_availability_{season.availability.state}"))
        elif season.availability.state == "confirmed_unavailable":
            if existing is None:
                decisions.extend((
                    _decision(collected.snapshot.generation_id, "sonarr", show.tvdb_id, season.season_number, "sonarr_add", "selected_season_confirmed_unavailable"),
                    _decision(collected.snapshot.generation_id, "sonarr", show.tvdb_id, season.season_number, "sonarr_monitor_season", "selected_season_confirmed_unavailable"),
                ))
            else:
                if not existing.monitored:
                    decisions.append(_decision(collected.snapshot.generation_id, "sonarr", show.tvdb_id, season.season_number, "sonarr_monitor_series", "selected_season_confirmed_unavailable"))
                if not existing.seasons.get(season.season_number, False):
                    decisions.append(_decision(collected.snapshot.generation_id, "sonarr", show.tvdb_id, season.season_number, "sonarr_monitor_season", "selected_season_confirmed_unavailable"))

        plex_desired = season.availability.state == "available" or show.tvdb_id in library_tvdb_ids
        plex_existing = plex_by_tvdb.get(show.tvdb_id)
        if plex_desired and plex_existing is None:
            decisions.append(_decision(collected.snapshot.generation_id, "plex_watchlist", show.tvdb_id, season.season_number, "plex_add", "selected_season_available_or_in_plex_library"))
        elif plex_desired:
            decisions.append(_decision(collected.snapshot.generation_id, "plex_watchlist", show.tvdb_id, season.season_number, "keep", "plex_watchlist_desired"))
        elif plex_existing is not None:
            decisions.append(_decision(collected.snapshot.generation_id, "plex_watchlist", show.tvdb_id, season.season_number, "plex_remove", "selected_season_not_available_and_not_in_plex_library"))

    decisions.sort(key=lambda item: (item.tvdb_id, item.destination, item.selected_season_number or 0, item.action))
    return TvPlan(
        generation_id=collected.snapshot.generation_id,
        decisions=tuple(decisions),
        selected_season_by_tvdb=selected,
        collection_errors=collected.collection_errors,
        applyable=not collected.collection_errors,
    )


def _selected_season(show: TvShow) -> TvSeason | None:
    regular = sorted(show.seasons, key=lambda item: item.season_number)
    if not regular:
        return None
    if not any(episode.last_watched_at is not None for season in regular for episode in season.episodes):
        return next((season for season in regular if season.season_number == 1), None)
    if show.next_episode_season is not None:
        return next((season for season in regular if season.season_number == show.next_episode_season), None)
    completed = [season.season_number for season in regular if season.episodes and all(episode.last_watched_at is not None for episode in season.episodes)]
    return next((season for season in regular if season.season_number > max(completed, default=0)), None)


def _by_tvdb(rows: tuple[object, ...]) -> dict[int, object]:
    result: dict[int, object] = {}
    for row in rows:
        tvdb_id = getattr(row, "tvdb_id", None)
        if tvdb_id is None:
            tvdb_id = getattr(getattr(row, "identity", None), "tvdb_id", None)
        if isinstance(tvdb_id, int) and not isinstance(tvdb_id, bool) and tvdb_id > 0:
            result[tvdb_id] = row
    return result


def _decision(generation_id: str, destination: str, tvdb_id: int, season_number: int | None, action: str, reason: str) -> TvDecision:
    season_part = season_number or 0
    return TvDecision(
        action_id=f"{generation_id}:{destination}:{tvdb_id}:{season_part}:{action}",
        destination=destination,  # type: ignore[arg-type]
        action=action,  # type: ignore[arg-type]
        tvdb_id=tvdb_id,
        selected_season_number=season_number,
        reason=reason,
    )
