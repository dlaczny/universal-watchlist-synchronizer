---
type: Runbook
title: VOD Filter Operations
description: Configure, reconcile, apply, inspect, and recover the production movie worker.
tags:
  - worker
  - reconciliation
  - radarr
  - plex
timestamp: 2026-07-12T00:00:00Z
version: 0.4.0
---

# Production Configuration

The deployed worker uses these settings. Values containing credentials belong
only in ignored or host-local environment files.

| Variable | Purpose | Default |
|---|---|---|
| `WATCHLIST_SOURCE` | Must be `watchlist_app` for `sync_movies.py`. | `letterboxd` |
| `WATCHLIST_APP_URL` | Backend base URL. | required |
| `WATCHLIST_APP_SYNC_KEY` | Header value for backend sync mutation. | required in backend mode |
| `WATCHLIST_APP_SYNC_FIRST` | Trigger `POST /api/sync/movies` before snapshot read. | `false` |
| `WATCHLIST_APP_TIMEOUT_SECONDS` | Timeout for ordinary backend snapshot reads. | `30` |
| `WATCHLIST_APP_SYNC_TIMEOUT_SECONDS` | Timeout for the full backend movie refresh only. | `900` |
| `MOVIE_SYNC_APPLY` | Permit policy-approved destination actions. | `false` |
| `MOVIE_SYNC_MAX_SOURCE_AGE_MINUTES` | Maximum source freshness age. | `120` |
| `MOVIE_SYNC_MAX_REMOVAL_COUNT` | Maximum removals in one plan. | `10` |
| `MOVIE_SYNC_MAX_REMOVAL_PERCENT` | Maximum removals as percentage of live Radarr and Plex-watchlist rows. | `25` |
| `MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION` | Permit exact published watched Radarr removals to delete files. | `false` |
| `RADARR_URL`, `RADARR_API_KEY` | Live Radarr boundary. | required |
| `RADARR_QUALITY_PROFILE_ID`, `RADARR_ROOT_FOLDER` | Radarr add settings. | `1`, `/movies/` |
| `PLEX_URL`, `PLEX_TOKEN` | Live Plex boundary. | required |
| `PLEX_LIBRARY_NAME` | Plex movie library read by the collector. | `Movies` |
| `SYNC_INTERVAL` | Continuous loop delay in seconds. | `3600` |
| `DATABASE_PATH` | SQLite ownership and run history. | `./data/vod-filter.db` |
| `WORKER_HEARTBEAT_PATH` | Container health heartbeat. | `/app/data/last-run.json` |
| `WORKER_HEALTH_MAX_AGE_SECONDS` | Maximum accepted heartbeat age. | `7500` |

# Reconciliation Run

Run from `workers/vod-filter` with apply disabled:

```powershell
$env:MOVIE_SYNC_APPLY="false"
python sync_movies.py --skip-backend-sync
```

Omit `--skip-backend-sync` when the configured
`WATCHLIST_APP_SYNC_FIRST=true` should refresh the backend first. A
reconciliation-only run writes reports and ownership-neutral plans but does not
call mutating Radarr or Plex methods.

The full refresh can make hundreds of upstream metadata requests. Keep its
timeout separate from ordinary API reads so a slow refresh can finish without
hiding a stalled snapshot endpoint.

# Apply Run

Use one explicit supervised apply after reviewing the latest report:

```powershell
python sync_movies.py --apply
```

For unattended operation, set `MOVIE_SYNC_APPLY=true` in the host worker env
and restart the worker container. The same policy gates still apply.

`MOVIE_SYNC_APPLY` alone never enables watched file deletion. After a migration
reconciliation establishes the Radarr baseline and the report is reviewed, set
the host-only `MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION=true` to arm that one
authorization path. Keep the host env mode `0600`; never add the value or any
credential to GitHub.

# Reports And Exit Codes

Each run writes `data/reports/movie-sync-<run-id>.json` and `.md`.

| Exit | Meaning |
|---|---|
| `0` | Reconciliation or apply completed without effective blockers/errors. |
| `1` | Configuration, collection, or unhandled run failure. |
| `2` | One or more external actions failed; successful actions remain recorded. |
| `3` | Apply was requested but policy blockers prevented all mutation. |

Stable blocker codes are:

```text
mutation_disabled
source_freshness_unknown
source_snapshot_stale
collection_errors
invalid_source_identity
invalid_file_deletion_authorization
watched_file_deletion_disabled
unexpected_empty_source
removal_count_exceeded
removal_percentage_exceeded
```

Review decisions by `area`, `action`, `reason`, `managed`, and
`execution_status`. A first reconciliation normally includes
`mutation_disabled`; that blocker is expected while apply is off.

