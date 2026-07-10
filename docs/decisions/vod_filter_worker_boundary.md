---
type: Decision
title: VOD Filter Worker Boundary
description: Destructive Radarr and Plex cleanup belongs to the Python worker rather than Android-facing backend flows.
tags:
  - decision
  - worker
  - radarr
  - plex
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Decision

The Python VOD Filter worker owns local destructive automation: Radarr
add/remove, Plex watchlist cleanup, optional library/file cleanup, dry-run
reports, and local SQLite run history.

# Rationale

Android-facing flows should remain read-only and low-risk. Destructive local
automation needs explicit dry-run and production guardrails.

# Consequences

- Backend exposes cached export endpoints for the worker.
- Worker can keep a fallback direct Letterboxd mode.
- Destructive cleanup must not be added to Android-facing API flows without a
  separate safety design.

