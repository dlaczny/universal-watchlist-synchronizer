---
type: Runbook
title: Agent Onboarding
description: Starting instructions for coding agents working in this OKF-first repository.
tags:
  - agents
  - onboarding
  - okf
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Start Here

This repository uses `docs/` as the OKF bundle and active knowledge layer.
`AGENTS.md` is intentionally short; detailed project rules live in OKF.

# Required Reading For Most Work

1. [Watchlist App](../projects/watchlist_app.md)
2. [System Boundaries](../architecture/system_boundaries.md)
3. [Backend API](../apis/backend_api.md)
4. Relevant system doc:
   - [Backend Service](../systems/backend_service.md)
   - [Android TV Client](../systems/android_tv_client.md)
   - [VOD Filter Worker](../systems/vod_filter_worker.md)
5. [Validation](validation.md)

# Standing Rules

- Preserve read-only Android v1 behavior unless scope changes explicitly.
- Keep Android clients behind the backend API.
- Keep integration credentials out of Android and committed files.
- Keep destructive Radarr/Plex cleanup in worker flows.
- Update OKF concepts with code changes that alter behavior, contracts, data
  flow, integrations, or runbooks.

# Historical Context

Use [Migration Context](../references/migration_context.md) when you need
provenance from the worker migration.
