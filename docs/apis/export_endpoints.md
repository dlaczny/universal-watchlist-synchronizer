---
type: API
title: Export Endpoints
description: Cached backend contracts for the movie worker, read-only TV handoff, and legacy compatibility consumers.
tags:
  - api
  - worker
  - radarr
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
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

# Sonarr Placeholder

`GET /api/export/sonarr/tv` returns an empty array. Sonarr production behavior
is not implemented.

# Read-Only TV Sync State

`GET /api/export/tv/sync-state` resolves exactly one immutable published TV
generation and returns `404` until one exists. The version-1 envelope contains
these exact field names:

```text
{
  schemaVersion, generationId, publishedAt, generatedAt, kind,
  mutationCapable, healthReasons, plexHistory, shows, cleanupAuthorizations
}
```

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
`searchAiredUnwatchedEpisodes`, `cleanupState`, and `episodes`. Every
`episodes[]` item has `traktEpisodeId`, `seasonNumber`, `episodeNumber`,
`tvdbId`, `title`, `firstAired`, `aired`, `watched`, `lastWatchedAt`,
`plexRatingKey`, `watchedByConfiguredPlexAccount`, and `plexLastViewedAt`.
The envelope has a complete show list and this hard safety contract:

```text
mutationCapable: false
healthReasons: plex_history_phase_not_implemented, worker_tv_mutation_disabled
plexHistory: capable=false, bootstrapComplete=false
cleanupAuthorizations: []
```

Shows carry exact Trakt and supporting identities, lifecycle/progress, regular
season episodes, S00 identity-only specials, and PL provider data. Desired
Sonarr/Plex fields are informational compatibility data, not permission for a
worker to perform an action. The existing movie worker neither reads nor
applies this export in Phase 1. `404` means no TV generation has been
published; it is not an empty snapshot and no worker may infer a cleanup from
it.

# Links

- [VOD Filter Worker](../systems/vod_filter_worker.md)
- [Production Movie Sync](../architecture/movie_sync_production.md)
- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
