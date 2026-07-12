---
type: System
title: VOD Filter Worker
description: Python plan-and-apply worker for safe movie synchronization into Radarr and the Plex watchlist.
tags:
  - worker
  - python
  - radarr
  - plex
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Production Role

The worker under `workers/vod-filter/` is the only component that mutates
Radarr or the Plex watchlist. Production requires
`WATCHLIST_SOURCE=watchlist_app`; Letterboxd and TMDB credentials are not needed
inside the production worker because the backend owns source ingestion.

# Entry Points

| File | Role |
|---|---|
| `sync_movies.py` | One collect, plan, policy, optional apply, report, and heartbeat run. |
| `continuous_sync.py` | Runs `sync_movies.py` immediately and then at `SYNC_INTERVAL`. |
| `healthcheck.py` | Accepts a recent `completed`, `partial`, or `reconciliation` heartbeat. |
| `reconcile_sync.py` | Manual read-only report using the same complete snapshot and ownership state. |

Legacy direct-source and multi-script cleanup commands remain in the checkout
for compatibility but are excluded from the production image and are not
called by `continuous_sync.py`.

# Production Modules

| Module | Responsibility |
|---|---|
| `movie_sync_collector.py` | Reads backend snapshot, Radarr, Plex watchlist/library, and SQLite ownership; preserves every collection error. |
| `sync_reconciliation.py` | Pure deterministic destination plan and reason codes. |
| `movie_sync_policy.py` | Freshness, collection, identity, empty-source, apply-mode, and removal-volume gates. |
| `movie_sync_executor.py` | Applies Radarr first, then Plex-watchlist decisions, recording independent failures. |
| `movie_sync_report.py` | Redaction-safe JSON and Markdown reports. |
| `cache_service.py` | SQLite run history and `managed_destinations` ownership. |

# Ownership And Mutation

- `add` records worker ownership after the external action succeeds.
- `keep` adopts a desired pre-existing row or refreshes existing ownership.
- `remove` is possible only for an owned destination row.
- A no-longer-desired Radarr row with a file is skipped for manual review.
- Radarr removal always passes `delete_files=false`.
- Plex library state is read-only; only Plex watchlist add/remove is supported.
- Unmanaged destination rows are reported and preserved.

# Runtime Storage

The production volume is `/app/data`, mapped to
`/opt/watchlist-prod/data/worker` on the host. It contains:

- `vod-filter.db` for run and ownership state;
- `reports/movie-sync-<run-id>.json` and `.md`;
- `last-run.json` for container health.

# Configuration

Core production settings are documented in
[VOD Filter Operations](../runbooks/vod_filter_operations.md). The committed
`worker.env.example` contains placeholders only. Real values stay in
`/opt/watchlist-prod/config/worker.env` with mode `0600`.

# Container Contract

The worker image uses an exact production dependency lock, copies only the
production entry points, and defaults to UID/GID `10001`. Homelab Compose
overrides that identity with the `watchlist` service UID/GID so the private host
bind mount remains writable. The root filesystem stays read-only and only
`/app/data` is persisted.

# Links

- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
- [Export Endpoints](../apis/export_endpoints.md)
- [Production Movie Sync](../architecture/movie_sync_production.md)
