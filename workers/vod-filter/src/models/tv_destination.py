"""Immutable state used only by the TV destination planning workflow."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal, Mapping

from src.models.tv_sync import TvSnapshot


TvDestination = Literal["sonarr", "plex_watchlist"]
TvAction = Literal[
    "sonarr_add",
    "sonarr_monitor_series",
    "sonarr_monitor_season",
    "sonarr_search_episodes",
    "sonarr_adoption_candidate",
    "plex_add",
    "plex_remove",
    "keep",
    "skip",
    "uncertain",
]


@dataclass(frozen=True)
class TvOwnership:
    destination: TvDestination
    tvdb_id: int
    origin: Literal["worker", "manual"]


@dataclass(frozen=True)
class TvCollectedState:
    snapshot: TvSnapshot | None
    sonarr_series: tuple[object, ...]
    sonarr_episode_ids_by_tvdb: tuple[tuple[int, int, int], ...]
    plex_watchlist: tuple[object, ...]
    plex_library_identities: frozenset[object]
    ownership: tuple[TvOwnership, ...]
    collection_errors: tuple[str, ...]


@dataclass(frozen=True)
class TvDecision:
    action_id: str
    destination: TvDestination
    action: TvAction
    tvdb_id: int | None
    selected_season_number: int | None
    reason: str
    episode_numbers: tuple[int, ...] = ()
    tmdb_id: int | None = None
    imdb_id: str | None = None
    title: str | None = None


@dataclass(frozen=True)
class TvPlan:
    generation_id: str | None
    decisions: tuple[TvDecision, ...]
    selected_season_by_tvdb: "Mapping[int, int]"
    collection_errors: tuple[str, ...]
    applyable: bool

    def decisions_for(self, destination: TvDestination) -> tuple[TvDecision, ...]:
        return tuple(item for item in self.decisions if item.destination == destination)
