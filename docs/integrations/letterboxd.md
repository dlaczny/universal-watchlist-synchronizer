---
type: Integration
title: Letterboxd
description: Source of truth for movie watchlist entries imported by the backend.
tags:
  - letterboxd
  - movies
  - sync
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
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
unavailable or returns malformed JSON.

# Links

- Sync pipeline: [Sync Pipeline](../architecture/sync_pipeline.md)
- Backend service: [Backend Service](../systems/backend_service.md)

