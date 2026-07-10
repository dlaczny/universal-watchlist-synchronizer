---
type: API
title: Backend API
description: HTTP API exposed by the .NET backend for Android clients, sync operations, image proxying, and worker exports.
tags:
  - api
  - backend
  - android
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

The backend API is the only API Android clients should call. It serves cached
MongoDB data and exposes manual sync endpoints for local operation.

# Health

| Endpoint | Purpose |
|---|---|
| `GET /healthz` | Lightweight process health check. Does not require MongoDB, Plex, TMDB, Letterboxd, or seeded data. |

# Watchlist Browse

`GET /api/watchlist`

Query parameters:

| Name | Values | Default |
|---|---|---|
| `collection` | `all`, `movie`, `tv` | `all` |
| `availability` | Comma-separated `plex`, `not_on_plex`, `unreleased`, `unknown_match` | all four states |
| `sort` | `added_desc`, `title_asc` | `added_desc` |

Invalid query values return `400 Bad Request` with an error object.

The response is an array of browse items. When Plex availability is selected,
the response can include Plex-only movies with `source=plex` and
`libraryMembership=plex_only`.

# Watchlist Details

`GET /api/watchlist/{id}`

Returns a detailed watchlist or Plex-only item. Detail responses include browse
fields plus genres, runtime, original language, TMDB vote data, and primary
action fields.

# Image Proxies

| Endpoint | Behavior |
|---|---|
| `GET /api/images/tmdb/{size}/{fileName}` | Proxies TMDB artwork for allowed sizes `w500` and `w1280`. |
| `GET /api/images/plex/{ratingKey}/{kind}` | Proxies Plex poster or backdrop for Plex-only movies. `kind` must be `poster` or `backdrop`. |

Image proxy failures use standard HTTP status codes: `400` for unsupported
request shape, `404` for missing image, `502` for upstream failure, and `503`
when Plex proxy config is missing.

# Sync Status

`GET /api/sync/status`

Returns latest sync status from MongoDB. MongoDB outages return `503`.

# Manual Sync Endpoints

| Endpoint | Purpose |
|---|---|
| `POST /api/sync/letterboxd` | Import configured Letterboxd movie watchlist. |
| `POST /api/sync/tmdb/movies` | Enrich all Letterboxd movie records with TMDB metadata. |
| `POST /api/sync/tmdb/movies/{id}` | Enrich one Letterboxd movie record. |
| `POST /api/sync/tmdb/tv` | Import and enrich TMDB account TV watchlist. |
| `POST /api/sync/plex/movies` | Import Plex movie inventory and update watchlist availability. |
| `POST /api/sync/availability/refresh` | Stale-aware app-open Plex availability refresh. |
| `POST /api/sync/all` | Run Letterboxd, TMDB movies, TMDB TV, and Plex movie sync in order. |

# DTO Fields

Browse item fields:

```text
id, mediaType, source, sourceId, title, year, overview, posterUrl, backdropUrl,
releaseStatus, availabilityStatus, libraryMembership, vodReleaseKnown,
releasedOnVod, vodRegions, ownedServiceAvailability, addedAt, updatedAt
```

Detail-only fields:

```text
genres, runtimeMinutes, originalLanguage, tmdbVoteAverage, tmdbVoteCount,
primaryActionLabel, primaryActionEnabled, primaryActionTarget
```

# Links

- Watchlist data model: [Watchlist Item](../data_models/watchlist_item.md)
- Availability states: [Availability States](../data_models/availability_states.md)
- Android client: [Android TV Client](../systems/android_tv_client.md)

