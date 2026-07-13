---
type: Backlog
title: TV Integration Implementation Program
description: Ordered execution index and release gates for the approved Trakt, Plex, Sonarr, Android, and Polish-provider TV integration.
tags:
  - tv
  - trakt
  - plex
  - sonarr
  - android
  - rollout
timestamp: 2026-07-13T00:00:00Z
version: 0.1.0
---

# TV Integration Program Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved Trakt-backed TV experience in five independently gated implementation phases while preserving the production movie workflow and keeping every new external write or deletion disabled until its own supervised rollout.

**Architecture:** The .NET backend owns Trakt OAuth and source snapshots, configured-account Plex history ingestion, Trakt history delivery, TMDB enrichment, MongoDB lifecycle evidence, and cleanup authorization. The Python worker consumes one published TV generation, owns Sonarr and Plex universal-watchlist mutations, and persists destination ownership and action audit in SQLite. Android remains a read-only backend client. Each phase extends the same versioned contracts and must meet its exit criteria before the next phase is armed.

**Tech Stack:** .NET 10 minimal API, MongoDB 8, ASP.NET Data Protection, Trakt API, Plex API, TMDB API, Python 3.11, httpx, SQLite, Sonarr API v3, PlexAPI, Java Android TV, Gradle, Docker Compose, pytest, xUnit, and FluentAssertions.

---

## Program Documents

Execute these plans in order:

1. [Phase 1: TV Read Model](2026-07-13-tv-phase-1-read-model.md)
2. [Phase 2: Plex History To Trakt](2026-07-13-tv-phase-2-plex-trakt-history.md)
3. [Phase 3: Reversible Sonarr And Plex Destinations](2026-07-13-tv-phase-3-reversible-destinations.md)
4. [Phase 4: Concluded-Season Cleanup](2026-07-13-tv-phase-4-season-cleanup.md)
5. [Phase 5: Terminal-Series Cleanup And Revival](2026-07-13-tv-phase-5-terminal-cleanup.md)

This document is an execution index and release-checkpoint ledger, not a sixth
implementation stream. Perform each master task at the phase boundary that
creates its listed files: Task 1 verifies Phase 1 Task 12 ownership and is
rerun during Phase 3 parser work, Tasks 2 and 3 run continuously at phase
exits, Task 4 runs after Phase 5, and Task 5 runs at final release validation.
Phase 1 Task 16 solely creates the cumulative rollout ledger, Phase 1 Task 17
records its first exit, and Phase 2 Task 13 modifies and commits the Phase 2
entry before its exit gate. Later phases only modify that same ledger. Do not
try to edit a master-task file before its owning phase creates it.

The approved behavior and safety authority remains
[TV Show Integration Design](../specs/2026-07-13-tv-show-integration-design.md).
If an implementation choice conflicts with that design, stop and amend the
design through review before changing code.

### Task 1: Freeze Cross-Component Contract Versions

**Files:**
- Test/Verify: `contracts/tv/watchlist-browse-v1.json`
- Test/Verify: `contracts/tv/watchlist-detail-v1.json`
- Test/Verify: `contracts/tv/worker-sync-state-v1.json`
- Test/Verify: `contracts/tv/enums-v1.json`
- Test/Verify: `backend/tests/Watchlist.Api.Tests/TvAndroidContractTests.cs`
- Test/Verify: `backend/tests/Watchlist.Api.Tests/TvWorkerContractTests.cs`
- Test/Verify: `android/app/src/test/java/com/watchlist/tv/TvContractFixtureTest.java`
- Test/Verify: `workers/vod-filter/tests/vod_filter/test_tv_backend_client.py`

- [ ] **Step 1: Verify Phase 1 fixture ownership**

Phase 1 Task 12 is the sole creator and backend-serialization owner of these
four fixtures. This program task creates none of them; it verifies that
`schemaVersion: "1"` is used for the worker snapshot and preserves the existing
movie DTO fields. The fixtures are examples produced by real backend DTOs, not
an independent hand-maintained schema. Backend serialization tests own them;
Android and Python consume the same physical files.

The sole worker fixture uses string Plex library section IDs, timestamp
watermarks, positive `traktEpisodeId` on every episode, full last/next episode
objects, exact desired-state fields, and both cleanup authorization shapes. Do
not create a worker-local copy or a second Python-only schema.

