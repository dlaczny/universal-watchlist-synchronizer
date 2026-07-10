---
type: System
title: VOD Filter Worker
description: Python worker that consumes backend exports and performs local Radarr/Plex automation with dry-run safety.
tags:
  - worker
  - python
  - radarr
  - plex
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

The VOD Filter worker lives under `workers/vod-filter/`. It was migrated from
`C:\NextCloud\plex-radarr-letterboxed-tmdb\vod-filter` into this monorepo.

The worker is the destructive automation layer. It can add or remove Radarr
movies, clean Plex watchlist entries, optionally delete library files, write
dry-run reports, and persist local run history in SQLite.

# Source Modes

| Mode | Meaning |
|---|---|
| `WATCHLIST_SOURCE=watchlist_app` | Preferred monorepo mode. Optionally calls backend sync, then reads `GET /api/export/radarr/movies`. |
| `WATCHLIST_SOURCE=letterboxd` | Fallback direct mode where the worker calls Letterboxd/TMDB itself. |

# Important Scripts

| Script | Role |
|---|---|
| `run_all_syncs.py` | Runs cleanup, main sync, and library sync sequentially by default; parallel mode is manual. |
| `src/main.py` | Main Letterboxd/backend-to-Radarr/Plex sync entry point. |
| `cleanup_removed_movies.py` | Removes Radarr/Plex watchlist entries when source watchlist items disappear. |
| `sync_library_to_watchlist.py` | Library-to-watchlist synchronization flow. |
| `compare_watchlist_sources.py` | Compares direct Python source with backend export mode. |
| `cache_inspect.py` | Inspects local SQLite cache/run history. |
| `validate_providers.py` | Validates provider configuration. |
| `continuous_sync.py` | Runs sync on an interval. |

# Important Modules

| Module | Role |
|---|---|
| `src/config.py` | Environment loading and validation. |
| `src/clients/watchlist_app_client.py` | Backend export client and mapper. |
| `src/clients/letterboxd_client.py` | Direct Letterboxd client. |
| `src/clients/tmdb_client.py` | TMDB provider availability client. |
| `src/clients/radarr_client.py` | Radarr integration. |
| `src/clients/plex_client.py` | Plex integration. |
| `src/services/cache_service.py` | SQLite cache and run history. |
| `src/services/radarr_service.py` | Radarr side-effect orchestration. |
| `src/services/plex_service.py` | Plex side-effect orchestration. |

# Runtime Storage

Runtime files stay under `workers/vod-filter/data/` and are ignored by git:

- `vod-filter.db`
- `reports/`
- `logs/`

# Safety Rules

- Use `--dry-run` before production cleanup.
- Production cleanup requires explicit confirmation flags.
- Radarr file deletion is off by default for VOD-available cleanup.
- Cache misses are skipped rather than deleted when cleanup cannot identify an
  item safely.

# Links

- Operations: [VOD Filter Operations](../runbooks/vod_filter_operations.md)
- Worker boundary decision: [VOD Filter Worker Boundary](../decisions/vod_filter_worker_boundary.md)
- Export API: [Export Endpoints](../apis/export_endpoints.md)

