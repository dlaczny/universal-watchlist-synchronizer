---
type: API
title: Export Endpoints
description: Cached backend contracts for the movie worker, schema-v2 TV destination handoff, and legacy compatibility consumers.
tags:
  - api
  - worker
  - radarr
timestamp: 2026-07-11T00:00:00Z
version: 0.4.0
---

# Complete Movie Sync State

`GET /api/export/movies/sync-state` is the production worker contract. It
returns one object:

```text
sourceSnapshotId
generatedAt
lastSuccessfulMovieSyncAt
movies[]
  tmdbId, imdbId, title, year, sourceId, metadataStatus,
  availabilityStatus, ownedServiceAvailability,
  radarrEligible, radarrEligibilityReason
watchedMovies[]
  tmdbId, imdbId, title, year, sourceId, watchedAt,
  lifecycleVersion, lifecycleEventId
```

`movies` contains the complete active Letterboxd set, including rows with
missing identity or incomplete metadata so the worker blocks unsafe plans
rather than mistaking them for removals. `watchedMovies` contains the complete
published watched set. Its stable lifecycle event ID is the cleanup
authorization; `tmdbId` remains nullable for diagnostics but a null value can
never authorize mutation.

`sourceSnapshotId` identifies the immutable manifest used for both arrays. A
worker-triggered refresh must return this same ID before the export is accepted.
Active and watched entries sharing a TMDB ID make the plan invalid.

For active rows, `radarrEligible` is true only when TMDB identity is valid,
metadata is enriched, and no configured owned service is available. Reason
values are `invalid_tmdb_id`, `metadata_not_enriched`,
`owned_service_available`, or `no_owned_service`.

`lastSuccessfulMovieSyncAt` is derived from the latest completed Plex movie
sync and is the worker freshness reference. The endpoint is read-only and does
not trigger source integrations.

# Radarr Compatibility Export

`GET /api/export/radarr/movies` returns Radarr-style rows with `id`, `imdb_id`,
`title`, `release_year`, `clean_title`, and `adult`. It filters out movies with
owned-service availability and rows whose source ID is not numeric.

This endpoint is not a complete desired-state snapshot and must not drive
production removals. It remains for compatibility and source comparison.

# Sonarr Compatibility Placeholder

`GET /api/export/sonarr/tv` returns an empty array. It is a compatibility
surface, not the TV destination contract.

# Schema-V2 TV Sync State

`GET /api/export/tv/sync-state` resolves exactly one immutable published TV
generation and returns `404` until one exists. It is read-only and the worker
must fetch one complete envelope before it collects live destination state. The
schema-version-2 envelope contains these exact field names:

```text
{
  schemaVersion, generationId, publishedAt, generatedAt, kind,
  mutationCapable, destinationSync, healthReasons, plexHistory, shows,
  cleanupAuthorizations
}
```

`destinationSync` has exact fields `capable` and `blockers`.
`capable=true` means the manifest is `valid`, has a positive publication time,
and has no manifest validation failures. When false, `blockers` is the sorted,
de-duplicated set of `tv_generation_not_valid`,
`tv_generation_unpublished`, and/or `tv_generation_validation_failed`.
This capability is intentionally narrower than a write authorization.

Each `shows[]` member uses the worker-specific names below (not the public
`inWatchlist`, `airedEpisodes`, and `completedEpisodes` names):

```text
{
  traktId, tvdbId, tmdbId, imdbId, title, year, identityStatus,
  inTraktWatchlist, lifecycleState, lifecycleVersion, traktStatus,
  aired, completed, lastWatchedEpisode, nextEpisode,
  sonarrDesired, sonarrMonitoredDesired, plexWatchlistDesired,
  seasons, polandAvailability, blockers
}
```

`seasons[]` has `seasonNumber`, `aired`, `completed`, `monitoredDesired`,
`searchAiredUnwatchedEpisodes`, `cleanupState`, `polandAvailability`, and
`episodes`. `polandAvailability` is the season-specific PL observation; show
availability must not be substituted for it. Every
`episodes[]` item has `traktEpisodeId`, `seasonNumber`, `episodeNumber`,
`tvdbId`, `title`, `firstAired`, `aired`, `watched`, `lastWatchedAt`,
`plexRatingKey`, `watchedByConfiguredPlexAccount`, and `plexLastViewedAt`.
The envelope has a complete show list and this hard safety contract:

```text
mutationCapable: false
destinationSync: { capable: true|false, blockers: []|stable generation blockers }
plexHistory: capable=false, bootstrapComplete=false
cleanupAuthorizations: []
```

Shows carry exact Trakt and supporting identities, lifecycle/progress, regular
season episodes, S00 identity-only specials, and PL provider data. Desired
Sonarr/Plex fields express backend desired state but are not an executable
command stream. The separate TV worker derives one selected regular season:
Season 1 when unstarted; otherwise the season of `nextEpisode`, or the next
aired numbered season after the latest fully watched season. Specials never
select a destination season.

TVDB is mandatory for a Sonarr action. A missing, nonpositive, or conflicting
TVDB value is a per-show blocker and title/year fallback is prohibited. The
worker may add, monitor, and search a selected season only when that season is
`confirmed_unavailable` in PL; `unknown` and `stale` block the new Sonarr
action. It may add an exactly identified Plex Watchlist show when the selected
season is `available` or the configured Plex TV library has an exact episode,
and it may remove an exactly identified Plex Watchlist row when neither fact
is true. Plex library media is never altered.

Existing exact Sonarr rows are adoption candidates. An operator-reviewed,
separately armed adoption stores `manual` origin; worker-created rows store
`worker` origin. Neither origin permits a Sonarr file, season, or series
removal. `404` means no TV generation has been published; it is not an empty
snapshot and no worker may infer a cleanup from it.

# Links

- [VOD Filter Worker](../systems/vod_filter_worker.md)
- [Production Movie Sync](../architecture/movie_sync_production.md)
- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
