---
type: Backlog
title: Production Movie Sync Implementation
description: TDD execution plan for the approved production movie sync architecture and homelab delivery.
tags:
  - movies
  - sync
  - worker
  - ci-cd
timestamp: 2026-07-11T00:00:00Z
version: 0.1.0
---

# Production Movie Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver safe unattended movie synchronization through one backend snapshot and one worker plan-and-apply engine, validated by GitHub Actions and deployed to the homelab with no GitHub-held production secrets.

**Architecture:** The backend performs the movie-only source sync and publishes one complete worker snapshot. The worker validates live backend/Radarr/Plex/SQLite state, creates deterministic decisions, applies only policy-permitted actions, and records outcomes. A CI-gated systemd deployer uses a clean production checkout and commit-tagged images with rollback.

**Tech Stack:** .NET 10 minimal API, MongoDB 8, Python 3.11, httpx, SQLite, pytest, Docker Compose, GitHub Actions, Bash, systemd.

---

### Task 1: Backend Movie-Only Sync And Authentication

**Files:**
- Create: `backend/src/Watchlist.Application/IMovieSyncService.cs`
- Create: `backend/src/Watchlist.Application/MovieSyncResultDto.cs`
- Create: `backend/src/Watchlist.Application/MovieSyncService.cs`
- Create: `backend/src/Watchlist.Api/SyncApiKeyFilter.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MovieSyncServiceTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [x] **Step 1: Write failing service tests**

Add tests proving the movie service calls Letterboxd, TMDB movies, and Plex
movies in order, excludes TMDB TV, and returns `partial` when TMDB enrichment
reports failed items.

```csharp
[Fact]
public async Task SyncAsync_RunsOnlyMovieStagesInOrder()
{
    List<string> calls = [];
    MovieSyncService service = CreateService(calls);

    MovieSyncResultDto result = await service.SyncAsync(CancellationToken.None);

    calls.Should().Equal("letterboxd", "tmdb_movies", "plex_movies");
    result.Status.Should().Be("completed");
}
```

- [x] **Step 2: Verify the service test fails**

Run: `dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter MovieSyncServiceTests`

Expected: compile failure because `MovieSyncService` does not exist.

- [x] **Step 3: Implement the movie-only orchestrator**

Use this public contract:

```csharp
public interface IMovieSyncService
{
    Task<MovieSyncResultDto> SyncAsync(CancellationToken cancellationToken);
}

public sealed record MovieSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    LetterboxdSyncResultDto Letterboxd,
    TmdbMovieEnrichmentResultDto TmdbMovies,
    PlexMovieSyncResultDto PlexMovies);
```

- [x] **Step 4: Write failing API-key tests**

Test `POST /api/sync/movies` with a configured key: missing/wrong keys return
`401`; the correct `X-Watchlist-Sync-Key` returns `200`. Preserve current test
behavior when no key is configured outside Production.

- [x] **Step 5: Implement constant-time sync-key validation and endpoint**

Register `IMovieSyncService`, map `POST /api/sync/movies`, and apply the same
filter to all mutation endpoints under `/api/sync`. Production startup must fail
when `Sync:ApiKey` is empty.

- [x] **Step 6: Verify focused and existing backend tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "MovieSyncServiceTests|FullyQualifiedName~CombinedSyncServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit**

```powershell
git add backend/src backend/tests
git commit -m "feat: add authenticated movie-only sync"
```

### Task 2: Complete Worker Movie Snapshot

**Files:**
- Create: `backend/src/Watchlist.Application/WorkerMovieDto.cs`
- Create: `backend/src/Watchlist.Application/WorkerMovieSnapshotDto.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistExportMovieModel.cs`
- Modify: `backend/src/Watchlist.Application/IWatchlistExportRepository.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistExportService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistExportRepository.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Test: `backend/tests/Watchlist.Application.Tests/WatchlistExportServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoWatchlistExportRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Write failing snapshot mapping tests**

Cover enriched/no-owned-service, enriched/owned-service, failed enrichment,
duplicate-free TMDB identity, and latest successful Plex-movie sync time.

```csharp
result.Movies.Should().ContainEquivalentOf(new
{
    TmdbId = 1297842,
    MetadataStatus = "enriched",
    RadarrEligible = true,
    RadarrEligibilityReason = "no_owned_service"
});
```

- [ ] **Step 2: Verify snapshot tests fail**

Run: `dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter "WatchlistExportServiceTests|MongoWatchlistExportRepositoryTests"`

Expected: compile/assertion failure for the missing snapshot API.

- [ ] **Step 3: Implement the snapshot DTO and mapping**

Use this shape:

```csharp
public sealed record WorkerMovieDto(
    int TmdbId,
    string? ImdbId,
    string Title,
    int? Year,
    string SourceId,
    string MetadataStatus,
    string AvailabilityStatus,
    IReadOnlyList<string> OwnedServiceAvailability,
    bool RadarrEligible,
    string RadarrEligibilityReason);

