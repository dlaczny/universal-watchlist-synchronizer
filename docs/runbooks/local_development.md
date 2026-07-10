---
type: Runbook
title: Local Development
description: Commands for running the backend, Android client, and worker locally.
tags:
  - local-development
  - backend
  - android
  - worker
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Backend

Start MongoDB:

```powershell
docker compose up -d mongo
```

Run the API:

```powershell
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5000
```

Health check:

```powershell
Invoke-RestMethod http://localhost:5000/healthz
```

# Android

Set Java when needed:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio\jbr'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
```

Run tests and build:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

# Worker

Run from `workers/vod-filter`:

```powershell
$env:WATCHLIST_SOURCE="watchlist_app"
$env:WATCHLIST_APP_URL="http://localhost:5000"
$env:WATCHLIST_APP_SYNC_FIRST="false"
python run_all_syncs.py --dry-run --quiet
```

Compare direct and backend source modes:

```powershell
$env:WATCHLIST_APP_URL="http://localhost:5000"
python compare_watchlist_sources.py --skip-watchlist-app-sync
```

# Links

- Validation: [Validation](validation.md)
- Worker operations: [VOD Filter Operations](vod_filter_operations.md)

