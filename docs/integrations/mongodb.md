---
type: Integration
title: MongoDB
description: Persistent movie read model, protected Trakt state, immutable TV generations, Plex inventory, and sync history store.
tags:
  - mongodb
  - persistence
  - read-model
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Purpose

MongoDB stores normalized movie and TV read models used by backend browse and
export endpoints. Browsing does not depend on live third-party calls.

# Collections

| Collection | Purpose |
|---|---|
| `watchlist_items` | Normalized movie rows and legacy TV migration source only; legacy TV rows are not read after migration. |
| `plex_library_items` | Latest Plex movie inventory snapshot. |
| `sync_runs` | Sync status, errors, timestamps, and counts. |
| `trakt_connections` | One encrypted device/OAuth connection; never a plaintext token store. |
| `tv_shows` | Immutable per-generation Phase 1 TV show documents. |
| `tv_sync_manifests` | Staged and published generation manifests; publish-last pointer authority. |
| `tv_lifecycle_events` | Immutable TV lifecycle events with canonical predicate hashes. |

# Local Development

Start local MongoDB from the repository root:

```powershell
docker compose up -d mongo
```

The root `compose.yaml` is for local development only.

# Runtime Behavior

`MongoBootstrapHostedService` inserts deterministic sample records only when
target collections are empty. The legacy-TV migration runs before bootstrap and
creates only exact-identity rows or deterministic quarantine results. MongoDB
outages return `503 Service Unavailable` from MongoDB-backed endpoints.

# Deployment Note

The homelab deployment uses MongoDB Atlas Free Tier because the Proxmox VM CPU
did not expose AVX required by modern MongoDB containers.

# Links

- Watchlist item: [Watchlist Item](../data_models/watchlist_item.md)
- Sync run: [Sync Run](../data_models/sync_run.md)
- TV show: [TV Show](../data_models/tv_show.md)

