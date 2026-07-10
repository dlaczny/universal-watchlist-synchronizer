---
type: Runbook
title: Validation
description: Verification commands for docs, backend, Android, and worker changes.
tags:
  - validation
  - tests
  - okf
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# OKF Docs

```powershell
python tests\validate_okf.py
```

# Backend

```powershell
dotnet build backend\src\Watchlist.Api\Watchlist.Api.csproj
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --no-restore --no-build
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName!~Mongo"
```

Full Application tests can include Mongo-related timeout behavior. Treat that as
pre-existing unless a current change touches Mongo integration behavior.

# Android

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

For Android TV UI changes, also perform the manual remote test in
[Android TV Client](../systems/android_tv_client.md).

# Worker

Run from `workers/vod-filter`:

```powershell
python -m pytest tests\vod_filter -q
python run_all_syncs.py --dry-run --quiet
```

Long worker dry-runs can exceed Codex command timeouts; when that happens,
inspect the generated report and run history before interpreting the result.

