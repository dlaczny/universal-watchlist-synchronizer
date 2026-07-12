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

Android TV feature work is on hold. For required contract-preserving changes:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio\jbr'
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

# Links

- [Validation](validation.md)
- [VOD Filter Operations](vod_filter_operations.md)
