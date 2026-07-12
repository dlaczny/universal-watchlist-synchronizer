---
type: Runbook
title: Agent Onboarding
description: Starting instructions for agents working in the OKF-first movie sync repository.
tags:
  - agents
  - onboarding
  - okf
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Start Here

`docs/` is the authoritative OKF knowledge layer. `AGENTS.md` and `README.md`
are intentionally thin pointers.

Read, in order:

1. [Watchlist App](../projects/watchlist_app.md)
2. [System Boundaries](../architecture/system_boundaries.md)
3. [Production Movie Sync](../architecture/movie_sync_production.md)
4. [Backend API](../apis/backend_api.md)
5. The relevant system or runbook concept.
6. [Validation](validation.md)

# Standing Rules

- Use the complete backend movie snapshot for production desired state.
- Keep planning side-effect free and preserve stable reason codes.
- Never automatically delete downloaded Radarr files or Plex library media.
- Preserve unmanaged Radarr and Plex-watchlist rows.
- Keep credentials out of Git, logs, reports, Android, and GitHub Actions.
- Keep Android TV and TV/Sonarr work on hold unless scope explicitly changes.
- Update OKF whenever behavior, contracts, ownership, deployment, or operations
  change.

# Context

Use [Migration Context](../references/migration_context.md) only for provenance;
current behavior lives in system, API, architecture, decision, and runbook
concepts.
