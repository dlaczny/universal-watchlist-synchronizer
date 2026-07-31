---
type: Runbook
title: Validation
description: Local commands equivalent to the production movie CI and deployment checks.
tags:
  - validation
  - tests
  - okf
timestamp: 2026-07-31T00:00:00Z
version: 0.5.0
---

# OKF And Deployment Tooling

```powershell
python tests\validate_okf.py
python -m pytest tests\test_tv_destination_plan_docs.py -q
python -m pytest tests\deployment -q
python -m py_compile scripts\check-movie-ci.py
```

On Linux or Git Bash:

```bash
bash -n scripts/deploy-movie-sync.sh
```

# Backend

A local MongoDB 8 instance is required for backend validation; an older server,
mock, or in-memory substitute is not sufficient. Start it unauthenticated on
`localhost:27017`, then run the same Release commands as `Movie CI`:

```powershell
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
dotnet test backend\Watchlist.sln --configuration Release --no-build
```

Run the full Release solution rather than relying on historical test counts.
Mongo repository and TV generation tests are part of the required result, not
an optional timeout exclusion.

Lifecycle coverage must include rejected empty/duplicate source snapshots,
the first-write bootstrap manifest, publish-last operational manifests,
active/watched/reactivated transitions, active-only reads, and coherent watched
export.

TV coverage must include protected Trakt connection/key-ring restart behavior,
complete paginated reads, cursor-race rejection, publish-last generation
reads, provider `unknown`/`stale` behavior, legacy-row migration, TV browse
state validation, schema-v2 export serialization (`destinationSync` and
per-season `polandAvailability`), and all locked-false history/cleanup gates.
Scheduling tests must prove a six-hour scheduled full-sync interval and the
unchanged five-minute activity poll.

Retention planner and Mongo repository coverage must prove:

- the inclusive seven-day age boundary and the shared 48-generation cap,
  including the always-protected current generation;
- deterministic newest-`publishedAt`, then generation-ID ordering;
- exact orphan matching with `^tv-[0-9]{17}-[0-9a-f]{32}$`, an inclusive
  24-hour grace boundary, and rejection of orphan/manifest overlap;
- published-pointer/current-manifest consistency plus BSON scalar-type guards
  for pointer, manifest, `documentKind`, and `generationId`;
- fail-closed malformed manifests or pointers, preservation of legacy rows and
  malformed or uncertain physical identities, and current-pointer revalidation
  before delete;
- acknowledged child-first deletion of show rows, lifecycle events, then
  manifests, with idempotent retries and convergence.

TV orchestration and contract coverage must prove mandatory retention runs
under the coordinator after current-generation validation but before token or
source access. A mandatory failure must perform no token/source call and no
candidate, staging, or pointer write; it must preserve the readable current
generation and map to stable redacted API, hosted-service, and combined-sync
results. The direct endpoint mapping is HTTP `503` with
`tv_generation_retention_failed`; combined sync remains HTTP `200` partial,
preserves completed movie stages, and reports a failed,
non-mutation-capable TV result with that health reason. Best-effort
post-publication failure and cancellation must log
`tv_generation_retention_deferred`, preserve successful publication and its
response, and leave cleanup for the next mandatory pass.

# Worker

Run from `workers/vod-filter`:

```powershell
python -m pip install -r requirements.txt "pytest>=8.0.0"
python -m pytest -q
python -m compileall -q src continuous_sync.py sync_movies.py sync_tv.py reconcile_sync.py healthcheck.py
```

Run the full worker suite rather than relying on a historical test count. It
includes strict schema-v2 snapshot parsing, exact TVDB Sonarr and verified Plex
identity checks, selected-season/provider planning, SQLite origin/action audit,
report-only and separately armed adoption/apply policy, independent workflow
scheduling/health, and movie regression coverage.

The TV workflow simulation must cover unavailable unstarted Season 1,
provider-available skip, progress advance to a later aired season, exact Plex
library-driven add, exact Plex Watchlist removal when neither desired fact is
present, supervised manual-origin adoption, unknown/stale provider blocking,
and a second apply that converges to only `keep`/`skip` results. It must also
prove no Plex library, Trakt-history, movie, or Sonarr file/season/series
operation is exposed.

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
Also inspect resolved Compose configuration for the persistent backend key-ring
mount. The checked-in deployment must retain `TV_SYNC_APPLY=false`,
`TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false`, every history/cleanup setting
false, and no media-root mount. `TV_SYNC_ENABLED` is disabled by default in the
worker configuration; enabling collection on a host must not turn on apply.

When validating a release candidate, run the full gate in this order and retain
the actual output outside Git:

```powershell
python tests\validate_okf.py
python -m pytest tests\test_tv_destination_plan_docs.py -q
python -m pytest tests\deployment -q
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
dotnet test backend\Watchlist.sln --configuration Release --no-build
Push-Location workers\vod-filter
python -m pytest -q
python -m compileall -q src continuous_sync.py sync_movies.py sync_tv.py healthcheck.py
Pop-Location
```

Passing local validation proves only the build and test gates. Production
report-only, adoption, reversible apply, and convergence evidence belongs in
the rollout ledger after each real host stage.

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

Android TV validation is deferred with the Android backlog and must not be run
as active TV scope unless the user explicitly resumes Android work.