These destination reasons require explicit attention:

| Reason | Meaning |
|---|---|
| `desired_radarr_movie_missing_override_exclusion` | Apply will remove the exact TMDB import-list exclusion, then add the desired movie. |
| `radarr_title_year_collision_requires_manual_review` | Another TMDB identity already maps to the same Radarr title/year folder key; no add occurs. |
| `radarr_movie_without_source_authorization_preserved` | The complete active/watched source has no authority for this Radarr ID; stale ownership cannot remove it. |
| `plex_discovery_identity_not_found` | Plex Discover could not resolve the exact TMDB identity; no mutation or ownership record occurs. |
| `watched_letterboxd_movie_remove_from_radarr` | Exact published watched event will remove the Radarr row with files; requires both apply and watched-file gates. |
| `watched_letterboxd_movie_remove_from_plex_watchlist` | Exact published watched event will remove only the Plex-watchlist row. |
| `watched_letterboxd_movie_absent_from_radarr` | Watched Radarr target is already converged; no call occurs. |
| `watched_letterboxd_movie_absent_from_plex_watchlist` | Watched Plex target is already converged; no call occurs. |
| `manually_removed_radarr_movie_remove_from_plex_watchlist` | A post-baseline manual Radarr disappearance authorizes only exact Plex-watchlist cleanup. |
| `manually_removed_radarr_movie_absent_from_plex_watchlist` | The manual-removal Plex target is already converged; no call occurs. |
| `watched_movie_missing_tmdb_identity` | Watched history is visible but cannot authorize any destination mutation. |
| `watched_movie_missing_lifecycle_event_id` | Watched identity lacks its published event key; mutation is blocked. |
| `active_watched_tmdb_identity_conflict` | One TMDB ID appears in both active and watched sets; all mutation is blocked. |

Watched decisions use `authorization=letterboxd_watched` and the lifecycle
event ID. Manual Plex cleanup uses `authorization=manual_radarr_removal` with no
event ID. No other authorization value is executable.

# Review Checklist

Before enabling apply, verify:

- backend, Radarr, Plex watchlist/library, and ownership collections have no
  errors;
- source freshness is within policy;
- every source movie has a valid TMDB identity and enriched metadata;
- `sourceSnapshotId` is non-empty and matches the ID returned by the refresh;
- active and watched TMDB sets do not conflict;
- Radarr adds are truly unavailable on configured owned services;
- each planned Radarr exclusion override is intended;
- title/year collision skips identify the correct TMDB row to retain manually;
- Plex-watchlist adds match source/library/download intent;
- unmanaged destination rows are `skip`, not `remove`;
- downloaded Radarr rows are `skip` with manual-review reason;
- ordinary planned removals are owned and within both limits;
- every watched Radarr removal has `authorization=letterboxd_watched`, a
  non-empty event ID, and `delete_files=true`;
- no other decision has `delete_files=true`;
- manual Radarr disappearance creates no Radarr decision and only an exact
  Plex-watchlist remove or converged skip.

# Observation Baseline

The first successful complete Radarr collection after deployment creates
`radarr_observations` with `present=true` and does not classify anything as
manual. A failed Radarr collection must leave the ledger unchanged. Review the
first report with watched file deletion disabled and confirm:

- backend and Radarr collections succeeded;
- `manual_radarr_disappearances` is zero;
- active/watched conflicts are zero;
- no unexpected file-delete decision exists.

Later, a previously present exact TMDB ID that disappears while neither active
nor watched becomes `manual`. Worker-initiated watched removal is recorded as
`watched` immediately and is not reclassified as manual.

# Continuous And Health

```powershell
python continuous_sync.py --continuous --interval 3600
python healthcheck.py
```

The loop retries from a fresh snapshot after any nonzero run. Health accepts a
recent `completed`, `partial`, or `reconciliation` heartbeat; a blocked or
failed run is unhealthy until a later accepted run.

# Hard Safety Guarantees

- Ordinary Radarr cleanup always uses `delete_files=false`; downloaded ordinary
  rows require manual review.
- Only an exact published watched Radarr removal can use `delete_files=true`,
  and only while both mutation and watched-file gates are enabled.
- Plex library media is never mutated.
- Ordinary Plex cleanup requires ownership. Published watched and durable
  manual-Radarr-removal authorizations may remove only their exact TMDB
  Plex-watchlist row.
- A boundary collection failure prevents all mutation.

# Links

- [VOD Filter Worker](../systems/vod_filter_worker.md)
- [Production Movie Sync](../architecture/movie_sync_production.md)
- [Homelab CD](homelab_cd.md)
