---
type: API
title: Export Endpoints
description: Cached backend contracts for the movie worker and legacy Radarr/Sonarr consumers.
tags:
  - api
  - worker
  - radarr
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Complete Movie Sync State

`GET /api/export/movies/sync-state` is the production worker contract. It
returns one object:

```text
generatedAt
lastSuccessfulMovieSyncAt
movies[]
  tmdbId, imdbId, title, year, sourceId, metadataStatus,
  availabilityStatus, ownedServiceAvailability,
  radarrEligible, radarrEligibilityReason
```

The array contains every Letterboxd movie, including rows with missing identity
or incomplete metadata so the worker can block unsafe plans rather than mistake
them for removals. `tmdbId` is nullable. `radarrEligible` is true only when the
TMDB identity is valid, metadata is enriched, and no configured owned service
is available. Reason values are `invalid_tmdb_id`, `metadata_not_enriched`,
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

# Links

- [VOD Filter Worker](../systems/vod_filter_worker.md)
- [Production Movie Sync](../architecture/movie_sync_production.md)
