---
type: Backlog
title: Roadmap
description: Completed production movie foundation and remaining operational, TV, Android, and cleanup work.
tags:
  - backlog
  - roadmap
timestamp: 2026-07-12T00:00:00Z
version: 0.5.0
---

# Completed Movie Foundation

- Authenticated backend movie-only source sync.
- Complete source snapshot with freshness, nullable identity, and eligibility
  reasons.
- Deterministic Radarr and Plex-watchlist plan with SQLite ownership.
- Stale/failed/ambiguous/empty-source and removal-volume policy gates.
- Independent apply outcomes, JSON/Markdown reports, run history, and health.
- Non-root read-only production containers and exact runtime dependency lock.
- One `Movie CI` gate with Mongo-backed tests, deployment tests, secret scans,
  and image builds.
- Exact-SHA homelab deployer with clean checkout, health cutover, and rollback.
- Supervised reconciliation/apply rollout, converged second run, unattended
  hourly apply, and active five-minute CI-gated deployment timer.
- Production boundary handling for Radarr exclusions/folder collisions and
  exact-TMDB Plex discovery/manual skips.

# Operations Next

- Roll out the implemented [Watched Movie Lifecycle Design](watched_movie_lifecycle_design.md):
  deploy with watched file deletion disabled, establish the Radarr observation
  baseline, review the first reconciliation, then enable the protected host
  gate and supervise apply/convergence. The implementation is test-covered but
  remains an operations item until this rollout is recorded.
- Record explicitly that disappearances before the first published lifecycle
  manifest are not backfilled as watched history.
- Resolve the `Resurrection` (2025) Radarr folder collision: desired TMDB
  `878608` conflicts with existing TMDB `1279580`; automatic add remains
  skipped.
- Review Plex catalog misses for `Memories` (1995, TMDB `1622426`) and
  `Resurrection` (2025, TMDB `1279580`); both remain unowned skips.
- Expose the existing Plex watchlist rows `Manifesto` (2017) and `Always`
  (2011), which have no TMDB GUID, as explicit report decisions instead of log
  warnings only.
- Add notification/alerting for repeated blocked, partial, or unhealthy runs.
- Add host disk alerts and a build-cache retention threshold; the first stable
  rollout left about 2.1 GB free at 86% filesystem use before final cache prune.
- Decide a rollback-observation period, then remove legacy backend deployment
  files and `scripts/deploy-watchlist-local.sh` if no longer needed.
- Consider retiring direct-source worker scripts after the production engine
  has enough operating history.

# Backend Later

- Add operator review for `unknown_match` Plex availability.
- Refine owned-service matching with confirmed provider IDs.
- Cache image bytes only if proxy latency or availability justifies it.

# TV And Sonarr

- Add Plex TV inventory/matching only after movie sync is stable in operation.
- Define and test the Sonarr export and ownership policy before any mutation.

# Android TV: On Hold

- Preserve read-only backend-only contracts.
- Resume component extraction, lifecycle coverage, and connected focus tests
  only after the hold decision changes.

# Documentation Candidates

- Add machine-readable schemas only when a contract consumer needs them.
- Archive or remove the completed implementation plan after the agreed
  operational observation period.
