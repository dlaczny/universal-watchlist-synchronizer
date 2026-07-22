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
version: 0.3.1
---

# Backend State

MongoDB `sync_runs` stores integration status, timestamps, errors, and counts.
`GET /api/sync/status` returns the latest row. The complete movie snapshot uses
the latest `plex_movies_completed` timestamp as
`lastSuccessfulMovieSyncAt`, which is the production freshness reference.

MongoDB `letterboxd_source_snapshots` stores immutable publish-last manifests.
Each row has a snapshot ID, publish time, complete active source IDs, current
watched event references, and item count. `sync_runs` is operational history;
the latest manifest is lifecycle authority. During migration, reads with no
manifest treat existing Letterboxd documents as active. The first lifecycle
writer publishes a `letterboxd-bootstrap-*` manifest for that legacy active set
before changing documents, with an empty watched set; later operational
manifests remain publish-last.

TV uses separate immutable `tv_sync_manifests`, `tv_shows`, and
`tv_lifecycle_events`. A candidate is staged, validated, and published last;
the published pointer is the only browse/export authority. A Trakt source,
pagination, schedule, identity, or cursor-race failure leaves the old pointer
unchanged. Provider failures may still publish a complete generation with
`unknown` or `stale` provider observations.

# Worker State

SQLite stores `movie_sync` runs and `managed_destinations` keyed by destination
and TMDB ID. Ownership records distinguish worker-managed rows from unrelated
Radarr or Plex-watchlist content.

SQLite also stores:

| Table | Purpose |
|---|---|
| `radarr_observation_state` | Marks whether the first successful full Radarr baseline exists. |
| `radarr_observations` | Exact TMDB presence plus `manual`, `active_source`, or `watched` disappearance state. |
| `movie_cleanup_history` | Credential-free watched/manual cleanup attempts, status, event ID, destination, and `delete_files`. |

Every run writes a JSON/Markdown report pair with source snapshot ID, source
counts, timestamps, blockers, decisions, reason codes, ownership,
authorization, event ID, `delete_files`, and execution status. The worker
also atomically writes `last-run.json`; container health accepts only recent
`completed`, `partial`, or `reconciliation` states.

# Recovery Meaning

An action failure makes a run partial; successful actions and ownership updates
are retained. The next interval collects fresh state and replans instead of
replaying a stale transaction.

A failed Radarr collection does not alter observations. A successful watched
Radarr removal immediately marks its observation absent with the authorizing
event ID, preventing later misclassification as a manual removal.

# Links

- [Sync Pipeline](../architecture/sync_pipeline.md)
- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
