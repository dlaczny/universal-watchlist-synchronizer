---
type: Decision
title: Sync Correctness Priority
description: Android TV feature work remains on hold while movie sync correctness, explainability, and operations take priority.
tags:
  - decision
  - sync
  - worker
  - backend
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
---

# Decision

Keep Android TV feature work on hold. Prioritize correct and explainable movie
sync across Letterboxd, TMDB, MongoDB, Radarr, and Plex.

# Implemented Consequences

- The backend has an authenticated movie-only sync and complete worker snapshot.
- One worker engine collects, plans, gates, optionally applies, and reports.
- Decisions share stable actions and reason codes across reports and tests.
- SQLite ownership protects unmanaged destination rows.
- Plex library media is excluded from automatic deletion. Downloaded files are
  excluded from ordinary cleanup; exact published watched removals use the
  separately gated exception.
- GitHub validates the full movie release without production secrets.
- Homelab deployment is exact-SHA gated, health checked, and rollback capable.

# Remaining Consequences

- Operate the first deployment in reconciliation mode before enabling apply.
- Keep operator review for ordinary downloaded managed Radarr rows, watched
  cleanup rollout, and uncertain backend Plex matches.
- Do not expand to TV/Sonarr until movie operations have proved stable.
- Limit Android changes to contract-preserving fixes until this decision changes.

# Links

- [Production Movie Sync](../architecture/movie_sync_production.md)
- [Roadmap](../backlog/roadmap.md)
