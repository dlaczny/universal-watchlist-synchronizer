---
type: Backlog
title: Roadmap
description: Completed production movie foundation and remaining operational, TV, Android, and cleanup work.
tags:
  - backlog
  - roadmap
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
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

# Operations Next

- Complete and observe the first reconciliation-only homelab release.
- Enable apply under supervision and verify idempotent second-run decisions.
- Add an operator workflow for managed Radarr rows with downloaded files that
  are no longer desired.
- Add notification/alerting for repeated blocked, partial, or unhealthy runs.
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
