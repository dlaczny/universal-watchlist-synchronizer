---
type: Decision
title: Sync Correctness Priority
description: Android TV is deferred while movie correctness and the non-destructive backend TV read model take priority.
tags:
  - decision
  - sync
  - worker
  - backend
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
---

# Decision

Keep Android TV feature work deferred until explicitly requested. Prioritize
correct and explainable movie sync, while allowing the implemented Phase 1
backend-only Trakt TV read model to publish safe immutable generations.

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
- TV source reads use complete publish-last generations; source/cursor failures
  preserve the old pointer, and provider failure remains `unknown` or `stale`.
- Every TV mutation gate is hard-locked false. Plex history, Trakt history
  writes, Sonarr, and Plex-watchlist TV actions remain later-phase work.

# Remaining Consequences

- Operate the first deployment in reconciliation mode before enabling apply.
- Keep operator review for ordinary downloaded managed Radarr rows, watched
  cleanup rollout, and uncertain backend Plex matches.
- Do not expand the TV read model into a destination mutation path without a
  separately approved later phase and its safeguards.
- Do not resume Android changes without an explicit user request.

# Links

- [Production Movie Sync](../architecture/movie_sync_production.md)
- [Roadmap](../backlog/roadmap.md)
- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