- [ ] **Step 2: Add credential-shape rejection to every contract consumer**

Every backend, Android, and Python contract consumer uses one exact case-insensitive key
denylist: `token`, `accessToken`, `refreshToken`, `apiKey`, `api_key`, `password`, `secret`, `clientSecret`, `plexToken`, `sonarrApiKey`, `syncKey`, `mongoConnectionString`, `rawPath`, `mediaPath`, `media_path`, `filesystemPath`, `filesystem_path`, `responseBody`, and `response_body`. Tests inject every name at the root and at least two nested
dictionary/array depths. Fixtures contain stable external IDs and
redacted reason codes but no API keys, OAuth codes, tokens, full media paths,
or raw upstream response bodies.

- [ ] **Step 3: Run the focused contract tests**

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvAndroidContractTests|FullyQualifiedName~TvWorkerContractTests"

Push-Location android
.\gradlew.bat :app:testDebugUnitTest --tests "com.watchlist.tv.TvContractFixtureTest"
Pop-Location

Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_backend_client.py -q
Pop-Location
```

Expected after the owning phase implements each consumer: all contract tests
pass against the same fixture set. Before that phase, its focused test is
expected to fail for the missing parser or DTO rather than being skipped.

- [ ] **Step 4: Record the contract checkpoint**

Record the owning Phase 1 Task 12 commit SHA and the focused test results in the
phase-exit evidence. Do not create a second fixture commit from this program
index; later phases modify and retest the same physical files.

### Task 2: Enforce Phase Order And Entry Gates

**Files:**
- Modify: `docs/reports/tv_integration_rollout.md` (created by Phase 1 Task 16)
- Modify: `docs/runbooks/tv_sync_operations.md`
- Modify: `docs/backlog/roadmap.md`
- Modify: `docs/log.md`

- [ ] **Step 1: Record Phase 1 exit evidence**

Use the Phase 1 section of the cumulative rollout ledger created in Phase 1
Task 16. Phase 1 Task 17 must commit these facts before the Phase 2 gate is
evaluated; this program checkpoint does not create a substitute report.

Do not enter Phase 2 until all of these facts are recorded with UTC times and
the deployed commit SHA:

- Trakt device OAuth survives a backend restart through the persistent keyring;
- at least two complete hourly TV generations publish coherently;
- malformed, partial, or raced source reads leave the prior generation visible;
- Android renders active, caught-up, retired, unknown, and stale examples;
- legacy TV rows no longer act as membership or disappearance authority; and
- all six safety switches introduced/configured by Phase 1 are false;
  `TV_SYNC_ENABLED` does not exist until Phase 3 and is therefore not a Phase 1
  gate.

- [ ] **Step 2: Record Phase 2 exit evidence**

Phase 2 Task 13 must append and commit its integration-ledger entry before this
gate is evaluated. Do not defer the Phase 2 record until Phase 3 documentation
work.

Do not enter Phase 3 until the configured Plex account/library backfill is
complete, the ledger and watermark have been reviewed, bootstrap does not
replay historical rewatches, a supervised Trakt batch converges, and there are
no unexplained ambiguous, duplicate, quarantined, or dead-letter events.

- [ ] **Step 3: Record Phase 3 exit evidence**

Do not enter Phase 4 until report-only exact-TVDB destination plans have been
reviewed, existing destinations were adopted only through the explicit gate,
reversible apply converges twice, continuing caught-up shows remain monitored
in Sonarr, and all deletion switches remain false.

- [ ] **Step 4: Record Phase 4 exit evidence**

Do not enter Phase 5 until seven continuous days of eligible scheduled
generations have been observed, one supervised season cleanup has converged,
the exact Sonarr episode-file children and SQLite/backend audits agree, and
the terminal deletion switch remains false.

- [ ] **Step 5: Record Phase 5 exit evidence**

Do not declare normal operation until an additional seven-day terminal
report-only period has completed, the read-only path map and Sonarr recycle
bin have been verified, no more than one supervised terminal deletion has
converged, and revival behavior has been simulated and inspected.

### Task 3: Preserve Independent Safety Switches

**Files:**
- Test/Verify: `backend/tests/Watchlist.Application.Tests/TvOptionsTests.cs`
- Test/Verify: `backend/tests/Watchlist.Application.Tests/TvHistoryConfigurationTests.cs`
- Test/Verify: `tests/deployment/test_tv_phase1_deployment.py`
- Test/Verify: `tests/deployment/test_tv_history_config.py`
- Test/Verify: `workers/vod-filter/tests/vod_filter/test_config.py`
- Test/Verify: `tests/deployment/test_deploy_script.py`

- [ ] **Step 1: Write failing default-value tests**

Use phase-relative assertions. Phase 1 verifies the six names it places in
deployment examples; Phase 2 verifies the real backend history apply gate and
keeps committed history collection disabled; Phase 3 introduces
`TV_SYNC_ENABLED` and is the first point at which all seven names exist and can
be tested together:

```text
TRAKT_HISTORY_SYNC_APPLY
TV_SYNC_ENABLED
TV_SYNC_APPLY
TV_SYNC_ADOPT_EXISTING_DESTINATIONS
TV_SYNC_ALLOW_SEASON_FILE_DELETION
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE
```

`TV_SYNC_ENABLED` enables collection/reporting only. No test may infer a more
specific switch from a broader switch. The worker CLI's `--apply` is only a
per-run request: effective apply requires `--apply` and
`TV_SYNC_APPLY=true`, and the CLI can never override the false environment
gate.

- [ ] **Step 2: Run each phase's owning switch tests**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvOptionsTests"
python -m pytest tests\deployment\test_tv_phase1_deployment.py -q

dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvHistoryConfigurationTests"
python -m pytest tests\deployment\test_tv_history_config.py -q

Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_config.py -q
Pop-Location

python -m pytest tests\deployment\test_deploy_script.py -q
```

