---
type: Architecture
title: System Boundaries
description: Ownership boundaries between Android clients, the backend, MongoDB, external integrations, and workers.
tags:
  - architecture
  - boundaries
  - agents
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

The repository is a monorepo with a .NET backend, Android clients, local
automation workers, deployment tooling, and OKF documentation.

```text
Letterboxd      TMDB Account/API      Plex Server
    \                 |                  /
     \                |                 /
      +---------- .NET Backend --------+
                    |
                  MongoDB
                    |
              Read-only API
                    |
             Android TV Client

.NET Backend export API
        |
 Python VOD Filter worker
        |
   Radarr / Plex mutations
```

# Rules

- Android clients call only the backend API.
- Android clients must not call Letterboxd, TMDB, Plex, Radarr, or MongoDB directly.
- The backend owns integration credentials, API tokens, sync logic, caching,
  matching, persistence, and read-only client contracts.
- MongoDB stores the normalized read model used by clients.
- The Python worker owns destructive Radarr and Plex side effects.
- Destructive behavior must stay outside Android-facing API flows unless an
  explicit safety design is added.

# Component Ownership

| Component | Owns | Does Not Own |
|---|---|---|
| Android TV client | Read-only browsing, focus behavior, state restoration, rendering backend DTOs. | External credentials, direct third-party calls, watchlist mutations. |
| .NET backend | Syncs, matching, read model, export APIs, image proxying, integration credentials. | Radarr/Plex cleanup side effects. |
| MongoDB | Cached normalized watchlist, Plex inventory, sync history. | Live third-party behavior. |
| VOD Filter worker | Radarr import/removal, Plex watchlist/library cleanup, dry-run reports, local SQLite run history. | Android contracts and backend persistence. |

# Links

- Project: [Watchlist App](../projects/watchlist_app.md)
- Backend: [Backend Service](../systems/backend_service.md)
- Worker boundary decision: [VOD Filter Worker Boundary](../decisions/vod_filter_worker_boundary.md)