public sealed record WorkerMovieSnapshotDto(
    DateTimeOffset GeneratedAt,
    DateTimeOffset? LastSuccessfulMovieSyncAt,
    IReadOnlyList<WorkerMovieDto> Movies);
```

Only `metadataStatus == "enriched"` with no owned service is Radarr-eligible.
Malformed or absent TMDB IDs remain visible as invalid rows rather than being
silently turned into candidates.

- [ ] **Step 4: Map `GET /api/export/movies/sync-state`**

Return the complete Letterboxd movie set and use the latest
`plex_movies_completed` run as snapshot freshness evidence.

- [ ] **Step 5: Verify backend contracts**

Run all API tests and non-Mongo application tests. Then start local MongoDB and
run the full Application test project.

- [ ] **Step 6: Commit**

```powershell
git add backend/src backend/tests
git commit -m "feat: expose complete movie sync snapshot"
```

### Task 3: Pure Worker Planner And Safety Policy

**Files:**
- Modify: `workers/vod-filter/src/services/sync_reconciliation.py`
- Create: `workers/vod-filter/src/services/movie_sync_policy.py`
- Modify: `workers/vod-filter/tests/vod_filter/test_sync_reconciliation.py`
- Create: `workers/vod-filter/tests/vod_filter/test_movie_sync_policy.py`

- [ ] **Step 1: Write planner tests for desired state and ownership**

Cover Radarr add/keep/adopt/remove, downloaded-file removal skip, unrelated
Radarr preservation, Plex add/keep/remove, unrelated Plex preservation,
duplicate/missing IDs, empty-source protection, and failed collection.

```python
assert decision.action == "skip"
assert decision.reason == "downloaded_file_requires_manual_review"
```

- [ ] **Step 2: Run planner tests and confirm RED**

Run: `python -m pytest tests/vod_filter/test_sync_reconciliation.py tests/vod_filter/test_movie_sync_policy.py -q`

Expected: failures for ownership and policy behavior not yet implemented.

- [ ] **Step 3: Extend the planner without side effects**

Add explicit `managed` and `execution_status` fields. Planner inputs distinguish
complete backend movies, Radarr eligibility, live destination state, and worker
ownership. Never emit `remove` for unmanaged destination rows.

- [ ] **Step 4: Implement policy evaluation**

```python
@dataclass(frozen=True)
class SyncPolicy:
    max_source_age_minutes: int = 120
    max_removal_count: int = 10
    max_removal_percent: float = 25.0
    allow_mutation: bool = False

def evaluate_plan(report: SyncReconciliationReport, policy: SyncPolicy) -> list[str]:
    """Return blocking reason codes; an empty list permits apply."""