Expected: assertions fail until the phase owning each option introduces the
option with a safe default and deployment example. Do not run the Phase 3
Python parser assertion as a Phase 1 exit gate.

- [ ] **Step 3: Verify switch independence after every phase**

After each phase, rerun only the suites whose owning phase has landed; after
Phase 3, rerun the complete matrix after every phase. A production example may
document how to arm a switch, but its checked-in value remains `false`.

### Task 4: Run The Cross-Phase Workflow Simulation

**Files:**
- Modify/Test: `backend/tests/Watchlist.Application.Tests/TvLifecycleWorkflowSimulationTests.cs`
- Modify/Test: `workers/vod-filter/tests/vod_filter/test_tv_workflow_simulation.py`
- Test/Verify: `contracts/tv/worker-sync-state-v1.json`

Phase 3 owns creation of the worker simulation and Phase 5 Task 8 owns creation
of the backend simulation plus the terminal/revival RED cases. This final
checkpoint may add only cross-phase assertions to those two simulation files
after Phase 5 is green. It only reads/verifies the shared fixture; it never
modifies or commits `contracts/tv/worker-sync-state-v1.json`.

- [ ] **Step 1: Extend the green backend lifecycle scenario**

After all five phase-owned RED/GREEN slices pass, extend the existing
deterministic-clock scenario through this complete sequence:

```text
Trakt watchlist show
  -> Plex S01E01 play
  -> one confirmed Trakt history write
  -> caught_up
  -> newly aired S01E02
  -> active/reactivated
  -> concluded season authorization after observed grace
  -> ended/canceled terminal authorization after observed grace
  -> later status reversal or newly aired episode
  -> active or caught_up revival
```

Assert complete generations, stable lifecycle versions/event IDs, cancellation
on contradiction, and no authorization from source disappearance. The added
assertions must pass immediately against the completed Phase 5 implementation.

- [ ] **Step 2: Run the backend simulation and confirm GREEN**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvLifecycleWorkflowSimulationTests"
```

Expected: the complete backend lifecycle simulation passes. Any failure is a
release blocker and is fixed in the owning phase implementation or test.

- [ ] **Step 3: Extend the green worker workflow scenario**

Use the versioned fixture to cover exact Sonarr/Plex additions, caught-up Plex
removal only, new-episode monitoring and exact search, season child cleanup,
terminal exact-series cleanup, crash convergence, and revival. Assert every
unrelated movie reconciliation result is unchanged. Add only assertions that
pass against the completed phase-owned implementation; contract evolution and
its RED tests remain in the phase plans.

- [ ] **Step 4: Run the worker simulation and confirm GREEN**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_workflow_simulation.py -q
Pop-Location
```

