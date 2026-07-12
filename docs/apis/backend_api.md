---
type: API
title: Backend API
description: HTTP contracts for health, browsing, source synchronization, images, status, and worker exports.
tags:
  - api
  - backend
  - worker
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Boundary

The backend serves cached MongoDB data to clients and complete desired movie
state to the production worker. Read-only endpoints require no API key on the
trusted LAN. Every `POST /api/sync/*` endpoint is protected by
`X-Watchlist-Sync-Key` when `Sync:ApiKey` is configured; Production startup
fails when that key is missing.

# Health And Browse

| Endpoint | Contract |
|---|---|
| `GET /healthz` | Lightweight process health; does not prove integration or MongoDB health. |
| `GET /api/watchlist` | Cached browse rows filtered by `collection`, `availability`, and `sort`. |
| `GET /api/watchlist/{id}` | Cached detail row or `404`. |
| `GET /api/sync/status` | Latest persisted backend sync status or `404`; MongoDB outage returns `503`. |

`GET /api/watchlist` accepts:

| Query | Values | Default |
|---|---|---|
| `collection` | `all`, `movie`, `tv` | `all` |
| `availability` | comma-separated `plex`, `not_on_plex`, `unreleased`, `unknown_match` | all |
| `sort` | `added_desc`, `title_asc` | `added_desc` |

Invalid query values return `400`. Selecting Plex availability may include
Plex-only movies with `source=plex` and `libraryMembership=plex_only`.

# Movie Sync

`POST /api/sync/movies` is the production source-refresh operation. It runs:

1. Letterboxd movie import.
2. TMDB movie enrichment.
3. Plex movie inventory and availability sync.

It does not run TV sync. The response includes `startedAt`, `finishedAt`, each
stage result, and overall `status=completed|partial`. TMDB not-found or failed
items make the result partial.

# Other Sync Operations

| Endpoint | Purpose |
|---|---|
| `POST /api/sync/letterboxd` | Import Letterboxd movies. |
| `POST /api/sync/tmdb/movies` | Enrich all imported movies. |
| `POST /api/sync/tmdb/movies/{id}` | Enrich one movie or return `404`. |
| `POST /api/sync/plex/movies` | Refresh Plex movie inventory and matching. |
| `POST /api/sync/availability/refresh` | Run stale-aware Plex availability refresh. |
| `POST /api/sync/tmdb/tv` | Compatibility TV watchlist sync. |
| `POST /api/sync/all` | Compatibility combined movie and TV sequence. |

# Image Proxies

| Endpoint | Contract |
|---|---|
| `GET /api/images/tmdb/{size}/{fileName}` | Proxies `w500` or `w1280` TMDB artwork. |
| `GET /api/images/plex/{ratingKey}/{kind}` | Proxies `poster` or `backdrop` for a stored Plex movie. |

Image errors use `400` for invalid shape, `404` for missing content, `502` for
upstream failure, and `503` when required Plex proxy configuration is missing.

# Exports

Worker contracts are documented separately in [Export Endpoints](export_endpoints.md).

# Links

- [Backend Service](../systems/backend_service.md)
- [Watchlist Item](../data_models/watchlist_item.md)
- [Availability States](../data_models/availability_states.md)
