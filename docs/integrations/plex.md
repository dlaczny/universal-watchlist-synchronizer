---
type: Integration
title: Plex
description: Source of truth for current availability on the user's Plex server.
tags:
  - plex
  - availability
  - matching
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Purpose

Plex defines what is available now on the user's media server.

# Configuration

| Setting | Meaning |
|---|---|
| `Plex:BaseUrl` | Plex server base URL, for example `http://127.0.0.1:32400`. |
| `Plex:Token` | Plex token. Keep out of committed files. |

# Movie Inventory Sync

- Discover movie libraries through `/library/sections` by filtering type
  `movie`.
- Scan each movie section with `/library/sections/{key}/all?type=1`.
- Fetch per-movie metadata from `/library/metadata/{ratingKey}`.
- Read nested GUID IDs for IMDb, TMDB, and TVDB references.
- Store normalized inventory in `plex_library_items`.

# Matching

The backend matches watchlist movies to Plex movies by:

1. IMDb ID.
2. TMDB ID.
3. Unique normalized title and year fallback.

Ambiguous title/year fallback results are represented as `unknown_match`, not
hidden as unavailable.

# Image Proxy

The backend can proxy Plex images for Plex-only movies through:

```text
GET /api/images/plex/{ratingKey}/{kind}
```

`kind` must be `poster` or `backdrop`.

# Links

- Availability states: [Availability States](../data_models/availability_states.md)
- Backend API: [Backend API](../apis/backend_api.md)

