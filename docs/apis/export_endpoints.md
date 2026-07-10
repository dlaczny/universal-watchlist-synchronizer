---
type: API
title: Export Endpoints
description: Backend endpoints used by external automation workers instead of allowing workers to call source integrations directly.
tags:
  - api
  - worker
  - radarr
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

Export endpoints provide cached backend read-model data to local automation
tools. They do not call Letterboxd, TMDB, Plex, or MongoDB directly from the
worker.

# Radarr Movies

`GET /api/export/radarr/movies`

Returns Radarr/Letterboxd-style JSON for Letterboxd watchlist movies that are
not already available on subscribed VOD services according to cached TMDB
provider data.

Example shape:

```json
[
  {
    "id": 1297842,
    "imdb_id": "tt27613895",
    "title": "GOAT",
    "release_year": "2026",
    "clean_title": "/film/goat-2026/",
    "adult": false
  }
]
```

Filtering rules:

- Include Letterboxd movie watchlist items with no cached subscribed-service
  availability.
- Exclude movies with cached `OwnedServiceAvailability`.
- Do not exclude movies just because TMDB enrichment has not run.
- Plex availability does not filter this endpoint.

# Sonarr TV

`GET /api/export/sonarr/tv`

Returns an empty array in v1. TMDB TV watchlist sync exists, but a
Sonarr-compatible TV export shape is not implemented yet.

# Links

- Worker: [VOD Filter Worker](../systems/vod_filter_worker.md)
- Worker operations: [VOD Filter Operations](../runbooks/vod_filter_operations.md)

