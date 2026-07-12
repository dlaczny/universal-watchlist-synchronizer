---
type: Decision
title: VOD Filter Worker Boundary
description: Radarr and Plex-watchlist mutations belong to the guarded Python worker, with an exact watched-file exception and read-only Plex library.
tags:
  - decision
  - worker
  - radarr
  - plex
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
---

# Decision

The Python worker owns Radarr and Plex-watchlist mutation. The production path
must compute a complete plan, enforce policy, and track destination ownership
before mutation.

Plex library media is read-only. Ordinary no-longer-desired Radarr rows with
files require manual review. The only automatic file-deletion exception is an
exact TMDB Radarr removal carrying a current published Letterboxd watched event
ID and passing a dedicated default-off host gate.

# Rationale

Backend and client flows need predictable read contracts. Local media actions
require live destination state, explicit ownership, removal limits, reports,
and host-local credentials that do not belong in Android or the backend read
API.

# Consequences

- The backend exposes a complete cached movie snapshot.
- The production worker uses backend source mode and retains SQLite ownership.
- Unmanaged destination rows are preserved except when an exact published
  watched event or durable manual-Radarr-removal observation authorizes the
  matching cleanup defined by the production architecture.
- Legacy direct-source/file-cleanup settings are not authority for production
  movie behavior.
- Any additional file-deletion category or any Plex-library mutation requires
  a new explicit safety design and decision.
