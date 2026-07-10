---
type: Integration
title: MongoDB
description: Persistent normalized read model, Plex inventory, and sync history store.
tags:
  - mongodb
  - persistence
  - read-model
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Purpose

MongoDB stores the normalized read model used by Android and backend export
endpoints. Browsing should not depend on live third-party calls.

# Collections

| Collection | Purpose |
|---|---|
| `watchlist_items` | Normalized movie and TV watchlist records with metadata and availability. |
| `plex_library_items` | Latest Plex movie inventory snapshot. |
| `sync_runs` | Sync status, errors, timestamps, and counts. |

# Local Development

Start local MongoDB from the repository root:

```powershell
docker compose up -d mongo
```

The root `compose.yaml` is for local development only.

# Runtime Behavior

`MongoBootstrapHostedService` inserts deterministic sample records only when
target collections are empty. MongoDB outages return `503 Service Unavailable`
from MongoDB-backed endpoints.

# Deployment Note

The homelab deployment uses MongoDB Atlas Free Tier because the Proxmox VM CPU
did not expose AVX required by modern MongoDB containers.

# Links

- Watchlist item: [Watchlist Item](../data_models/watchlist_item.md)
- Sync run: [Sync Run](../data_models/sync_run.md)

