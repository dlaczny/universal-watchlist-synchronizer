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
version: 0.2.0
---

# Decision

Keep Android TV feature work on hold. Prioritize correct and explainable movie
sync across Letterboxd, TMDB, MongoDB, Radarr, and Plex.

# Implemented Consequences

- The backend has an authenticated movie-only sync and complete worker snapshot.
- One worker engine collects, plans, gates, optionally applies, and reports.
- Decisions share stable actions and reason codes across reports and tests.
- SQLite ownership protects unmanaged destination rows.
- Downloaded files and Plex library media are excluded from automatic deletion.
- GitHub validates the full movie release without production secrets.
- Homelab deployment is exact-SHA gated, health checked, and rollback capable.

# Remaining Consequences

- Operate the first deployment in reconciliation mode before enabling apply.
- Keep operator review for downloaded managed Radarr rows and uncertain backend
  Plex matches.
- Do not expand to TV/Sonarr until movie operations have proved stable.
- Limit Android changes to contract-preserving fixes until this decision changes.

# Links

- [Production Movie Sync](../architecture/movie_sync_production.md)
- [Roadmap](../backlog/roadmap.md)
