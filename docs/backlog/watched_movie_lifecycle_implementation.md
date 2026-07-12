---
type: Backlog
title: Watched Movie Lifecycle Implementation
description: TDD implementation plan for durable Letterboxd watched transitions, Radarr file cleanup, and Plex watchlist convergence.
tags:
  - movies
  - letterboxd
  - radarr
  - plex
  - implementation
timestamp: 2026-07-12T00:00:00Z
version: 0.1.0
---

# Watched Movie Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist disappearance from a valid non-empty Letterboxd snapshot as a watched lifecycle transition, remove exact watched identities from Radarr with files and from the Plex watchlist, and remove Plex-watchlist rows after separately observed manual Radarr removals.

**Architecture:** MongoDB retains each Letterboxd movie and its lifecycle events. One immutable published source manifest is the authority for active membership and prevents partial writes from authorizing deletion. The worker consumes active and watched identities, tracks successful Radarr observations in SQLite, creates explicit destructive decisions, applies policy gates, and records every outcome without directly mutating Plex library media.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, MongoDB 8 standalone-safe publish manifests, C# xUnit/FluentAssertions, Python 3.11, SQLite, pytest, httpx, PlexAPI, pyarr, Docker Compose, GitHub Actions, systemd.

---

### Task 1: Reject Invalid Source Snapshots And Return Publication Identity

**Files:**
- Create: `backend/src/Watchlist.Application/LetterboxdSnapshotRejectedException.cs`
- Create: `backend/src/Watchlist.Application/LetterboxdMovieSyncApplyResult.cs`
- Create: `backend/src/Watchlist.Application/LetterboxdSyncGate.cs`
- Modify: `backend/src/Watchlist.Application/LetterboxdSyncResultDto.cs`
- Modify: `backend/src/Watchlist.Application/IWatchlistWriteRepository.cs`
- Modify: `backend/src/Watchlist.Application/LetterboxdMovieSyncService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Test: `backend/tests/Watchlist.Application.Tests/LetterboxdMovieSyncServiceTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [x] **Step 1: Write failing service tests for rejected snapshots**

Add tests proving that an empty result, duplicate source IDs, a non-positive
source ID, or an empty title throws `LetterboxdSnapshotRejectedException` and
never calls the write repository.

```csharp
[Fact]
public async Task SyncAsync_WhenSourceIsEmpty_RejectsWithoutWriting()
{
    FakeWatchlistWriteRepository repository = new([]);
    LetterboxdMovieSyncService service = CreateService(
        new FakeLetterboxdWatchlistClient([]),
        repository);

    Func<Task> action = () => service.SyncAsync(CancellationToken.None);

    await action.Should().ThrowAsync<LetterboxdSnapshotRejectedException>();
    repository.ApplyCallCount.Should().Be(0);
}
```

- [x] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter LetterboxdMovieSyncServiceTests
```

Expected: compile or assertion failures for the missing exception, apply result,
and validation.

- [x] **Step 3: Define the source publication result contract**

Use these application contracts:

```csharp
public sealed record LetterboxdMovieSyncApplyResult(
    string SourceSnapshotId,
    int ItemsMarkedWatched);

public sealed record LetterboxdSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ItemsFetched,
    int ItemsUpserted,
    int ItemsMarkedWatched,
    string SourceSnapshotId);
```

Change `ApplyLetterboxdMovieSyncAsync` to return
`LetterboxdMovieSyncApplyResult`. Keep all validation before `GetItemsAsync` or
any write call. Serialize concurrent source refreshes inside the single backend
process with this injected singleton gate:

```csharp
public sealed class LetterboxdSyncGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<T> RunAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            gate.Release();
        }
    }
}
```

Register `LetterboxdSyncGate` as a singleton and wrap the complete fetch,
validate, compare, and publish sequence.

- [x] **Step 4: Map source rejection to a non-success HTTP response**

Catch `LetterboxdSnapshotRejectedException` at the Letterboxd and movie-sync
HTTP boundaries and return `502` problem details without source rows or config
values. Add API tests for `POST /api/sync/letterboxd` and
`POST /api/sync/movies`.

- [x] **Step 5: Verify GREEN and existing orchestration compatibility**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "LetterboxdMovieSyncServiceTests|MovieSyncServiceTests|CombinedSyncServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~Sync"
```

