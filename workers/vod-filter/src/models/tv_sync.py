"""Immutable, validated input types for the backend TV sync export."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime


@dataclass(frozen=True)
class TvAvailability:
    state: str
    region: str
    fetched_at: datetime


@dataclass(frozen=True)
class TvEpisode:
    trakt_episode_id: int
    season_number: int
    episode_number: int
    tvdb_id: int | None
    first_aired: datetime | None
    last_watched_at: datetime | None


@dataclass(frozen=True)
class TvSeason:
    season_number: int
    availability: TvAvailability
    episodes: tuple[TvEpisode, ...]


@dataclass(frozen=True)
class TvShow:
    trakt_id: int
    tvdb_id: int
    title: str
    seasons: tuple[TvSeason, ...]


@dataclass(frozen=True)
class TvSnapshot:
    schema_version: str
    generation_id: str
    published_at: datetime
    generated_at: datetime
    kind: str
    mutation_capable: bool
    shows: tuple[TvShow, ...]
