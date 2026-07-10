---
type: Project
title: Watchlist App
description: Personal Android TV-first watchlist application backed by a .NET API, MongoDB read model, and local automation workers.
tags:
  - watchlist
  - android-tv
  - backend
  - plex
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

Watchlist App is a local-first personal application for deciding what to watch
on an Android TV. The first client is Android TV; Android phone support is a
later goal.

Version 1 is read-only from the client UI. The Android app must not create,
edit, delete, reorder, or mutate watchlist entries unless the product scope
explicitly changes.

# Goals

- Show movies and TV shows the user wants to watch.
- Mark what is available on the user's Plex server.
- Preserve distinct states for unreleased items, missing Plex items, and
  uncertain Plex matches.
- Keep browsing fast by serving cached normalized data from the backend.
- Keep destructive Radarr and Plex cleanup outside Android-facing flows.

# Sources Of Truth

| Concern | Source |
|---|---|
| Movie watchlist | Letterboxd |
| TV watchlist | TMDB account watchlist |
| Metadata and artwork | TMDB |
| Plex availability | User's Plex server |
| Client read model | MongoDB through the .NET backend |
| Radarr and Plex side effects | Python VOD Filter worker |

# Repository Areas

| Path | Responsibility |
|---|---|
| `backend/` | .NET API, sync orchestration, integrations, MongoDB persistence, read-only endpoints. |
| `android/` | Android TV client. |
| `workers/vod-filter/` | Python automation worker for Radarr and Plex side effects. |
| `deploy/` | Homelab deployment and Portainer notes. |
| `docs/` | Active OKF knowledge base. |

# Links

- Architecture: [System Boundaries](../architecture/system_boundaries.md)
- Backend: [Backend Service](../systems/backend_service.md)
- Android: [Android TV Client](../systems/android_tv_client.md)
- Worker: [VOD Filter Worker](../systems/vod_filter_worker.md)
- Decisions: [Android TV Read-Only V1](../decisions/android_tv_read_only_v1.md)
