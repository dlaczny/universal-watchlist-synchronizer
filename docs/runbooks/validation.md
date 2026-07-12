---
type: Runbook
title: Validation
description: Local commands equivalent to the production movie CI and deployment checks.
tags:
  - validation
  - tests
  - okf
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
---

# OKF And Deployment Tooling

```powershell
python tests\validate_okf.py
python -m pytest tests\deployment -q
python -m py_compile scripts\check-movie-ci.py
```

On Linux or Git Bash:

```bash
bash -n scripts/deploy-movie-sync.sh
```

# Backend

Start an unauthenticated local MongoDB 8 instance on `localhost:27017`, then run
the same Release commands as `Movie CI`:

```powershell
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
dotnet test backend\Watchlist.sln --configuration Release --no-build
```

The expected suite is 167 Application tests and 39 API tests. Mongo repository
tests are part of the required result, not an optional timeout exclusion.

Lifecycle coverage must include rejected empty/duplicate source snapshots,
publish-last manifests, active/watched/reactivated transitions, active-only
reads, and coherent watched export.

# Worker

Run from `workers/vod-filter`:

```powershell
python -m pip install -r requirements.txt "pytest>=8.0.0"
python -m pytest -q
python -m compileall -q src continuous_sync.py sync_movies.py reconcile_sync.py healthcheck.py
```

The expected worker suite is 132 tests. It includes strict snapshot matching,
Radarr baseline/disappearance persistence, watched/manual planning, destructive
policy and executor checks, cleanup audit history, reports, and configuration.

# Containers

With placeholder `backend.env` and `worker.env` files in a temporary config
directory and a temporary data directory:

```powershell
$env:WATCHLIST_CONFIG_DIR="C:\path\to\test-config"
$env:WATCHLIST_DATA_DIR="C:\path\to\test-data"
$env:WATCHLIST_RELEASE="validation"
docker compose -f deploy\production\compose.yaml config --quiet
docker build -f backend\src\Watchlist.Api\Dockerfile -t watchlist-api:validation .
docker build -t watchlist-worker:validation workers\vod-filter
```

Verify image healthchecks, non-root users, backend `/healthz`, and a `401`
response from an unauthenticated `POST /api/sync/movies` when a sync key is set.

# Secrets

CI uses Gitleaks `v8.30.1` pinned by digest. Run both redacted scans against a
clean publishable checkout:

```powershell
docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:v8.30.1 git --redact --no-banner /repo
docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:v8.30.1 dir --redact --no-banner /repo
```

The publishable-tree scan must run from a clean exact-tree worktree so ignored
host secrets and local build output are absent by construction. Any confirmed
finding blocks integration, push, and deployment.

Local ignored `.env`, `appsettings.*.Local.json`, build output, and `.artifacts`
may contain real credentials and must remain ignored. A broad local directory
scan is not equivalent to a publishable-tree scan unless those paths are
excluded or the scan runs from a clean checkout.

# Android TV

Android TV is on hold. When a contract-preserving Android change is necessary,
run its separate workflow-equivalent Gradle tests and build as documented in
[Android TV Client](../systems/android_tv_client.md).
