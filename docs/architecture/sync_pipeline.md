---
type: Architecture
title: Sync Pipeline
description: Implemented movie flow from source ingestion through a guarded Radarr and Plex-watchlist plan.
tags:
  - sync
  - backend
  - worker
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Production Movie Flow

1. The worker may call authenticated `POST /api/sync/movies`.
2. The backend imports Letterboxd movies, enriches them through TMDB, then
   refreshes Plex movie inventory and availability.
3. The worker reads `GET /api/export/movies/sync-state`, including every
   Letterboxd movie and source freshness.
4. It independently collects live Radarr movies, Plex watchlist entries, Plex
   library movies, and worker-owned destination rows from SQLite.
5. The pure planner emits `add`, `keep`, `remove`, `skip`, `uncertain`, and
   `error` decisions with stable reason codes.
6. The policy blocks all mutation for stale or failed collection, invalid
   source identity, an unexpected empty source, disabled apply mode, or removal
   limits above the configured count or percentage.
7. The executor applies eligible Radarr decisions before Plex-watchlist
   decisions and records each result independently.
8. JSON and Markdown reports, SQLite run history, ownership state, and the
   worker heartbeat are persisted under the worker data directory.

# Destination Behavior

- Radarr receives backend rows marked `radarrEligible`. A worker-managed movie
  that is no longer eligible is removed only when it has no downloaded file.
  `delete_files` is always false in the production executor.
- Plex watchlist desired state is the union of source movies available on an
  owned service, completed Radarr downloads, and Plex library movies.
- Plex library state is read-only. It contributes availability and desired
  watchlist state but is never mutated.
- Existing desired destination rows are adopted into worker ownership. Existing
  unrelated rows remain unmanaged and are preserved.

# Other Paths

`POST /api/sync/all` and the direct-source worker scripts remain compatibility
or development paths. They are not invoked by the production container. TV and
Sonarr synchronization are outside the active production flow, and Android TV
work remains on hold.

# Links

- [Production Movie Sync](movie_sync_production.md)
- [Backend API](../apis/backend_api.md)
- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
