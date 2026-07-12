---
type: Architecture
title: System Boundaries
description: Ownership boundaries between clients, backend persistence, external integrations, the movie worker, and deployment tooling.
tags:
  - architecture
  - boundaries
  - agents
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Overview

The active production path is backend-owned movie ingestion followed by a
worker-owned plan-and-apply process.

```text
Letterboxd --\
TMDB --------+--> .NET backend --> MongoDB --> read APIs
Plex library-/          |
                       +--> complete movie snapshot
                                  |
                       Python movie worker <--> SQLite ownership
                          |               |
                       Radarr        Plex watchlist

Android TV --> backend read APIs only (client work is on hold)
```

# Rules

- Letterboxd is the desired movie-watchlist source. The backend imports it and
  owns TMDB enrichment, Plex inventory matching, and MongoDB persistence.
- The deployed worker consumes the complete backend movie snapshot. It does not
  infer desired state from the filtered Radarr compatibility export.
- The worker reads live Radarr and Plex state, computes a deterministic plan,
  applies policy gates, and owns only destination rows it added or adopted.
- Automatic sync never deletes a downloaded Radarr file or Plex library media.
- Unmanaged Radarr movies and Plex watchlist entries are preserved.
- Android clients call only backend read APIs and contain no integration
  credentials. Android TV feature work is on hold.
- GitHub validates public code without production secrets. The homelab host
  keeps runtime credentials outside its clean Git checkout.

# Component Ownership

| Component | Owns | Does not own |
|---|---|---|
| Android TV client | Read-only rendering and remote navigation. | External credentials, sync orchestration, mutations. |
| .NET backend | Source ingestion, metadata, Plex inventory, MongoDB read model, sync and export contracts. | Radarr or Plex-watchlist mutations. |
| MongoDB | Normalized watchlist, Plex inventory, backend sync history. | Live destination state. |
| Movie worker | Live-state collection, planning, policy, Radarr/Plex-watchlist actions, SQLite ownership and reports. | Backend persistence, Plex library deletion, downloaded-file deletion. |
| GitHub Actions | Tests, OKF validation, secret scanning, image-build validation. | Runtime credentials or deployment access. |
| Homelab deployer | Exact-SHA CI gate, local image build, health checks, release state, rollback. | Editing the legacy `/opt/watchlist-app` checkout. |

# Links

- [Production Movie Sync](movie_sync_production.md)
- [Backend Service](../systems/backend_service.md)
- [VOD Filter Worker](../systems/vod_filter_worker.md)
- [Deployment Tooling](../systems/deployment_tooling.md)