```

Block stale/empty/incomplete/ambiguous snapshots and excess removal volume.

- [ ] **Step 5: Verify planner and policy tests pass**

Run the two focused test files and then the full worker suite.

- [ ] **Step 6: Commit existing reconciliation work with the planner**

Stage the pre-existing reconciliation CLI/client/tests together with this task
after reviewing their diff; do not stage unrelated documentation.

```powershell
git add workers/vod-filter/reconcile_sync.py workers/vod-filter/src workers/vod-filter/tests/vod_filter
git commit -m "feat: plan safe movie synchronization"
```

### Task 4: Snapshot Collection And Managed Ownership

**Files:**
- Modify: `workers/vod-filter/src/clients/watchlist_app_client.py`
- Modify: `workers/vod-filter/src/config.py`
- Modify: `workers/vod-filter/src/models/schema.sql`
- Modify: `workers/vod-filter/src/services/cache_service.py`
- Create: `workers/vod-filter/src/services/movie_sync_collector.py`
- Test: `workers/vod-filter/tests/vod_filter/test_watchlist_app_client.py`
- Create: `workers/vod-filter/tests/vod_filter/test_movie_sync_collector.py`
- Create: `workers/vod-filter/tests/vod_filter/test_managed_destinations.py`

- [ ] **Step 1: Write failing client and collector tests**

Require `X-Watchlist-Sync-Key`, call `/api/sync/movies`, parse the complete
snapshot strictly, and preserve backend eligibility reasons. A failed source,
Radarr, Plex watchlist, or Plex library read must be represented as a blocking
collection error.

- [ ] **Step 2: Verify RED**

Run the three focused files. Expected: failures for missing snapshot and
ownership APIs.

- [ ] **Step 3: Implement config and backend client**

Add validated settings:

```text
WATCHLIST_APP_SYNC_KEY
MOVIE_SYNC_APPLY
MOVIE_SYNC_MAX_SOURCE_AGE_MINUTES
MOVIE_SYNC_MAX_REMOVAL_COUNT
MOVIE_SYNC_MAX_REMOVAL_PERCENT
```

Production `WATCHLIST_SOURCE=watchlist_app` requires the sync key. Do not log
its value.

- [ ] **Step 4: Add managed-destination persistence**

Create a migration-safe `managed_destinations` table keyed by
`(destination, tmdb_id)`, with first/last managed timestamps and last action.
Expose `get_managed_destinations`, `mark_managed`, and `release_managed`.

- [ ] **Step 5: Implement the collector and verify GREEN**

Use the existing Radarr/Plex clients only for reads. Return one immutable
snapshot object plus collection errors. Run focused and full worker tests.

- [ ] **Step 6: Commit**

```powershell
git add workers/vod-filter/src workers/vod-filter/tests/vod_filter
git commit -m "feat: collect authoritative movie sync state"
```

### Task 5: Executor, Reports, CLI, And Health

**Files:**
- Create: `workers/vod-filter/src/services/movie_sync_executor.py`
- Create: `workers/vod-filter/src/services/movie_sync_report.py`
- Create: `workers/vod-filter/sync_movies.py`
- Create: `workers/vod-filter/healthcheck.py`
- Modify: `workers/vod-filter/continuous_sync.py`
- Modify: `workers/vod-filter/reconcile_sync.py`
- Test: `workers/vod-filter/tests/vod_filter/test_movie_sync_executor.py`
- Create: `workers/vod-filter/tests/vod_filter/test_sync_movies_cli.py`
- Create: `workers/vod-filter/tests/vod_filter/test_worker_healthcheck.py`

- [ ] **Step 1: Write failing executor tests**

Prove dry-run calls no mutating client method; policy blockers call nothing;
adds/removals call only the intended endpoint; downloaded files and Plex library
are never deleted; successful actions update ownership; partial failures are
recorded and do not execute a stale retry plan.

- [ ] **Step 2: Verify RED**

Run the three new test files. Expected: import failure for missing executor/CLI.

- [ ] **Step 3: Implement executor and report formats**

Write `data/reports/movie-sync-<run-id>.json` and `.md`. JSON contains the full
machine-readable plan and outcomes; Markdown groups the shared decision
vocabulary for operator review. Never serialize config or request headers.

- [ ] **Step 4: Implement `sync_movies.py`**

CLI flags:

```text
--apply
--skip-backend-sync
--report-dir
--quiet
--log-level
```

Exit codes are `0` completed/no changes, `2` partial execution, `3` safety
blocked, and `1` configuration/collection failure.

- [ ] **Step 5: Switch continuous production execution**

`continuous_sync.py` invokes `sync_movies.main([])` once per interval. It writes
`data/last-run.json` atomically after every run. `healthcheck.py` validates the
heartbeat age and accepted last status.

- [ ] **Step 6: Verify focused and full worker tests**

Run all worker tests and `python -m compileall -q src *.py` from the worker root.

- [ ] **Step 7: Commit**

```powershell
git add workers/vod-filter
git commit -m "feat: apply and monitor movie sync plans"
```

### Task 6: Production Containers

**Files:**
- Modify: `backend/src/Watchlist.Api/Dockerfile`
- Modify: `workers/vod-filter/Dockerfile`
- Create: `deploy/production/compose.yaml`
- Create: `deploy/production/backend.env.example`
- Create: `deploy/production/worker.env.example`
- Modify: `.dockerignore`
- Modify: `.gitignore`

- [ ] **Step 1: Add container contract tests/checks**

Make backend health available to Compose, copy only production worker entry
points, run both containers as non-root users, and persist worker `/app/data`.

- [ ] **Step 2: Implement unified production Compose**

Use `${WATCHLIST_RELEASE}` image tags, server-local `${WATCHLIST_CONFIG_DIR}`
env files, backend health dependency, JSON log rotation, read-only root
filesystems where compatible, and `no-new-privileges`.

- [ ] **Step 3: Validate and build locally**

Create temporary non-secret test env files outside Git, run `docker compose
config --quiet`, build both images, start with test configuration, and inspect
container health.

- [ ] **Step 4: Commit**

```powershell
git add backend/src/Watchlist.Api/Dockerfile workers/vod-filter/Dockerfile deploy/production .dockerignore .gitignore
git commit -m "build: package production movie services"
```

### Task 7: Secret-Safe Movie CI

**Files:**
- Create: `.github/workflows/movie-ci.yml`
- Delete: `.github/workflows/backend-ci.yml`
- Delete: `.github/workflows/validate-okf.yml`
- Modify: `.github/workflows/android-ci.yml`

- [ ] **Step 1: Implement one required movie workflow**

Jobs: `okf`, `backend`, `worker`, `secret-scan`, and `containers`. Give
`backend` a MongoDB 8 service with health checks. Pin third-party actions to
immutable commit revisions and keep `permissions: contents: read`.

- [ ] **Step 2: Match local and CI commands**

Backend runs Release restore/build/full test. Worker runs Python 3.11 dependency
install, pytest, and compileall. Containers build only after code tests pass.
Secret scanning covers history and working tree with redacted output.

- [ ] **Step 3: Validate workflow syntax and local equivalents**

Parse YAML, execute every local test command, run the redacted secret scanner,
and build both Docker images.

- [ ] **Step 4: Commit**

```powershell
git add .github/workflows
git commit -m "ci: validate complete movie deployment"
```

### Task 8: CI-Gated Homelab Deployment And Rollback

**Files:**
- Create: `scripts/deploy-movie-sync.sh`
- Create: `scripts/check-movie-ci.py`
- Modify: `deploy/local-cd/systemd/watchlist-deploy.service`
- Modify: `deploy/local-cd/systemd/watchlist-deploy.timer`
- Modify: `deploy/local-cd/watchlist-deploy.env.example`
- Test: `tests/deployment/test_check_movie_ci.py`
- Test: `tests/deployment/test_deploy_script.py`

- [ ] **Step 1: Write failing CI-gate and shell-contract tests**

Test successful, pending, failed, and missing `Movie CI` API responses. Shell
tests assert use of `flock`, detached validated checkout, commit-tagged images,
health checks, last-successful release, rollback, and no environment-value
logging.

- [ ] **Step 2: Verify RED**

Run: `python -m pytest tests/deployment -q`

Expected: missing module/script failures.

- [ ] **Step 3: Implement public GitHub Actions gate**

`check-movie-ci.py` accepts repository, workflow filename, and commit SHA. Exit
`0` only for a completed successful push run for the exact SHA; use distinct
nonzero codes for pending and failed/missing runs.

- [ ] **Step 4: Implement deployment and rollback**

Use `/opt/watchlist-prod/repository`, `/opt/watchlist-prod/config`, and
`/opt/watchlist-prod/data`. Preserve `/opt/watchlist-app`. Build and start the
validated release, run smoke checks, atomically record the SHA, and restart the
previous release on failure. Retain two release image sets and prune stale
builder cache.

- [ ] **Step 5: Run systemd as the deployment user**

Set `User=watchlist`, `Group=watchlist`, restrictive umask, and explicit write
paths. Keep the five-minute timer and add randomized delay to avoid fixed-time
API bursts.

- [ ] **Step 6: Verify tests and shell syntax**

Run deployment pytest, `bash -n`, and a local dry-run using fake Docker/GitHub
command fixtures.

- [ ] **Step 7: Commit**

```powershell
git add scripts deploy/local-cd tests/deployment
git commit -m "deploy: gate homelab releases on movie ci"
```

### Task 9: OKF, Full Verification, Push, And Supervised Rollout

**Files:**
- Modify: `docs/architecture/sync_pipeline.md`
- Modify: `docs/systems/backend_service.md`
- Modify: `docs/systems/vod_filter_worker.md`
- Modify: `docs/systems/deployment_tooling.md`
- Modify: `docs/apis/backend_api.md`
- Modify: `docs/apis/export_endpoints.md`
- Modify: `docs/runbooks/homelab_cd.md`
- Modify: `docs/runbooks/vod_filter_operations.md`
- Modify: `docs/runbooks/validation.md`
- Modify: `docs/backlog/roadmap.md`
- Modify: `docs/log.md`

- [ ] **Step 1: Update OKF to implemented behavior**

Document exact endpoints, decision reasons, environment variable names, safety
limits, reports, CI workflow, deployment paths, rollback, and operator commands.
Mark completed roadmap entries and list remaining manual-review work.

- [ ] **Step 2: Run complete local verification**

```powershell
python tests\validate_okf.py
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
dotnet test backend\Watchlist.sln --configuration Release --no-build
python -m pytest workers\vod-filter\tests\vod_filter -q
python -m pytest tests\deployment -q
docker compose -f deploy\production\compose.yaml config --quiet
docker compose -f deploy\production\compose.yaml build
git diff --check
```

- [ ] **Step 3: Run redacted secret scans**

Scan the working tree and complete Git history. Report only rule, path, commit,
and redacted fingerprint. Do not continue to push if a real credential is found.

- [ ] **Step 4: Commit docs and push `main`**

Confirm all staged files match this plan, commit OKF updates, and push only after
fresh verification succeeds.

- [ ] **Step 5: Wait for exact-SHA Movie CI**

Poll the public workflow API until success. A failure returns to implementation;
it is not bypassed on the host.

- [ ] **Step 6: Prepare the host without exposing secrets**

Create `/opt/watchlist-prod`, copy the existing backend env and local worker env
over SSH, set mode `0600`, generate one sync API key directly on the host, and
write it to both env files without printing it. Install and daemon-reload the
systemd units.

- [ ] **Step 7: Deploy reconciliation-only**

Set `MOVIE_SYNC_APPLY=false`, run the deploy service manually, verify backend and
worker container health, and inspect the generated plan for blockers/removals.

- [ ] **Step 8: Enable and supervise safe apply**

Only when the report passes policy, set `MOVIE_SYNC_APPLY=true`, restart the
worker, run one sync, and verify that no file/library deletion endpoint was
called. Run a second sync and require only `keep`/`skip` or explained changes.

- [ ] **Step 9: Verify continuous delivery**

Confirm timer active, service successful, recorded release SHA equals GitHub
main, backend `/healthz` succeeds, sync status is current, worker heartbeat is
fresh, and rollback metadata names the prior release.

# Links

- [Production Movie Sync](../architecture/movie_sync_production.md)
- [Sync Correctness Priority](../decisions/sync_correctness_priority.md)
- [Validation](../runbooks/validation.md)
