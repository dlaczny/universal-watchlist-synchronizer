---
type: Integration
title: Plex
description: Backend movie-inventory authority and worker-managed universal watchlist destination; library media is read-only.
tags:
  - plex
  - availability
  - watchlist
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
---

# Backend Inventory

The backend discovers movie libraries, reads inventory and GUID metadata,
stores normalized rows in `plex_library_items`, and matches watchlist movies by
IMDb ID, TMDB ID, then unique normalized title/year. Ambiguous fallback matches
remain `unknown_match`.

Backend configuration uses `Plex:BaseUrl` and `Plex:Token`. The backend also
proxies stored movie poster/backdrop paths through its image API.

# Worker Watchlist

The worker reads the Plex watchlist and configured movie library. A movie is
desired on the Plex watchlist when it is on an owned streaming service, is in
the Plex library, or has a completed Radarr download. Existing desired rows are
adopted; unrelated rows remain unmanaged.

Ordinary cleanup removes only worker-owned no-longer-desired watchlist rows.
Two exact-TMDB authorizations can also remove a pre-existing watchlist row:

- a current published Letterboxd watched event;
- a durable manual Radarr disappearance for an identity that is neither active
  nor watched in the published source state.

Both override Plex-library membership for the watchlist decision only. Plex
library media is never removed or otherwise mutated by production sync.
Watchlist add and remove operations require exact TMDB identity; title/year is
used only to narrow discovery results.

Worker configuration uses `PLEX_URL`, `PLEX_TOKEN`, and optional
`PLEX_LIBRARY_NAME`.

# Links

- [Availability States](../data_models/availability_states.md)
- [Production Movie Sync](../architecture/movie_sync_production.md)
