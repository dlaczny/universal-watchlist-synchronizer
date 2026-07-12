---
type: Data Model
title: Sync Run
description: Backend freshness records plus worker run, report, ownership, and heartbeat state.
tags:
  - data-model
  - sync
  - mongodb
  - sqlite
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Backend State

MongoDB `sync_runs` stores integration status, timestamps, errors, and counts.
`GET /api/sync/status` returns the latest row. The complete movie snapshot uses
the latest `plex_movies_completed` timestamp as
`lastSuccessfulMovieSyncAt`, which is the production freshness reference.

# Worker State

SQLite stores `movie_sync` runs and `managed_destinations` keyed by destination
and TMDB ID. Ownership records distinguish worker-managed rows from unrelated
Radarr or Plex-watchlist content.

Every run writes a JSON/Markdown report pair with source counts, timestamps,
blockers, decisions, reason codes, ownership, and execution status. The worker
also atomically writes `last-run.json`; container health accepts only recent
`completed`, `partial`, or `reconciliation` states.

# Recovery Meaning

An action failure makes a run partial; successful actions and ownership updates
are retained. The next interval collects fresh state and replans instead of
replaying a stale transaction.

# Links

- [Sync Pipeline](../architecture/sync_pipeline.md)
- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
