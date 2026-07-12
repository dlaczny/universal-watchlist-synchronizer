---
type: Architecture
title: Production Movie Sync
description: Plan-and-apply architecture for safe, explainable movie synchronization and homelab deployment.
tags:
  - movies
  - sync
  - worker
  - deployment
  - ci-cd
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Purpose

Make movie synchronization safe to run unattended across Letterboxd, TMDB,
MongoDB, Radarr, and Plex. Android TV and TV-show/Sonarr work are outside this
design.

The production workflow uses one plan-and-apply engine. It first collects a
complete snapshot, validates that snapshot, writes an explicit plan, and only
then applies permitted actions. The worker must never infer a complete desired
watchlist from the Radarr-filtered export.

# Sources Of Truth

| Concern | Source of truth |
|---|---|
| Desired movie watchlist | Letterboxd, imported through the backend |
| Movie identity and provider metadata | TMDB data cached by the backend |
| Normalized desired state | MongoDB through the backend movie sync export |
| Existing downloads and Radarr monitoring | Live Radarr state |
| Existing media and universal watchlist | Live Plex state |
| Worker-owned destination records | Worker SQLite state |

The production worker uses only the backend as its watchlist source. Direct
Letterboxd/TMDB worker access is not part of the deployed path.

# Backend Contract

The backend adds a movie-only sync operation that runs, in order:

1. Letterboxd movie import.
2. TMDB movie enrichment.
3. Plex movie inventory and availability matching.

It does not run TMDB TV synchronization. The endpoint returns a combined result
whose overall status is `completed` or `partial`. A source or inventory failure
returns an error and prevents the worker from collecting a fresh snapshot.

The backend also adds one worker-focused movie snapshot endpoint. One response
contains:

- generation time and last successful movie sync time;
- TMDB ID, IMDb ID, title, year, source ID, and metadata status;
- owned-service availability and Plex availability;
- whether the movie is an eligible Radarr candidate;
- enough status detail to explain why an item is ineligible or uncertain.

Radarr eligibility requires successfully enriched metadata and no configured
owned streaming service. Missing or failed enrichment is `uncertain`; it is not
treated as permission to add a movie to Radarr.

Production sync endpoints require `X-Watchlist-Sync-Key`. Read-only browse,
export, status, and health endpoints remain available on the trusted LAN. The
sync key is configured only in server-local backend and worker environment
files.

# Worker Components

The production worker is split into focused units:

| Unit | Responsibility |
|---|---|
| Snapshot collector | Optionally triggers movie sync, then reads backend, Radarr, Plex watchlist/library, and SQLite ownership state. |
| Snapshot validator | Rejects malformed rows, missing or duplicate TMDB IDs, stale backend state, collection failures, and suspicious empty snapshots. |
| Planner | Produces deterministic destination decisions without side effects. |
| Policy gate | Blocks unsafe plans and limits which removal actions can run automatically. |
| Executor | Applies permitted Radarr and Plex actions idempotently. |
| Recorder | Writes JSON and Markdown reports, action outcomes, ownership state, run history, and a health heartbeat. |

`sync_movies.py` is the single production command. The container loop invokes
that command in one Compose-managed process. Legacy overlapping cleanup,
main-sync, and library-sync entry points are excluded from the production image
after their required behavior is covered by the new engine.

# Decision Model

Every destination decision uses one of these actions:

- `add`: desired and absent;
- `keep`: desired and present;
- `remove`: no longer desired, present, worker-managed, and policy-permitted;
- `skip`: intentionally unchanged;
- `uncertain`: identity or source data is insufficient for mutation;
- `error`: collection or execution failed.

Each decision contains destination, TMDB ID, title/year, reason code,
worker-ownership state, and eventual execution result. Reports use the same
vocabulary as runtime logs and tests.

# Destination Rules

## Radarr

- Add an eligible backend candidate that is absent from Radarr.
- Keep an eligible candidate already in Radarr.
- Adopt an existing Radarr movie as worker-managed only while it is desired by
  the backend.
- Remove only a worker-managed movie that is no longer a Radarr candidate.
- If a removal target has a downloaded file, skip it and require manual review.
- Never delete movie files automatically.
- Never remove unrelated Radarr movies that were not adopted or added by this
  worker.

## Plex Watchlist

A movie is desired on the Plex watchlist when at least one condition is true:

- it is a source movie available through a configured owned service;
- it is present in the Plex movie library;
- it is a completed Radarr download.