Expected: all selected tests pass and rejected snapshots execute no repository
write.

- [x] **Step 6: Commit the source contract**

```powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure/DependencyInjection.cs backend/src/Watchlist.Api/Program.cs backend/tests
git commit -m "feat: reject invalid Letterboxd snapshots"
```

### Task 2: Persist Lifecycle Events Behind A Published MongoDB Manifest

**Files:**
- Create: `backend/src/Watchlist.Application/LetterboxdSourceSnapshot.cs`
- Create: `backend/src/Watchlist.Application/PublishedWatchedMovie.cs`
- Create: `backend/src/Watchlist.Application/ILetterboxdSourceSnapshotRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoMovieLifecycleEventDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoPublishedWatchedMovieDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoLetterboxdSourceSnapshotDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoLetterboxdSourceSnapshotRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoDbOptions.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistWriteRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/appsettings.json`
- Modify: `backend/src/Watchlist.Api/appsettings.Development.json`
- Modify: `backend/src/Watchlist.Api/appsettings.Development.Local.example.json`
- Test: `backend/tests/Watchlist.Application.Tests/MongoWatchlistWriteRepositoryTests.cs`
- Create: `backend/tests/Watchlist.Application.Tests/MongoLetterboxdSourceSnapshotRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoDbOptionsTests.cs`

- [x] **Step 1: Write failing MongoDB lifecycle tests**

Cover initial `added`, unchanged active, `watched`, stable watched,
`reactivated`, and second watched transitions. Assert that documents are retained,
versions increase only on transitions, and events contain unique IDs and source
snapshot IDs.

```csharp
result.ItemsMarkedWatched.Should().Be(1);
stored.LifecycleEvents.Select(item => item.EventType)
    .Should().Equal("added", "watched", "reactivated");
stored.LifecycleVersion.Should().Be(3);
```

Add a failure-path test in which a document update throws before manifest
insertion. The newest published snapshot must remain unchanged and the failed
generation must not become authoritative.

