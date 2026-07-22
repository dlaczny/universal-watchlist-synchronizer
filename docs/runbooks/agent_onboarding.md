---
type: Runbook
title: Agent Onboarding
description: Starting instructions for agents working in the OKF-first movie sync repository.
tags:
  - agents
  - onboarding
  - okf
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
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
- Delete Radarr files only for an exact TMDB removal carrying a published
  Letterboxd watched event and only when the watched-file gate is enabled.
- Never mutate Plex library media.
- Preserve unmanaged Radarr and Plex-watchlist rows except for the explicit
  watched and manually observed Radarr-removal authorizations.
- Keep credentials out of Git, logs, reports, Android, and GitHub Actions.
- Treat Phase 1 Trakt TV generations as implemented backend behavior. Keep
  Android TV deferred until an explicit user request, and keep Plex history,
  Trakt writes, Sonarr, and Plex-watchlist TV mutations outside Phase 1.
- Update OKF whenever behavior, contracts, ownership, deployment, or operations
  change.

# Context

Use [Migration Context](../references/migration_context.md) only for provenance;
current behavior lives in system, API, architecture, decision, and runbook
concepts.

For TV work also read [TV Sync Read Model](../architecture/tv_sync_read_model.md),
[Trakt](../integrations/trakt.md), and [TV Sync Operations](tv_sync_operations.md).