Expected: the complete worker simulation passes against the Phase 5 version of
the shared fixture. Do not weaken or skip any phase-owned assertion.

- [ ] **Step 5: Commit only the final simulation extensions**

```powershell
git add backend/tests/Watchlist.Application.Tests/TvLifecycleWorkflowSimulationTests.cs workers/vod-filter/tests/vod_filter/test_tv_workflow_simulation.py
git commit -m "test(tv): cover the complete TV lifecycle workflow"
```

The fixture is test input at this checkpoint. Never stage it opportunistically;
Phase 5 Task 2 owns its terminal-candidate contract modification and commit.

### Task 5: Execute Full Release Validation

**Files:**
- Modify: `docs/runbooks/validation.md`
- Modify: `docs/reports/tv_integration_rollout.md`

- [ ] **Step 1: Validate OKF and repository formatting**

```powershell
python tests\validate_okf.py
git diff --check
```

Expected: both commands exit `0`.

- [ ] **Step 2: Validate the backend with MongoDB 8 available on localhost**

```powershell
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
dotnet test backend\Watchlist.sln --configuration Release --no-build
```

Expected: restore, build, and every backend/API test pass.

- [ ] **Step 3: Validate the worker and deployment tools**

```powershell
Push-Location workers\vod-filter
python -m pytest -q
python -m compileall -q src continuous_sync.py sync_movies.py sync_tv.py reconcile_sync.py healthcheck.py
Pop-Location

python -m pytest tests\deployment -q
python -m py_compile scripts\check-movie-ci.py
```

Expected: all tests pass and Python compilation emits no output.

- [ ] **Step 4: Validate Android**

```powershell
Push-Location android
.\gradlew.bat :app:testDebugUnitTest :app:assembleDebug
Pop-Location
```

Expected: Gradle reports `BUILD SUCCESSFUL`.

- [ ] **Step 5: Validate production Compose and images**

Create real temporary non-secret env files and source directories, then render
the host TV source as read-only inside the worker:

```powershell
$validationRoot = Join-Path $env:TEMP "watchlist-tv-release-validation"
$configDir = Join-Path $validationRoot "config"
$dataDir = Join-Path $validationRoot "data"
$tvRoot = Join-Path $validationRoot "tv-media"
New-Item -ItemType Directory -Force $configDir, "$dataDir/backend/data-protection-keys", "$dataDir/worker", $tvRoot | Out-Null
Copy-Item deploy/production/backend.env.example "$configDir/backend.env"
Copy-Item deploy/production/worker.env.example "$configDir/worker.env"
$env:WATCHLIST_CONFIG_DIR = $configDir
$env:WATCHLIST_DATA_DIR = $dataDir
$env:WATCHLIST_TV_MEDIA_HOST_ROOT = $tvRoot
$env:WATCHLIST_RUNTIME_UID = "10001"
$env:WATCHLIST_RUNTIME_GID = "10001"
$env:WATCHLIST_RELEASE = "validation"
docker compose -f deploy\production\compose.yaml config --quiet
docker build -f backend\src\Watchlist.Api\Dockerfile -t watchlist-api:tv-validation .
docker build -t watchlist-worker:tv-validation workers\vod-filter
Remove-Item Env:WATCHLIST_CONFIG_DIR, Env:WATCHLIST_DATA_DIR, Env:WATCHLIST_TV_MEDIA_HOST_ROOT, Env:WATCHLIST_RUNTIME_UID, Env:WATCHLIST_RUNTIME_GID, Env:WATCHLIST_RELEASE
Remove-Item -Recurse -Force $validationRoot
```

Expected: Compose validation and both builds exit `0`; the rendered backend
has only its keyring writable and the TV media mapping is read-only.

- [ ] **Step 6: Record exact evidence and commit standing documentation**

Update the validation counts, command outputs, deployed commit SHA, generation
ID, gate values, supervised action IDs, and redacted convergence evidence.

```powershell
git add docs
git commit -m "docs(tv): record TV integration validation and rollout"
```
