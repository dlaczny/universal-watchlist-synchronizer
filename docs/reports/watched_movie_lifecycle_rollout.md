---
type: Report
title: Watched Movie Lifecycle Rollout
description: Verified release, baseline, gate enablement, convergence, and live-exercise evidence for watched movie cleanup.
tags:
  - report
  - movies
  - lifecycle
  - deployment
timestamp: 2026-07-13T05:54:04Z
version: 0.1.0
---

# Result

Watched movie lifecycle handling is implemented, CI-validated, deployed, and
armed in production. No real watched transition existed during rollout, so the
destructive path remains test-covered but has not been manufactured or
live-exercised against a real file.

# Release Evidence

| Check | Observed result |
|---|---|
| Implementation release | `63fbf5804f9b6c3d7d387f5e7f70ebcd2d63b41f` |
| GitHub gate | [Movie CI run 29226820025](https://github.com/dlaczny/universal-watchlist-synchronizer/actions/runs/29226820025) completed successfully for the exact SHA. |
| Deployment completed | 2026-07-13 05:48 UTC through `watchlist-deploy.timer`. |
| Rollback release | `91d5d474785c1465dbdb8d1fee0958a3606fd616` |
| Runtime images | Backend and worker both use the full implementation SHA tag. |
| Runtime health | API and worker healthy; API health returned `200`; unauthenticated movie sync returned `401`. |
| Repository state | Detached production checkout at the exact SHA with zero status entries. |
| Continuous delivery | Deploy service result `success`; five-minute timer active and enabled. |

Local release validation passed 167 Application tests, 39 API tests, 133 worker
tests, 12 deployment tests, OKF validation, Python compilation, Compose
validation, and both production image builds. Gitleaks `v8.30.1` found no leaks
across 170 commits or the clean exact publishable tree. Runtime credentials
remained host-local; host environment files retained mode `0600`.

# Gated Baseline

Report `movie-sync-29.json` ran with watched file deletion disabled and
published source snapshot
`letterboxd-202607130544453666458-0ece34a44de7438ba7e68a4d42847fc6`.

| Observation | Count |
|---|---:|
| Active backend movies | 289 |
| Watched authorizations | 0 |
| Radarr-eligible backend movies | 240 |
| Radarr movies observed | 317 |
| Plex library movies | 268 |
| Plex watchlist movies | 331 |
| Collection errors | 0 |
| Manual Radarr disappearances | 0 |
| Removal decisions | 0 |
| `delete_files=true` decisions | 0 |
| Cleanup history rows | 0 |

The first successful observation created one baseline generation with all 317
Radarr identities present. It produced no active/watched conflicts and no
safety blockers.

# Armed Convergence

After baseline review, the protected worker environment received exactly one
`MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION=true` entry and remained mode `0600`.
The `10` removal-count, `25%` removal-percentage, and 120-minute source-age
limits remained active. Only the worker was recreated from the exact deployed
image.

Report `movie-sync-30.json` used source snapshot
`letterboxd-202607130549573509841-edbd730943a24aa0817b66e5dbc0c0e7`.
It completed with a new heartbeat at 2026-07-13 05:53:08 UTC and exit code 0.
The run had 683 decisions: 550 completed keeps and 133 intentional skips. It
had zero blockers, collection errors, authorization conflicts, watched
authorizations, removals, and file deletions.

SQLite contained 317 present Radarr observations, 550 managed destination
records, and no cleanup history. The armed run therefore converged without an
external deletion.

# Live-Exercise Boundary

No movie disappeared from the valid non-empty Letterboxd snapshot during this
rollout. The service did not fabricate a watched event, remove a test movie, or
delete a real media file to exercise the path. The next genuine disappearance
will be eligible only when its retained database record and published exact
TMDB watched authorization satisfy all policy gates. Its result must be audited
in the worker report and `movie_cleanup_history`.

Disappearances before the bootstrap and first operational lifecycle manifest
are not backfilled as watched history.

# Capacity And Follow-Up

At the final implementation audit the host retained 2.6 GB free at 82% use
while both current and rollback images were present. Docker reported 2.538 GB
of reclaimable unused image/build cache. Disk alerting and an explicit cache
retention threshold remain roadmap items.

# Links

- [Watched Movie Lifecycle Design](../backlog/watched_movie_lifecycle_design.md)
- [Watched Movie Lifecycle Implementation](../backlog/watched_movie_lifecycle_implementation.md)
- [Production Movie Sync](../architecture/movie_sync_production.md)
- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
