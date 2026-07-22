---
type: System
title: VOD Filter Worker
description: Python plan-and-apply worker for safe movie synchronization into Radarr and the Plex watchlist.
tags:
  - worker
  - python
  - radarr
  - plex
timestamp: 2026-07-12T00:00:00Z
version: 0.4.0
---

# Production Role

The worker under `workers/vod-filter/` is the only component that mutates
Radarr or the Plex watchlist. Production requires
`WATCHLIST_SOURCE=watchlist_app`; Letterboxd and TMDB credentials are not needed
inside the production worker because the backend owns source ingestion.

Phase 1 TV data is not consumed by this worker. The backend's TV export is
read-only and declares `mutationCapable=false`; the worker has no TV collector,
planner, executor, ownership, Sonarr, Plex-history, or Plex-watchlist TV path.

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
| `movie_sync_collector.py` | Reads the coherent backend lifecycle snapshot, Radarr movies/exclusions, Plex state, ownership, and observations; preserves every collection error. |
| `sync_reconciliation.py` | Pure deterministic destination plan and reason codes. |
| `movie_sync_policy.py` | Freshness, collection, identity, empty-source, apply, removal-volume, and watched-file gates. |
| `movie_sync_executor.py` | Revalidates destructive authorization, applies Radarr then Plex-watchlist decisions, and records independent outcomes. |
| `movie_sync_report.py` | Redaction-safe JSON and Markdown reports. |
| `cache_service.py` | SQLite ownership, run history, Radarr observations, and cleanup audit history. |

# Ownership And Mutation

- `add` records worker ownership after the external action succeeds.
- `keep` adopts a desired pre-existing row or refreshes existing ownership.
- Compatibility ownership-only `remove` remains available outside the complete
  snapshot path; production lifecycle planning requires source authorization.
- Ordinary Radarr remove also requires the exact ID to remain active in the
  complete backend source. Stale ownership alone is never deletion authority.
- A no-longer-desired Radarr row with a file is skipped for manual review.
- Ordinary Radarr removal always passes `delete_files=false`.
- A published watched exact-TMDB Radarr removal carries
  `letterboxd_watched`, its lifecycle event ID, and `delete_files=true`; it may
  remove a downloaded row that predates worker ownership only when the separate
  watched-file gate is enabled.
- The same watched event may remove the exact Plex-watchlist row regardless of
  ownership or Plex-library membership.
- A `manual` absent Radarr observation may remove only the exact Plex-watchlist
  row. It never creates a Radarr or Plex-library action.
- Stale Plex ownership without a watched/manual authorization produces
  `plex_watchlist_movie_without_cleanup_authorization_preserved`.
- A Radarr exclusion is removed only for an exact TMDB add whose plan contains
  `desired_radarr_movie_missing_override_exclusion`.
- A different TMDB identity with the same Radarr title/year is skipped for
  manual review instead of risking a folder collision.
- Plex library state is read-only; only Plex watchlist add/remove is supported.
- Plex discovery requires exact TMDB identity; a catalog miss is reported as a
  skip and does not create ownership.
- Unmanaged destination rows are reported and preserved unless an exact
  watched/manual-removal authorization explicitly overrides ownership.

# Runtime Storage

The production volume is `/app/data`, mapped to
`/opt/watchlist-prod/data/worker` on the host. It contains:

- `vod-filter.db` for ownership, runs, Radarr observations, and cleanup audit;
- `reports/movie-sync-<run-id>.json` and `.md`;
- `last-run.json` for container health.

# Configuration

Core production settings are documented in
[VOD Filter Operations](../runbooks/vod_filter_operations.md). The committed
`worker.env.example` contains placeholders only. Real values stay in
`/opt/watchlist-prod/config/worker.env` with mode `0600`.

`MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION` defaults to `false` and is independent
of `MOVIE_SYNC_APPLY`. Reports show authorization and `delete_files`, never
credentials or this host control value.

The committed worker example and production Compose hard-lock
`TRAKT_HISTORY_SYNC_APPLY`, `TV_SYNC_APPLY`,
`TV_SYNC_ADOPT_EXISTING_DESTINATIONS`, `TV_SYNC_ALLOW_SEASON_FILE_DELETION`,
`TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION`, and
`TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE` to `false`. These are not an invitation
to configure a TV action in Phase 1.

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
- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
