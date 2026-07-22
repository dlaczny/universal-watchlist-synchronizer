---
type: Project
title: Watchlist App
description: Personal movie sync system backed by a .NET API, MongoDB read model, guarded local worker, and an on-hold Android TV client.
tags:
  - watchlist
  - sync
  - backend
  - plex
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
---

# Current Product

Watchlist App keeps a Letterboxd movie watchlist aligned with TMDB metadata,
Plex availability/watchlist state, and Radarr automation. It also has a
non-destructive Trakt-backed TV read model: the backend publishes immutable TV
generations with watched progress and Poland provider observations. The worker
continues to execute guarded movie behavior only.

Android TV work is deferred until explicitly requested. Plex history, Trakt
history writes, Sonarr, and Plex-watchlist TV behavior are not part of Phase 1.

# Goals

- Preserve Letterboxd as desired movie-watchlist authority.
- Explain every destination add, keep, remove, skip, uncertainty, and failure.
- Keep unrelated Radarr and Plex watchlist entries unchanged.
- Delete downloaded files only for an exact published watched Radarr event
  behind the dedicated host gate; never mutate Plex library media.
- Serve cached browse data without depending on live integrations.
- Deploy only tested, secret-free commits with rollback.

# Sources Of Truth

| Concern | Source |
|---|---|
| Desired movies | Letterboxd imported by backend |
| Active/watched lifecycle | Latest published backend source manifest |
| Identity, metadata, owned-service availability | TMDB cached by backend |
| Normalized desired state | MongoDB complete movie snapshot |
| Existing downloads and monitoring | Live Radarr |
| Existing media and universal watchlist | Live Plex |
| Worker ownership and run history | Worker SQLite |
| Manual Radarr disappearance | Worker SQLite observation ledger |
| Desired TV membership and watched progress | Trakt, read by backend |
| TV metadata and Poland provider observations | TMDB exact-ID reads |
| Published TV state | MongoDB immutable TV generation |
| Deployed release | `/opt/watchlist-prod/state/last-successful.sha` |

# Repository Areas

| Path | Responsibility |
|---|---|
| `backend/` | .NET movie sync plus Trakt TV read generations, persistence, APIs, and exports. |
| `workers/vod-filter/` | Movie planning, policy, Radarr/Plex-watchlist actions, reports. |
| `deploy/`, `scripts/` | Production containers, exact-SHA CI gate, systemd deployment. |
| `android/` | Read-only Android TV client; deferred until an explicit user request. |
| `docs/` | Authoritative OKF knowledge layer. |

# Links

- [Production Movie Sync](../architecture/movie_sync_production.md)
- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
- [System Boundaries](../architecture/system_boundaries.md)
- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
- [Sync Correctness Priority](../decisions/sync_correctness_priority.md)
