---
type: Integration
title: Letterboxd
description: Source of truth for active movie intent and published watched transitions imported by the backend.
tags:
  - letterboxd
  - movies
  - sync
timestamp: 2026-07-08T00:00:00Z
version: 0.2.0
---

# Purpose

Letterboxd is the source of truth for movies the user wants to watch.

# Backend Import

The backend imports movies from the configured `Letterboxd:WatchlistUrl`.
Environment override: `Letterboxd__WatchlistUrl`.

The current proxy returns Radarr-style JSON with:

```text
id, imdb_id, title, release_year, clean_title, adult
```

# Mapping

| Source field | Backend usage |
|---|---|
| `id` | Backend `sourceId`; also first candidate TMDB movie ID. |
| `imdb_id` | Stored for TMDB lookup fallback and Plex matching. |
| `clean_title` | Stored as Letterboxd path. |
| `title`, `release_year`, `adult` | Used in normalized movie records and exports. |

# Failure Behavior

The Letterboxd sync endpoint returns dependency errors when the proxy is
unavailable or returns malformed JSON. Zero rows, duplicate source IDs,
nonpositive IDs, and blank required text are rejected before repository access.
Such a run performs no lifecycle transition, destination planning authority, or
manifest publication.

# Lifecycle Publication

For each valid non-empty snapshot, the backend compares the new source IDs with
the latest published manifest:

- a new active ID appends `added`;
- a previously active missing ID appends `watched`;
- a watched ID that reappears appends `reactivated`;
- a still-watched ID retains its original watched event and version.

Documents are retained. The complete manifest is written last and its snapshot
ID is returned by the sync API. One successful non-empty snapshot is enough to
confirm a watched transition. Pre-feature disappearances are not reconstructed
or backfilled.

# Links

- Sync pipeline: [Sync Pipeline](../architecture/sync_pipeline.md)
- Backend service: [Backend Service](../systems/backend_service.md)