The worker adds or keeps desired movies and records ownership. It removes only
worker-managed entries that are no longer desired. Pre-existing unrelated Plex
watchlist entries are preserved.

## Plex Library

The Plex library is read-only to automatic sync. The worker never deletes Plex
library media. Library rows contribute desired Plex-watchlist state and movie
availability only.

# Safety Gates

No destination mutation occurs when any of these conditions is true:

- backend movie sync or snapshot collection failed;
- the snapshot is older than the configured maximum age;
- the source unexpectedly becomes empty while managed movies still exist;
- a required row has no valid TMDB ID;
- duplicate TMDB IDs make identity ambiguous;
- live Radarr or Plex state could not be collected;
- planned removals exceed the configured count or percentage threshold;
- the plan contains unresolved `uncertain` identity findings.

The first deployed run is reconciliation-only. Production mutation is enabled
only after its report is reviewed. Adds may then run unattended. Removals remain
subject to ownership, file-preservation, and volume thresholds.

# Failure And Recovery

Planning is side-effect free. During apply, each action is independent and
idempotent. A successful action is recorded immediately; a later failure leaves
the run `partial` and the next run recomputes state rather than replaying a stale
plan. External side effects are not transactionally rolled back.

The continuous process survives integration failures, waits for the configured
interval, and retries with a fresh snapshot. Its health check requires a recent
completed, partial, or reconciliation heartbeat. Logs and
reports redact credentials and never include request authorization headers.

# CI

GitHub Actions uses one `Movie CI` workflow for movie production paths. It runs:

1. OKF validation and deployment-tool tests.
2. Backend restore, Release build, API/application tests, and Mongo repository
   tests against a MongoDB service container.
3. Worker dependency installation and the complete worker test suite.
4. Secret scanning of the working tree and Git history with findings redacted.
5. Backend and worker Docker image builds.

Actions have read-only repository permissions, are pinned to immutable
revisions, and receive no production credentials. Android CI remains isolated
and is not a movie deployment prerequisite.

# Homelab Delivery

The public GitHub repository remains secret-free. The host keeps runtime
credentials in mode-`0600` files outside the Git checkout.

The deployment host uses a clean production checkout separate from the existing
dirty `/opt/watchlist-app` checkout. The existing checkout is preserved. A
systemd service running as `watchlist` performs this sequence under a deployment
lock:

1. Resolve the current `main` commit without executing it.
2. Query the public GitHub Actions API and require successful `Movie CI` for
   that exact commit.
3. Fetch and check out the validated commit in the production checkout.
4. Validate Compose and build commit-tagged backend and worker images.
5. Start the backend and let Compose start the worker after backend health.
6. Require backend HTTP health and a recent accepted worker heartbeat/report.
7. Record the successful release commit.
8. On failure, restart the previously successful image set.
9. Prune stale build cache and old release images while retaining rollback
   images.

The host-side timer polls every five minutes. It needs no GitHub token because
the repository and workflow results are public. Backend, worker, MongoDB,
Radarr, Plex, and TMDB credentials never enter GitHub Actions.

# Rollout

1. Correct the backend movie-only sync and snapshot contracts.
2. Replace the worker production path with the plan-and-apply engine.
3. Add CI, secret scanning, container health, and deployment validation.
4. Create the clean host checkout and migrate existing environment files
   without printing their values.
5. Deploy in reconciliation-only mode and review all decisions.
6. Enable safe mutations, run one supervised sync, and verify backend status,
   reports, Radarr, Plex, and worker heartbeat.
7. Leave Android TV and TV/Sonarr behavior unchanged.

# Acceptance Criteria

- A movie-only backend sync and snapshot can be invoked with authentication.
- A dry run explains every Radarr and Plex-watchlist decision.
- Failed or ambiguous collection produces no mutation.
- Automatic runs cannot delete downloaded files or Plex library media.
- Unrelated Radarr and Plex watchlist entries are preserved.
- Re-running the same state produces only `keep` or `skip` decisions.
- Backend, worker, docs, secret scan, and Docker builds pass in GitHub Actions.
- The homelab deploys only a commit with successful `Movie CI`.
- Failed deployment returns to the previous healthy release.
- Runtime secrets exist only in protected host-local files.

# Links

- [Sync Pipeline](sync_pipeline.md)
- [System Boundaries](system_boundaries.md)
- [VOD Filter Worker](../systems/vod_filter_worker.md)
- [Homelab CD](../runbooks/homelab_cd.md)
- [Sync Correctness Priority](../decisions/sync_correctness_priority.md)
