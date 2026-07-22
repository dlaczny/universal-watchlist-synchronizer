---
type: Runbook
title: Local Development
description: Local backend, production movie-worker, and on-hold Android commands.
tags:
  - local-development
  - backend
  - worker
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Backend

```powershell
docker compose up -d mongo
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5000
Invoke-RestMethod http://localhost:5000/healthz
```

Use the ignored
`backend/src/Watchlist.Api/appsettings.Development.Local.json` for local
credentials. Set `Sync:ApiKey` there when testing authenticated mutation.

For a local Phase 1 TV read-model test, use a disposable Trakt connection and
an absolute ignored key-ring path. Keep every TV mutation flag false; a TV sync
only reads and publishes MongoDB state.

```powershell
$env:DataProtection__KeyRingPath=(Resolve-Path .artifacts).Path + "\\tv-keyring"
$env:TRAKT_HISTORY_SYNC_APPLY="false"
$env:TV_SYNC_APPLY="false"
```

# Movie Worker

Run from `workers/vod-filter` after creating an ignored `.env`:

```powershell
$env:WATCHLIST_SOURCE="watchlist_app"
$env:WATCHLIST_APP_URL="http://localhost:5000"
$env:WATCHLIST_APP_SYNC_KEY="local-only-matching-backend-key"
$env:MOVIE_SYNC_APPLY="false"
python sync_movies.py --skip-backend-sync
```

Review `data/reports/` before using `python sync_movies.py --apply`. The legacy
direct-source commands are compatibility tools, not the production path.

# Android TV

Android TV work is deferred and must not be started or resumed without an
explicit user request. Its commands remain in the
[Android TV Integration Backlog](../backlog/android_tv_tv_integration.md).

# Links

- [Validation](validation.md)
- [VOD Filter Operations](vod_filter_operations.md)
- [TV Sync Operations](tv_sync_operations.md)
