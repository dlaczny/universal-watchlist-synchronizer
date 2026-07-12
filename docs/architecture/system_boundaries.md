---
type: Architecture
title: System Boundaries
description: Ownership boundaries between clients, backend persistence, external integrations, the movie worker, and deployment tooling.
tags:
  - architecture
  - boundaries
  - agents
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
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
                       Python movie worker <--> SQLite ownership/observations
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
  applies policy gates, and tracks destination rows it added or adopted.
- Downloaded Radarr files are preserved by ordinary cleanup. Only an exact
  published watched event may delete them behind the dedicated host gate.
- Plex library media is always read-only.
- Unmanaged Radarr and Plex-watchlist rows are preserved except for the exact
  watched/manual-removal authorizations defined by Production Movie Sync.
- Android clients call only backend read APIs and contain no integration
  credentials. Android TV feature work is on hold.
- GitHub validates public code without production secrets. The homelab host
  keeps runtime credentials outside its clean Git checkout.

# Component Ownership

| Component | Owns | Does not own |
|---|---|---|
| Android TV client | Read-only rendering and remote navigation. | External credentials, sync orchestration, mutations. |
| .NET backend | Source ingestion, metadata, Plex inventory, MongoDB read model, sync and export contracts. | Radarr or Plex-watchlist mutations. |
| MongoDB | Normalized watchlist, published Letterboxd lifecycle, Plex inventory, backend sync history. | Live destination state. |
| Movie worker | Live-state collection, planning, policy, authorized Radarr/Plex-watchlist actions, SQLite ownership/observations/reports. | Backend persistence, Plex library mutation, file deletion without an exact gated watched event. |
| GitHub Actions | Tests, OKF validation, secret scanning, image-build validation. | Runtime credentials or deployment access. |
| Homelab deployer | Exact-SHA CI gate, local image build, health checks, release state, rollback. | Editing the legacy `/opt/watchlist-app` checkout. |

# Links

- [Production Movie Sync](movie_sync_production.md)
- [Backend Service](../systems/backend_service.md)
- [VOD Filter Worker](../systems/vod_filter_worker.md)
- [Deployment Tooling](../systems/deployment_tooling.md)
