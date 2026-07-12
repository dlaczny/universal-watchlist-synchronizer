---
type: Decision
title: VOD Filter Worker Boundary
description: Radarr and Plex-watchlist mutations belong to the guarded Python worker, with library media and downloaded files protected.
tags:
  - decision
  - worker
  - radarr
  - plex
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Decision

The Python worker owns Radarr and Plex-watchlist mutation. The production path
must compute a complete plan, enforce policy, and track destination ownership
before mutation.

Plex library media is read-only. Downloaded Radarr files are never deleted
automatically, and a no-longer-desired Radarr row with a file requires manual
review rather than automatic removal.

# Rationale

Backend and client flows need predictable read contracts. Local media actions
require live destination state, explicit ownership, removal limits, reports,
and host-local credentials that do not belong in Android or the backend read
API.

# Consequences

- The backend exposes a complete cached movie snapshot.
- The production worker uses backend source mode and retains SQLite ownership.
- Unmanaged destination rows are preserved.
- Legacy direct-source/file-cleanup settings are not authority for production
  movie behavior.
- Any future file deletion or Plex library mutation requires a new explicit
  safety design and decision.
