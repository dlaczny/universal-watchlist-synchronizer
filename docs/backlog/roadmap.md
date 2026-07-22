---
type: Backlog
title: Roadmap
description: Completed production movie foundation and remaining operational, TV, Android, and cleanup work.
tags:
  - backlog
  - roadmap
timestamp: 2026-07-13T09:11:16Z
version: 0.8.0
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
- Published Letterboxd active/watched/reactivated lifecycle, exact-TMDB watched
  cleanup authorization, manual Radarr disappearance handling, reactivation,
  and cleanup audit history.
- Supervised watched-lifecycle rollout with 289 active movies, 317 Radarr
  observations, gated baseline report 29, armed convergence report 30, and no
  blockers, removals, or collection errors. Pre-feature disappearances are not
  backfilled as watched history.

# Completed TV Phase 1 Read Model

- Protected single-account Trakt device OAuth with a persistent server-side
  data-protection key ring and redacted public status.
- Complete Trakt watchlist/progress/schedule reads, exact pagination provenance,
  activity-cursor race rejection, and immutable publish-last Mongo generations.
- Deterministic migration/quarantine of legacy TV rows, TV lifecycle reduction,
  S00 exact identities, and Poland TMDB provider enrichment.
- TV browse/detail/state filtering, sync status/manual read sync, and a
  version-1 read-only worker export with no cleanup authorizations.
- Persistent key-ring deployment mount and all six TV mutation gates locked
  false in examples and production Compose.

This is implementation/test completion, not production rollout evidence. Track
real deployment evidence in [TV Integration Rollout](../reports/tv_integration_rollout.md).

# Operations Next

- Resolve the `Resurrection` (2025) Radarr folder collision: desired TMDB
  `878608` conflicts with existing TMDB `1279580`; automatic add remains
  skipped.
- Review Plex catalog misses for `Memories` (1995, TMDB `1622426`) and
  `Resurrection` (2025, TMDB `1279580`); both remain unowned skips.
- Expose the existing Plex watchlist rows `Manifesto` (2017) and `Always`
  (2011), which have no TMDB GUID, as explicit report decisions instead of log
  warnings only.
- Add notification/alerting for repeated blocked, partial, or unhealthy runs.
- Add host disk alerts and a build-cache retention threshold; the lifecycle
  rollout retained 2.6 GB free at 82% use with current and rollback images
  present, plus 2.538 GB of reclaimable unused image/build cache.
- Decide a rollback-observation period, then remove legacy backend deployment
  files and `scripts/deploy-watchlist-local.sh` if no longer needed.
- Consider retiring direct-source worker scripts after the production engine
  has enough operating history.

# Backend Later

- Add operator review for `unknown_match` Plex availability.
- Refine owned-service matching with confirmed provider IDs.
- Cache image bytes only if proxy latency or availability justifies it.

# TV And Sonarr

- Operate and collect rollout evidence for the completed Phase 1 backend read
  model. Do not treat a successful read generation as destination authority.
- Complete and review the configured-account Plex-history bootstrap and
  effectively-once Trakt outbox in [Phase 2](../superpowers/plans/2026-07-13-tv-phase-2-plex-trakt-history.md)
  before reversible destinations.
- Roll out Sonarr additions and Plex-watchlist lifecycle only through
  [Phase 3](../superpowers/plans/2026-07-13-tv-phase-3-reversible-destinations.md),
  before the independently gated [season](../superpowers/plans/2026-07-13-tv-phase-4-season-cleanup.md)
  and [terminal-series](../superpowers/plans/2026-07-13-tv-phase-5-terminal-cleanup.md)
  deletion phases.
- Keep every TV mutation disabled until its staged tests, reports, adoption
  review, grace periods, and supervised validation pass.

# Android TV

All Android work is in the [hard-gated Android TV Integration Backlog](android_tv_tv_integration.md).
Do not start or resume it until the user explicitly asks.

# Documentation Candidates

- Add machine-readable schemas only when a contract consumer needs them.
- Archive or remove the completed implementation plan after the agreed
  operational observation period.