- [x] **Step 2: Run the MongoDB tests and verify RED**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "MongoWatchlistWriteRepositoryTests|MongoLetterboxdSourceSnapshotRepositoryTests|MongoWatchlistItemDocumentTests|MongoDbOptionsTests"
```

Expected: compile failures for the lifecycle and manifest types.

- [x] **Step 3: Add lifecycle and manifest documents**

Use this persisted shape:

```csharp
public sealed class MongoMovieLifecycleEventDocument
{
    public string EventId { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string SourceSnapshotId { get; init; } = string.Empty;
    public long LifecycleVersion { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public sealed class MongoLetterboxdSourceSnapshotDocument
{
    [BsonId]
    public string Id { get; init; } = string.Empty;
    public DateTimeOffset PublishedAt { get; init; }
    public IReadOnlyList<string> SourceIds { get; init; } = [];
    public IReadOnlyList<MongoPublishedWatchedMovieDocument> WatchedMovies { get; init; } = [];
    public int ItemCount { get; init; }
}
```

The corresponding application model is:

```csharp
public sealed record PublishedWatchedMovie(
    string SourceId,
    string LifecycleEventId,
    DateTimeOffset WatchedAt,
    long LifecycleVersion);

public sealed record LetterboxdSourceSnapshot(
    string SnapshotId,
    DateTimeOffset PublishedAt,
    IReadOnlySet<string> SourceIds,
    IReadOnlyList<PublishedWatchedMovie> WatchedMovies);
```

Add `LastSeenInSourceAt`, `LastWatchedAt`, `LifecycleVersion`, and
`LifecycleEvents` to `MongoWatchlistItemDocument`. Add
`LetterboxdSourceSnapshotsCollectionName` with default
`letterboxd_source_snapshots`.

- [x] **Step 4: Implement publish-after-complete application**

Read the latest manifest first. With no manifest, treat all existing
Letterboxd movie documents as the initial active set and use an empty watched
set. Generate a unique snapshot ID, perform every upsert/event update tagged
with it, carry forward still-watched states, remove reactivated states, add new
watched states, then insert the complete immutable manifest last. Never call
`DeleteManyAsync` for missing Letterboxd movies.

The repository returns:

```csharp
return new LetterboxdMovieSyncApplyResult(snapshotId, watchedCount);
```

The manifest repository returns the newest manifest by `PublishedAt` and `Id`.
An absent manifest is valid only during migration and means all current
Letterboxd documents are active.

- [x] **Step 5: Verify lifecycle persistence and migration behavior**

Run the focused MongoDB tests, then:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "LetterboxdMovieSyncServiceTests|MongoWatchlistWriteRepositoryTests|MongoLetterboxdSourceSnapshotRepositoryTests|MongoWatchlistItemDocumentTests|MongoDbOptionsTests"
```

Expected: all selected tests pass; a removed movie remains in MongoDB with a
published watched event.

- [x] **Step 6: Commit MongoDB lifecycle persistence**

```powershell
git add backend/src backend/tests/Watchlist.Application.Tests
git commit -m "feat: persist watched movie lifecycle"
```

### Task 3: Expose Only Published Active State And Export Watched Authorizations

**Files:**
- Create: `backend/src/Watchlist.Application/WorkerWatchedMovieDto.cs`
- Modify: `backend/src/Watchlist.Application/WorkerMovieSnapshotDto.cs`
- Modify: `backend/src/Watchlist.Application/IWatchlistExportRepository.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistExportService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistReadRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistExportRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTmdbMovieMetadataRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoPlexMovieInventoryRepository.cs`
- Create: `backend/tests/Watchlist.Application.Tests/MongoWatchlistReadRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoWatchlistExportRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTmdbMovieMetadataRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoPlexMovieInventoryRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/WatchlistExportServiceTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [x] **Step 1: Write failing active-filter and snapshot tests**

Prove that a watched document is absent from browse, details, TMDB enrichment,
Plex matching, Radarr compatibility export, and `movies`, while it remains in
`watchedMovies`. Prove that no manifest preserves legacy behavior by treating
existing documents as active.

- [x] **Step 2: Verify RED**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "WatchlistExportServiceTests|MongoWatchlistReadRepositoryTests|MongoWatchlistExportRepositoryTests|MongoTmdbMovieMetadataRepositoryTests|MongoPlexMovieInventoryRepositoryTests"
```

Expected: watched documents still appear in active reads and the snapshot lacks
the new contract.

- [x] **Step 3: Implement active membership from the published manifest**

Every active repository first reads the latest source manifest and adds its
source-ID membership filter. Preserve all non-Letterboxd rows. Do not trust a
mutable document status field as deletion authorization.

Use this worker contract:

```csharp
public sealed record WorkerWatchedMovieDto(
    int? TmdbId,
    string? ImdbId,
    string Title,
    int? Year,
    string SourceId,
    DateTimeOffset WatchedAt,
    long LifecycleVersion,
    string LifecycleEventId);

public sealed record WorkerMovieSnapshotDto(
    string SourceSnapshotId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? LastSuccessfulMovieSyncAt,
    IReadOnlyList<WorkerMovieDto> Movies,
    IReadOnlyList<WorkerWatchedMovieDto> WatchedMovies);
```

The latest manifest's complete watched set selects the retained documents and
their authorizing event IDs. Existing watched documents without TMDB IDs remain
visible with `TmdbId=null`. The export repository reads the manifest once and
derives active and watched rows from that same published snapshot.

- [x] **Step 4: Verify API and repository GREEN**

Run all focused tests and the full API test project.

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "WatchlistExportServiceTests|MongoWatchlistReadRepositoryTests|MongoWatchlistExportRepositoryTests|MongoTmdbMovieMetadataRepositoryTests|MongoPlexMovieInventoryRepositoryTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj
```

- [x] **Step 5: Commit published active-state reads**

```powershell
git add backend/src backend/tests
git commit -m "feat: export watched movie authorizations"
```

### Task 4: Parse The Published Worker Contract And Track Radarr Observations

**Files:**
- Modify: `workers/vod-filter/src/clients/watchlist_app_client.py`
- Modify: `workers/vod-filter/src/models/schema.sql`
- Modify: `workers/vod-filter/src/services/cache_service.py`
- Modify: `workers/vod-filter/src/services/movie_sync_collector.py`
- Test: `workers/vod-filter/tests/vod_filter/test_watchlist_app_client.py`
- Test: `workers/vod-filter/tests/vod_filter/test_movie_sync_collector.py`
- Create: `workers/vod-filter/tests/vod_filter/test_radarr_observations.py`

- [x] **Step 1: Write failing client contract tests**

Test that `_sync_movies` returns the nested Letterboxd source snapshot ID, the
following export must contain the same ID, and `watchedMovies` is strictly
mapped. Missing IDs, mismatches, malformed timestamps, and malformed lifecycle
events raise `WatchlistAppError`.

```python
snapshot = client.fetch_movie_sync_snapshot(sync_first=True)
assert snapshot["source_snapshot_id"] == "letterboxd-42"
assert snapshot["watched_movies"][0]["lifecycle_event_id"] == "movie:42:watched"
```

- [x] **Step 2: Write failing SQLite observation tests**

Cover migration-safe initialization, baseline-only first observation, later
manual disappearance, active-source disappearance, watched disappearance,
worker-marked removal, Radarr reappearance, and persistence across restart.

Use observation states `manual`, `active_source`, and `watched`, with
`present=false` only after a successful complete Radarr collection.

- [x] **Step 3: Verify RED**

Run:

```powershell
python -m pytest workers/vod-filter/tests/vod_filter/test_watchlist_app_client.py workers/vod-filter/tests/vod_filter/test_movie_sync_collector.py workers/vod-filter/tests/vod_filter/test_radarr_observations.py -q
```

Expected: failures for missing source IDs, watched rows, and observation APIs.

- [x] **Step 4: Add migration-safe SQLite tables and methods**

Add:

```sql
CREATE TABLE IF NOT EXISTS radarr_observation_state (
    singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
    initialized BOOLEAN NOT NULL DEFAULT 0,
    updated_at TIMESTAMP
);

CREATE TABLE IF NOT EXISTS radarr_observations (
    tmdb_id INTEGER PRIMARY KEY,
    title TEXT NOT NULL,
    year INTEGER,
    present BOOLEAN NOT NULL,
    disappearance_cause TEXT,
    source_event_id TEXT,
    first_seen_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_seen_at TIMESTAMP,
    last_transition_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS movie_cleanup_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    authorization TEXT NOT NULL,
    authorization_event_id TEXT,
    destination TEXT NOT NULL,
    tmdb_id INTEGER NOT NULL,
    delete_files BOOLEAN NOT NULL DEFAULT 0,
    status TEXT NOT NULL,
    error TEXT,
    attempted_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
```

Implement this cache boundary:

```python
def observe_radarr_movies(
    self,
    movies: list[dict[str, object]],
    active_tmdb_ids: set[int],
    watched_events_by_tmdb: dict[int, str],
) -> list[dict[str, object]]:
    """Persist one successful full Radarr observation and return all states."""
```

Also implement `get_radarr_observations`,
`mark_radarr_removed_by_worker`, and `record_cleanup_attempt`. A failed Radarr
read must never advance observation state.

- [x] **Step 5: Extend collector state**

Add `source_snapshot_id`, `backend_watched_movies`, and
`radarr_observations` to `CollectedMovieSyncState`. After successful backend and
Radarr reads, classify disappearance using active and watched TMDB sets. The
first successful read is baseline-only.

- [x] **Step 6: Verify GREEN and commit**

Run the three focused files and the managed-destination migration tests, then:

```powershell
git add workers/vod-filter/src workers/vod-filter/tests/vod_filter
git commit -m "feat: track Radarr disappearance state"
```

### Task 5: Plan Watched And Manual Cleanup With Explicit Authorization

**Files:**
- Modify: `workers/vod-filter/src/services/sync_reconciliation.py`
- Modify: `workers/vod-filter/src/services/movie_sync_policy.py`
- Test: `workers/vod-filter/tests/vod_filter/test_sync_reconciliation.py`
- Test: `workers/vod-filter/tests/vod_filter/test_movie_sync_policy.py`
- Test: `workers/vod-filter/tests/vod_filter/test_movie_workflow_simulation.py`

- [x] **Step 1: Write failing watched planner tests**

Cover watched Radarr removal with a downloaded file, watched Plex removal while
present in Plex library, unmanaged destination removal, absent-target skips,
missing watched TMDB reporting, active/watched identity conflict, and
reactivation cancellation.

```python
decision = find_decision(report, "radarr", "remove", tmdb_id=101)
assert decision.reason == "watched_letterboxd_movie_remove_from_radarr"
assert decision.delete_files is True
assert decision.authorization == "letterboxd_watched"
assert decision.authorization_event_id == "movie-101:watched:2"
```

- [x] **Step 2: Write failing manual-removal planner tests**

Prove that an absent `manual` Radarr observation suppresses and removes the
exact Plex-watchlist identity despite Plex-library membership. Prove that it
never creates a Radarr decision and that an active Letterboxd identity is not
suppressed.

- [x] **Step 3: Extend immutable decision types**

Add lifecycle fields to `ReconciliationMovie`, plus these fields to
`ReconciliationDecision`:

```python
delete_files: bool = False
authorization: str | None = None
authorization_event_id: str | None = None
```

Add `source_snapshot_id` to `SyncReconciliationReport` and watched/manual counts
to `source_counts`.

- [x] **Step 4: Implement precedence and stable reason codes**

Watched and manual suppression is applied before normal Radarr/Plex desired
state. Watched exact IDs override downloaded-file, Plex-library, and worker
ownership protection. Manual Radarr removal overrides only Plex-watchlist
protection. Missing watched TMDB emits `watched_movie_missing_tmdb_identity`
without authorizing mutation.

- [x] **Step 5: Add policy defense in depth**

Extend `SyncPolicy`:

```python
allow_watched_file_deletion: bool = False
```

Emit `watched_file_deletion_disabled` when a plan contains an authorized
`delete_files=true` decision while the gate is false. Emit
`invalid_file_deletion_authorization` for any file-delete decision that is not
an exact Radarr remove with `authorization="letterboxd_watched"` and a non-empty
event ID. Keep count and percentage gates unchanged.

- [x] **Step 6: Verify planner and policy GREEN**

Run:

```powershell
python -m pytest workers/vod-filter/tests/vod_filter/test_sync_reconciliation.py workers/vod-filter/tests/vod_filter/test_movie_sync_policy.py workers/vod-filter/tests/vod_filter/test_movie_workflow_simulation.py -q
```

- [x] **Step 7: Commit the authorized cleanup plan**

```powershell
git add workers/vod-filter/src/services workers/vod-filter/tests/vod_filter
git commit -m "feat: plan watched movie cleanup"
```

### Task 6: Execute And Report Destructive Decisions Safely

**Files:**
- Modify: `workers/vod-filter/src/config.py`
- Modify: `workers/vod-filter/sync_movies.py`
- Modify: `workers/vod-filter/src/services/movie_sync_executor.py`
- Modify: `workers/vod-filter/src/services/movie_sync_report.py`
- Modify: `deploy/production/worker.env.example`
- Modify: `workers/vod-filter/example.env`
- Test: `workers/vod-filter/tests/vod_filter/test_config.py`
- Test: `workers/vod-filter/tests/vod_filter/test_movie_sync_executor.py`
- Test: `workers/vod-filter/tests/vod_filter/test_sync_movies_cli.py`
- Test: `workers/vod-filter/tests/vod_filter/test_run_history.py`

- [x] **Step 1: Write failing executor defense tests**

Prove exact watched Radarr decisions call
`remove_movie(tmdb_id, delete_files=True)`, ordinary removal remains false,
invalid destructive authorization raises without a client call, Plex cleanup
never calls a library method, independent failures continue, and SQLite records
success/error attempts.

- [x] **Step 2: Write failing config and report tests**

Assert `MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION` defaults false, parses true, is
passed into policy, and appears in neither `Config.__repr__` nor reports. JSON
and Markdown reports must include snapshot ID, authorization, event ID, and
`delete_files`.

- [x] **Step 3: Verify RED**

Run:

```powershell
python -m pytest workers/vod-filter/tests/vod_filter/test_config.py workers/vod-filter/tests/vod_filter/test_movie_sync_executor.py workers/vod-filter/tests/vod_filter/test_sync_movies_cli.py workers/vod-filter/tests/vod_filter/test_run_history.py -q
```

- [x] **Step 4: Implement executor checks and observation updates**

Use `decision.delete_files` in the Radarr client call only after checking the
authorization invariant. After successful watched removal, mark the Radarr
observation absent with its lifecycle event ID. Release managed ownership when
present. Record every authorized cleanup attempt without storing credentials.

- [x] **Step 5: Wire configuration and orchestration**

Pass `state.backend_watched_movies`, `state.radarr_observations`, and
`state.source_snapshot_id` into reconciliation. Construct policy with:

```python
allow_watched_file_deletion=config.movie_sync_allow_watched_file_deletion
```

Add `MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION=false` to tracked examples only.

- [x] **Step 6: Verify GREEN and full worker regression suite**

Run focused tests, then:

```powershell
python -m pytest workers/vod-filter/tests/vod_filter -q
python -m compileall -q workers/vod-filter/src workers/vod-filter/sync_movies.py workers/vod-filter/continuous_sync.py workers/vod-filter/healthcheck.py
```

- [x] **Step 7: Commit execution and reporting**

```powershell
git add workers/vod-filter deploy/production/worker.env.example
git commit -m "feat: execute watched cleanup safely"
```

### Task 7: Update OKF And Production Validation Contracts

**Files:**
- Modify: `docs/architecture/movie_sync_production.md`
- Modify: `docs/architecture/sync_pipeline.md`
- Modify: `docs/apis/backend_api.md`
- Modify: `docs/apis/export_endpoints.md`
- Modify: `docs/data_models/watchlist_item.md`
- Modify: `docs/data_models/sync_run.md`
- Modify: `docs/integrations/letterboxd.md`
- Modify: `docs/integrations/plex.md`
- Modify: `docs/systems/backend_service.md`
- Modify: `docs/systems/vod_filter_worker.md`
- Modify: `docs/runbooks/agent_onboarding.md`
- Modify: `docs/runbooks/vod_filter_operations.md`
- Modify: `docs/runbooks/validation.md`
- Modify: `docs/backlog/roadmap.md`
- Modify: `docs/reports/okf_cleanup_report.md`
- Modify: `docs/log.md`

- [ ] **Step 1: Update OKF from future design to implemented behavior**

Document source publication, lifecycle fields, snapshot JSON, exact reason
codes, SQLite observation state, destructive authorization, feature gate,
removal limits, reports, failure recovery, and operator checks. Replace the old
blanket downloaded-file prohibition with the narrow watched exception while
retaining all ordinary and Plex-library protections.

- [ ] **Step 2: Mark implementation progress accurately**

Check completed tasks in this plan only after their verification commands pass.
Move the roadmap item to completed only after supervised production rollout.
Record that pre-feature watched history is not backfilled.

- [ ] **Step 3: Validate OKF and tracked examples**

Run:

```powershell
python tests\validate_okf.py
git diff --check
```

Expected: OKF validation passes and no whitespace errors are reported.

- [ ] **Step 4: Commit documentation**

```powershell
git add docs
git commit -m "docs: document watched movie cleanup"
```

### Task 8: Full Verification, Review, Integration, And CI

**Files:**
- Review: all files changed by Tasks 1-7
- Test: `.github/workflows/movie-ci.yml` through its local equivalent commands

- [ ] **Step 1: Run complete backend verification**

Start MongoDB 8 on `localhost:27017`, then run:

```powershell
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
dotnet test backend\Watchlist.sln --configuration Release --no-build
```

Expected: restore, Release build, Application tests, and API tests all pass with
zero failures.

- [ ] **Step 2: Run complete worker and deployment verification**

```powershell
python -m pytest workers/vod-filter/tests/vod_filter -q
python -m pytest tests/deployment -q
python -m compileall -q workers/vod-filter/src workers/vod-filter/continuous_sync.py workers/vod-filter/sync_movies.py workers/vod-filter/reconcile_sync.py workers/vod-filter/healthcheck.py
python tests/validate_okf.py
docker compose -f deploy/production/compose.yaml config --quiet
docker build -f backend/src/Watchlist.Api/Dockerfile -t watchlist-api:watched-validation .
docker build -t watchlist-worker:watched-validation workers/vod-filter
git diff --check
```

- [ ] **Step 3: Run redacted secret scans**

Run Gitleaks `v8.30.1` against full Git history and a clean exact-tree worktree:

```powershell
docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:v8.30.1 git --redact --no-banner /repo
docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:v8.30.1 dir --redact --no-banner /repo
```

Do not print secret values. Any confirmed finding blocks integration and push.

- [ ] **Step 4: Review behavior against the approved design**

Verify line by line that empty source results do nothing, only published
watched events delete files, never-Letterboxd identities are not deleted from
Radarr, manual Radarr disappearance suppresses Plex watchlist, reactivation
cancels cleanup, limits remain active, and Plex library methods remain read-only.

- [ ] **Step 5: Integrate into local `main` and push**

After all checks pass, merge the feature branch without rewriting existing
history, verify local `main` is clean, and push `main`. Do not push a failing or
unreviewed tree.

- [ ] **Step 6: Require exact-SHA Movie CI**

Poll the public workflow for the pushed `main` SHA and require every Movie CI
job to succeed. Do not bypass the gate on the homelab.

### Task 9: Guarded Homelab Rollout

**Files:**
- Host-only: `/opt/watchlist-prod/config/worker.env`
- Host-only: `/opt/watchlist-prod/data/worker/vod-filter.db`
- Host-only: `/opt/watchlist-prod/data/worker/reports/`
- Verify: `/opt/watchlist-prod/state/last-successful.sha`

- [ ] **Step 1: Let exact-SHA deployment install the release with deletion gated**

Keep `MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION=false`. Require the deploy timer to
select the exact successful CI SHA, build both images, obtain a fresh worker
heartbeat, and record the release.

- [ ] **Step 2: Review the first migration reconciliation**

Require a non-empty published source snapshot, zero active/watched conflicts,
no collection errors, a baseline-only Radarr observation state, and no
unexpected `delete_files=true` decisions. Confirm current Radarr and Plex counts
without printing credentials.

- [ ] **Step 3: Enable the host-only watched deletion gate**

Set:

```text
MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION=true
```

in the protected worker env, retain mode `0600`, clear only the worker heartbeat
for fresh-health validation, and restart the worker container.

- [ ] **Step 4: Supervise one apply and one convergence run**

Inspect reports, SQLite observations, MongoDB lifecycle state, Radarr, Plex
watchlist, logs, and heartbeat. Do not manufacture a watched event or delete a
real file merely to exercise production. If no real watched transition exists,
record that the destructive path is test-covered and armed but not yet live-
exercised.

- [ ] **Step 5: Verify continuous operation and rollback state**

Confirm hourly apply, five-minute deploy timer, healthy read-only containers,
current/previous SHA files, no leaked secrets, and enough host disk for current
plus rollback images.

- [ ] **Step 6: Complete roadmap and rollout records**

Update OKF with actual deployed SHA, observed baseline counts, feature-gate
state, report IDs, any manual candidates, and the explicit live-exercise status.
Run OKF validation, commit, push, require exact-SHA CI again, and verify the
timer deploys the documentation SHA with a fresh worker heartbeat.

# Links

- [Watched Movie Lifecycle Design](watched_movie_lifecycle_design.md)
- [Production Movie Sync](../architecture/movie_sync_production.md)
- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
- [Validation](../runbooks/validation.md)
