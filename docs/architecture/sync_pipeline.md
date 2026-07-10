---
type: Architecture
title: Sync Pipeline
description: Repeatable data flow from source watchlists through backend normalization, Plex matching, and worker automation.
tags:
  - sync
  - backend
  - worker
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Backend Pipeline

The backend sync pipeline is repeatable and idempotent.

1. Fetch Letterboxd movie watchlist.
2. Enrich movies with TMDB metadata and watch-provider data.
3. Fetch TMDB account TV watchlist and enrich TV shows.
4. Fetch Plex movie library inventory.
5. Match watchlist movies to Plex movies by IMDb ID, TMDB ID, then unique
   normalized title and year.
6. Persist normalized watchlist records, Plex inventory, and sync run details in
   MongoDB.
7. Serve read-only API responses to Android and export endpoints to workers.

# Combined Sync

`POST /api/sync/all` runs Letterboxd movie sync, TMDB movie enrichment, TMDB TV
watchlist sync, and Plex movie sync in order.

The combined sync may return partial success. If TMDB TV account/session config
is missing, TV sync is skipped instead of failing the entire sync.

# Startup Availability Refresh

Android loads cached watchlist data first, then calls
`POST /api/sync/availability/refresh`. The backend runs Plex movie sync only
when the latest successful Plex movie sync is missing or older than 15 minutes.

# Worker Pipeline

The preferred worker source mode is `WATCHLIST_SOURCE=watchlist_app`.

1. Optionally call `POST /api/sync/all`.
2. Read `GET /api/export/radarr/movies`.
3. Map export rows into the worker movie model.
4. Run cleanup, main sync, and library sync.
5. Write dry-run reports and run history to local worker storage.

# Links

- Backend API: [Backend API](../apis/backend_api.md)
- VOD Filter operations: [VOD Filter Operations](../runbooks/vod_filter_operations.md)
- Migration context: [Migration Context](../references/migration_context.md)

