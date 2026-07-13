---
type: Backlog
title: TV Phase 4 Concluded-Season Cleanup Implementation
description: TDD plan for configured-account watched evidence, observed season eligibility, one-use cleanup authorization, exact Sonarr file deletion, and guarded rollout.
tags:
  - tv
  - plex
  - sonarr
  - cleanup
  - safety
timestamp: 2026-07-13T00:00:00Z
version: 0.1.0
---

# TV Phase 4 Concluded-Season Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete only exact Sonarr episode-file records for a fully watched, concluded numbered season after seven continuously observed eligible days, then unmonitor that season while keeping the series and new seasons monitored.

**Architecture:** The backend combines Trakt completion, configured-account Plex ledger/current-played evidence, and fresh worker Sonarr observations into immutable lifecycle facts and a mutable one-use cleanup authorization. The worker independently recollects Sonarr and Plex, groups multi-episode files, evaluates live gates and destructive caps, atomically claims the authorization, performs a final recollection, deletes only authorized exact file IDs, unmonitors the converged season, and reports every child result to MongoDB and SQLite. Neither global TV apply nor a stale lifecycle event is deletion permission.

**Tech Stack:** .NET 10, MongoDB 8, Plex API, Trakt read model, Python 3.11, httpx, SQLite, Sonarr API v3, xUnit, FluentAssertions, and pytest.

---

## Prerequisites

Complete Phases 1 through 3 first. The published TV snapshot, Plex history
ledger/outbox, exact-TVDB Sonarr/Plex collector, SQLite ownership, worker run
lease, report-only planner, and protected worker-run endpoint must already be
green. Keep these settings false throughout implementation and report-only
observation:

```dotenv
TV_SYNC_ALLOW_SEASON_FILE_DELETION=false
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false
```

### Task 1: Collect Fresh Configured-Account Plex Played State

**Files:**
- Create: `backend/src/Watchlist.Application/IPlexTvLibraryClient.cs`
- Create: `backend/src/Watchlist.Application/PlexTvLibrarySnapshotDto.cs`
- Create: `backend/src/Watchlist.Application/PlexEpisodePlayedStateDto.cs`
- Create: `backend/src/Watchlist.Application/TvShowCleanupEvidence.cs`
- Create: `backend/src/Watchlist.Application/ITvCleanupEvidenceService.cs`
- Create: `backend/src/Watchlist.Application/TvCleanupEvidenceService.cs`
- Create: `backend/src/Watchlist.Infrastructure/PlexTvLibraryClient.cs`
- Modify: `backend/src/Watchlist.Infrastructure/PlexOptions.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/PlexTvLibraryClientTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvCleanupEvidenceServiceTests.cs`

- [ ] **Step 1: Write failing current-played-state client tests**

Cover complete pagination, configured account and TV-library filtering, exact
nested TVDB/TMDB/IMDb GUID extraction, episode rating keys, season/episode
numbers, `viewCount`, `lastViewedAt`, and collection time. Reject title-only
identity, another account, another library, conflicting GUIDs, missing
per-account view state, partial pages, and malformed timestamps.

```csharp
snapshot.AccountId.Should().Be(42);
snapshot.LibrarySectionId.Should().Be("7");
snapshot.Episodes.Should().ContainEquivalentOf(new
{
    TvdbShowId = 81189,
    SeasonNumber = 2,
    EpisodeNumber = 3,
    PlexRatingKey = "episode-203",
    ViewCount = 1
});
```

- [ ] **Step 2: Run the client test and confirm RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~PlexTvLibraryClientTests"
```

Expected: compile failure because `IPlexTvLibraryClient` and its implementation
do not exist.

- [ ] **Step 3: Implement the narrow read-only client contract**

```csharp
public interface IPlexTvLibraryClient
{
    Task<PlexTvLibrarySnapshotDto> GetConfiguredAccountPlayedStateAsync(
        CancellationToken cancellationToken);
}

public sealed record PlexEpisodePlayedStateDto(
    int TvdbShowId,
    int SeasonNumber,
    int EpisodeNumber,
    string PlexRatingKey,
    int ViewCount,
    DateTimeOffset? LastViewedAt);

public sealed record PlexTvLibrarySnapshotDto(
    string MachineIdentifier,
    long AccountId,
    string LibrarySectionId,
    DateTimeOffset CollectedAt,
    IReadOnlyList<PlexEpisodePlayedStateDto> Episodes);
```

Use the configured Plex account and TV library only. This client exposes no
delete operation and does not mutate Plex metadata or watched state.

- [ ] **Step 4: Write failing evidence-composition tests**

For every exact local episode, require both a `plex_watch_events` ledger row
whose durable `Disposition == Accepted` for the configured account (independent
of its nullable bootstrap outcome) and a fresh current library state with
`viewCount > 0`. Cover Mark Unwatched (`viewCount == 0`), missing ledger event,
quarantined event, unresolved Trakt outbox state, account/machine/library
mismatch, stale collection, identity conflict, and the all-watched case.

- [ ] **Step 5: Implement evidence composition**

`TvCleanupEvidenceService` reads the Phase 2 event and outbox repositories and
produces stable blocker codes. It must include at least:

```text
plex_ledger_event_missing
plex_current_state_unwatched
plex_current_state_ambiguous
plex_evidence_stale
plex_event_quarantined
trakt_outbox_unresolved
plex_binding_mismatch
```

Freshness is 30 minutes. The play may be old; only the current configured-
account verification read must be fresh.

- [ ] **Step 6: Verify and commit configured-account evidence**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~PlexTvLibraryClientTests|FullyQualifiedName~TvCleanupEvidenceServiceTests"
git add backend/src backend/tests
git commit -m "feat(tv): verify configured-account episode watches"
```

Expected: focused tests pass and logs/DTOs contain no Plex token.

### Task 2: Publish Fresh Worker Season Observations

**Files:**
- Create: `backend/src/Watchlist.Application/TvWorkerCleanupObservationDto.cs`
- Create: `backend/src/Watchlist.Application/TvWorkerEpisodeFileObservationDto.cs`
- Modify: `backend/src/Watchlist.Application/TvWorkerRunSummaryRequestDto.cs`
- Modify: `backend/src/Watchlist.Application/TvWorkerRunService.cs`
- Modify: `backend/src/Watchlist.Application/ITvWorkerRunRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvWorkerRunDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvWorkerRunRepository.cs`
- Modify: `workers/vod-filter/src/models/tv_destination.py`
- Modify: `workers/vod-filter/src/services/tv_sync_report.py`
- Modify: `workers/vod-filter/src/clients/tv_backend_client.py`
- Test: `backend/tests/Watchlist.Application.Tests/TvWorkerRunServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvWorkerRunRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/TvWorkerApiTests.cs`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_sync_report.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_backend_client.py`

- [ ] **Step 1: Write failing observation-contract tests**

Bind each observation to the worker run's `sourceGenerationId`, `workerId`,
`lifecycleVersion`, exact TVDB ID, Sonarr series ID, numbered season, collection
time, ownership kind, collection
completeness, future/unknown-air-date regular-episode counts, and episode-file
groups. Store only IDs/counts/hashes; reject full media paths.

```csharp
public sealed record TvWorkerCleanupObservationDto(
    string SourceGenerationId,
    long LifecycleVersion,
    int TvdbId,
    int SonarrSeriesId,
    int SeasonNumber,
    string Ownership,
    DateTimeOffset CollectedAt,
    bool SonarrCollectionComplete,
    int FutureRegularEpisodes,
    int UnknownAirDateRegularEpisodes,
    IReadOnlyList<TvWorkerEpisodeFileObservationDto> EpisodeFiles);

public sealed record TvWorkerEpisodeFileObservationDto(
    int EpisodeFileId,
    IReadOnlyList<int> SonarrEpisodeIds,
    IReadOnlyList<string> EpisodeKeys);
```

Tests reject a non-owned/non-adopted row, a source generation that differs from
the enclosing worker run, a lifecycle-version mismatch, duplicate file ID,
duplicate episode mapping, missing exact TVDB ID, and collection times more
than 30 minutes old when consumed.

- [ ] **Step 2: Run backend and worker tests and confirm RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvWorkerRunServiceTests|FullyQualifiedName~MongoTvWorkerRunRepositoryTests"

Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_report.py tests\vod_filter\test_tv_backend_client.py -q
Pop-Location
```

Expected: contract assertions fail for missing season observations.

- [ ] **Step 3: Implement redacted observation persistence**

Extend the protected `POST /api/worker/tv/runs` body with
`cleanupObservations`. Persist the immutable run as received after structural
validation. Never treat a failed or partial Sonarr collection as an empty safe
inventory.

When the next backend sync builds draft manifest `N`, it may consume an
observation only when `observation.sourceGenerationId ==
N.previousGenerationId`, the enclosing run used that same generation, the
exact TVDB/Sonarr ownership binding is unchanged, `lifecycleVersion` still
matches, and `collectedAt` is no more than 30 minutes old. The observation is
evidence sourced from the previous published generation; any authorization it
helps create is bound to newly published generation `N`, never back to the
source generation.

- [ ] **Step 4: Verify API authorization and redaction**

Test missing/wrong sync key as `401`, invalid observations as `400`, and valid
observations as the unchanged `202 Accepted` response
`{runId, acceptedAt}`. Assert serialized Mongo documents and responses contain
no Sonarr API key or path and that the endpoint never echoes observations.

- [ ] **Step 5: Commit the observation bridge**

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvWorkerApiTests"

Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_report.py tests\vod_filter\test_tv_backend_client.py -q
Pop-Location

git add backend/src backend/tests workers/vod-filter/src workers/vod-filter/tests
git commit -m "feat(tv): publish exact season cleanup observations"
```

### Task 3: Evaluate Concluded Seasons And Seven-Day Continuity

**Files:**
- Create: `backend/src/Watchlist.Application/TvSeasonCleanupDecision.cs`
- Create: `backend/src/Watchlist.Application/TvSeasonCleanupEvaluator.cs`
- Create: `backend/src/Watchlist.Application/TvCleanupCandidateState.cs`
- Create: `backend/src/Watchlist.Domain/TvCleanupActionType.cs`
- Create: `backend/src/Watchlist.Domain/TvCleanupIntentPayload.cs`
- Modify: `backend/src/Watchlist.Domain/TvLifecycleEvent.cs`
- Modify: `backend/src/Watchlist.Domain/TvSeasonProgress.cs`
- Modify: `backend/src/Watchlist.Application/TvLifecycleEvaluator.cs`
- Modify: `backend/src/Watchlist.Application/TvSyncService.cs`
- Modify: `backend/src/Watchlist.Application/TvBlockerCodes.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvShowDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvLifecycleEventDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvGenerationRepository.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvSeasonCleanupEvaluatorTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvLifecycleEvaluatorTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvGenerationRepositoryTests.cs`

- [ ] **Step 1: Write the full failing eligibility matrix**

The positive case is season number greater than zero with at least one aired
regular episode, Trakt `completed == aired`, no Trakt future/unaired regular
episode, `next_episode` outside the season or absent, a complete fresh worker
observation with no future/unknown-air-date regular episode, exact verified
TVDB identity, owned/adopted Sonarr series, every file mapping exactly to Plex-
watched episodes, fresh Plex verification, no unresolved outbox/quarantine,
and no explicit current Trakt watchlist membership.

`TvLifecycleState.SourceRemoved` is always ineligible even if its retained
historical progress looks complete. Source absence is not terminal status and
must never authorize deletion.

Create one negative test for every predicate plus season 0, `aired == 0`, a
partially watched multi-episode group, and a zero-file season whose episode/file
collection is incomplete, stale, or otherwise unknown. Add the complete
zero-file positive case: a concluded season with a fresh exhaustive Sonarr
episode/file collection proving no files is eligible after every other gate and
eventually plans only the season-unmonitor child. Never infer zero files from a
failed or partial collection.

- [ ] **Step 2: Write continuity and idempotency tests**

Use a fake clock and scheduled generations:

- the first qualifying `scheduled_full` sets `candidateSince` and zero observed
  duration;
- only intervals between consecutive mutation-capable `scheduled_full`
  generations at most two hours apart accrue;
- activity generations can cancel but never accrue;
- a gap greater than two hours, `mutationCapable == false`, Mark Unwatched,
  explicit watchlist membership, new/future episode, stale Plex/worker evidence,
  or identity conflict resets and emits one `cleanup_canceled` event;
- exactly seven observed days emits one stable `season_cleanup_authorized` event;
- replaying the same generation creates no second event; and
- terminal cleanup for the same show suppresses season authorization in that
  generation.

- [ ] **Step 3: Run evaluator tests and confirm RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvSeasonCleanupEvaluatorTests|FullyQualifiedName~TvLifecycleEvaluatorTests"
```

Expected: missing evaluator and candidate-state assertions fail.

- [ ] **Step 4: Implement a pure evaluator**

```csharp
public sealed record TvCleanupCandidateState(
    DateTimeOffset CandidateSince,
    TimeSpan EligibleObservedDuration,
    string PredicateHash,
    string SourceGenerationId,
    long LifecycleVersion,
    DateTimeOffset LastObservedAt);

public sealed record TvSeasonCleanupDecision(
    int SeasonNumber,
    bool Eligible,
    DateTimeOffset? CandidateSince,
    TimeSpan EligibleObservedDuration,
    string PredicateHash,
    IReadOnlyList<string> Blockers,
    TvLifecycleEvent? Event);

public sealed class TvSeasonCleanupEvaluator
{
    public TvSeasonCleanupDecision Evaluate(
        TvShow current,
        TvSeasonProgress season,
        TvShowCleanupEvidence evidence,
        TvWorkerCleanupObservationDto? observation,
        TvGenerationManifest manifest,
        DateTimeOffset now);
}
```

Extend `TvLifecycleEvent` with nullable `TvCleanupIntentPayload CleanupIntent`
and define the payload exactly once in the domain:

```csharp
public sealed record TvCleanupIntentPayload(
    TvCleanupActionType ActionType,
    int TvdbId,
    int SonarrSeriesId,
    int? SeasonNumber,
    IReadOnlyList<int> ExpectedEpisodeFileIds,
    string? ExpectedInventoryHash,
    int ExpectedAired,
    int ExpectedCompleted,
    DateTimeOffset CandidateSince,
    DateTimeOffset AuthorizedAt,
    string SourceGenerationId,
    DateTimeOffset WorkerObservationCollectedAt,
    DateTimeOffset PlexEvidenceCollectedAt,
    DateTimeOffset? PlexHistoryWatermark,
    bool PlexHistoryCollectionComplete,
    bool PlexHistoryCollectionSucceeded,
    long PlexHistoryObservedEventCount);
```

`TvCleanupActionType` defines `SeasonFiles` and the forward-compatible
`TerminalSeries`. A `season_cleanup_authorized` event requires a non-null
payload with `SeasonFiles`, a positive numbered season, sorted exact file IDs,
and null inventory hash. Other Phase 4 lifecycle events require a null payload.
The event's existing `PredicateHash` binds the canonical complete eligibility
facts; the typed payload makes target, progress, candidate/authorization times,
and evidence sources immutable and queryable. Persist the payload in
`MongoTvLifecycleEventDocument`; an event-ID replay with a byte-different
predicate or payload is a conflict, never an overwrite. Add domain, evaluator,
serialization, and repository tests for every invariant. Phase 5 reuses this
same payload with `TerminalSeries`, null season, and a non-null complete-series
inventory hash.

Every cleanup payload requires a complete, successful configured-binding Plex
history collection and a nonnegative binding-wide distinct observed-event
count copied from the Phase 2 checkpoint. `PlexHistoryWatermark` is nullable
only for the strict complete-empty case:

```text
PlexHistoryWatermark == null
  iff PlexHistoryCollectionComplete == true
  and PlexHistoryCollectionSucceeded == true
  and PlexHistoryObservedEventCount == 0
```

A positive observed-event count requires a non-null watermark. A null
watermark with incomplete/failed/unknown collection state, or a non-null
watermark with zero binding-wide events, rejects the event before persistence.
These fields are part of the immutable payload and live claim binding, while
their timestamps remain excluded from the semantic continuity hash.

Hash canonical ordered semantic eligibility facts, not serialized object
property order. Exclude generation IDs, collection timestamps, and candidate
timers from this continuity hash; validate freshness separately and retain
`SourceGenerationId`/`LastObservedAt` beside it. Event IDs derive
deterministically from action type, Trakt ID, TVDB ID, season number, lifecycle
version, and predicate hash.

- [ ] **Step 5: Persist decisions inside the publish-last generation**

Stage season candidate state and lifecycle events with the TV row, write the
immutable manifest, then advance the published pointer. A staging failure must
leave the previous candidate and pointer visible.

The evaluator consumes only a Task 2 observation whose source generation is the
draft manifest's `PreviousGenerationId`. The emitted event and authorization
projection bind the newly published manifest ID and current lifecycle version.

- [ ] **Step 6: Verify and commit season eligibility**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvSeasonCleanupEvaluatorTests|FullyQualifiedName~TvLifecycleEvaluatorTests|FullyQualifiedName~MongoTvGenerationRepositoryTests"
git add backend/src backend/tests
git commit -m "feat(tv): authorize continuously eligible season cleanup"
```

### Task 4: Implement One-Use Cleanup Authorization And Result APIs

**Files:**
- Create: `backend/src/Watchlist.Domain/TvCleanupAuthorizationState.cs`
- Create: `backend/src/Watchlist.Application/TvCleanupAuthorization.cs`
- Create: `backend/src/Watchlist.Application/ITvCleanupAuthorizationService.cs`
- Create: `backend/src/Watchlist.Application/TvCleanupAuthorizationService.cs`
- Create: `backend/src/Watchlist.Application/TvCleanupClaimRequestDto.cs`
- Create: `backend/src/Watchlist.Application/TvCleanupClaimResponseDto.cs`
- Create: `backend/src/Watchlist.Application/TvCleanupResultRequestDto.cs`
- Create: `backend/src/Watchlist.Application/TvCleanupResultResponseDto.cs`
- Create: `backend/src/Watchlist.Application/TvCleanupChildResultDto.cs`
- Create: `backend/src/Watchlist.Application/ITvCleanupAuthorizationRepository.cs`
- Modify: `backend/src/Watchlist.Application/TvSyncService.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvCleanupAuthorizationDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvCleanupChildResultDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvCleanupAuthorizationRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoDbOptions.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvIndexHostedService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/TvEndpointRouteBuilderExtensions.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvCleanupAuthorizationServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvCleanupAuthorizationRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvSyncServiceTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/TvWorkerApiTests.cs`

- [ ] **Step 1: Write failing atomic-claim tests**

Require `eventId`, `workerId`, current `manifestId`, action type
`season_files`, exact Sonarr series ID, sorted expected episode-file IDs, and
`livePredicateHash`. Grant one ten-minute lease only when the mutable
authorization is pending, unexpired, uncanceled, bound to the current
mutation-capable manifest, no more than 30 minutes old, and has at least ten
minutes remaining before its authorization expiry. The exact ten-minute lease
therefore never outlives the authorization. Two concurrent claims produce
exactly one durable lease/worker; an exact same-worker replay may return that
same lease after revalidation without constituting a second winner.

At every claim, read fresh Phase 2 history health rather than trusting only the
published manifest. Initial and `retry_actions` claims require zero pending
post-cutover routes, no routing-conflict blocker, no unresolved outbox row, and
no quarantined Plex event for the exact show. If any appears, atomically cancel
an active pending mutation projection; for an already leased projection,
atomically set immutable `MutationRevokedAt` and
`MutationRevocationReason` while retaining its state and lease solely for
result/audit recovery. Reject any new external-action permission. A strictly
audit-only `reconcile_only` recovery may be created while the blocker remains,
but it can report only independently recollected `already_absent`/completed
children and can never construct or authorize a Sonarr call; test that explicit
exception.
An exact same-worker replay of an already granted claim is a live
revalidation: return the same authorization/lease without extending its
expiry only if every current gate still passes. Otherwise cancel and return
`409 cleanup_authorization_canceled` for pending or mark the lease revoked and
return `409 cleanup_authorization_revoked`. A different worker never receives
that lease.

```csharp
public sealed record TvCleanupClaimRequestDto(
    string WorkerId,
    string ManifestId,
    string ActionType,
    int SonarrSeriesId,
    IReadOnlyList<int> ExpectedEpisodeFileIds,
    string? TerminalPathFingerprint,
    string LivePredicateHash,
    bool Recovery,
    string? RecoveryOfAuthorizationId,
    IReadOnlyList<string> LocallyConvergedActionIds);

public sealed record TvCleanupClaimResponseDto(
    string AuthorizationId,
    string EventId,
    string? RecoveryOfAuthorizationId,
    string RecoveryMode,
    string LeaseId,
    string WorkerId,
    string ManifestId,
    string ActionType,
    int TvdbId,
    int SonarrSeriesId,
    int? SeasonNumber,
    DateTimeOffset LeaseIssuedAt,
    DateTimeOffset LeaseValidatedAt,
    DateTimeOffset LeaseExpiresAt,
    string LivePredicateHash,
    IReadOnlyList<int> ExpectedEpisodeFileIds,
    string? TerminalPathFingerprint);
```

Season claims allow an empty file-ID list for zero-file convergence and require
`TerminalPathFingerprint == null`. Initial claims require `Recovery == false`
with `RecoveryOfAuthorizationId == null` and an empty locally-converged list. A
recovery request names the exact expired authorization being recovered, the
current manifest ID, and a fresh live recollection. This removes ambiguity after
multiple interrupted attempts. The recovery must preserve the root event's
immutable file IDs, series ID, season, lifecycle version, and optional target
fingerprint.

- [ ] **Step 2: Write failing result and crash-recovery tests**

Use this body:

```csharp
public sealed record TvCleanupResultRequestDto(
    string AuthorizationId,
    string WorkerId,
    string LeaseId,
    string ManifestId,
    string Status,
    string LivePredicateHash,
    IReadOnlyList<TvCleanupChildResultDto> ChildResults);

public sealed record TvCleanupChildResultDto(
    string ActionId,
    string ChildType,
    string TargetId,
    string Status,
    bool DestructiveCallIssued,
    string Reason,
    DateTimeOffset ObservedAt,
    bool? EpisodeFileAbsent,
    bool? SeasonUnmonitored,
    bool? SeriesAbsent,
    bool? TerminalInventoryEmpty,
    bool? DeleteFiles,
    bool? AddImportListExclusion);

public sealed record TvCleanupResultResponseDto(
    string AuthorizationId,
    string EventId,
    string State,
    string Status,
    IReadOnlyList<string> UnresolvedActionIds);
```

For an initial claim, `AuthorizationId == EventId`,
`RecoveryOfAuthorizationId == null`, and `RecoveryMode == "initial"`.
`LeaseExpiresAt` must be exactly ten minutes after `LeaseIssuedAt`.
`LeaseValidatedAt` is the server time used for this response and satisfies
`LeaseIssuedAt <= LeaseValidatedAt < LeaseExpiresAt`; it equals issued time for
the initial response and advances on a replay without changing either endpoint.
The worker captures an injected monotonic timestamp immediately before sending
the claim and computes a candidate local deadline as
`requestStartedMonotonic + (LeaseExpiresAt - LeaseValidatedAt)`. The full
request/response round trip therefore consumes local lease budget. Key that
deadline by `(AuthorizationId, LeaseId)` and retain the minimum of the stored
deadline and every replay candidate; response arrival or replay never starts a
fresh ten-minute countdown. A restarted process has no portable monotonic
origin, so it derives a conservative remainder only from the server's current
`LeaseValidatedAt`. Never persist or compare a host monotonic absolute value
across process boots, and do not compare the server deadline directly with a
potentially skewed host wall clock.
Parent result status is exactly `partial`, `failed`, or `converged`. Child
status is exactly `completed`, `already_absent`, `failed`, or `blocked`;
`ChildType`, `TargetId`, `Reason`, and `ObservedAt` are always present and
stable. The nullable query booleans record the relevant post-action observation
without inventing success for another child type. Phase 4 season children
require `DeleteFiles == null` and `AddImportListExclusion == null`; the fields
are forward-compatible audit facts for Phase 5's exact terminal query.
Assert duplicate `(authorizationId, actionId)` reports are idempotent, a stale/wrong
lease is rejected, partial results preserve unresolved children, convergence
requires all expected file children plus the season-monitoring child, and an
expired lease itself can never be reclaimed or renewed.

A mutation-revoked leased projection grants no further Sonarr permission but
retains its original worker/lease solely for result audit. Before that lease
expires, the result endpoint accepts exact same-worker child results for calls
already issued (plus blocked children), applies the same immutable hash and
postcondition validation, and may mark factual convergence when every target
is proven converged. This is result ingestion, never lease reactivation. A
post-call/pre-result routing-blocker test must revoke the lease, accept and
audit the already-issued child's result, and converge when absence is proven.
If unresolved children remain, no new call is permitted under that lease.

Test two explicit recovery paths against the original event. After fresh live
recollection, the backend atomically creates a separate short-lived recovery
authorization with a new `AuthorizationId`, `RecoveryOfAuthorizationId` equal
to the expired authorization (including a previously mutation-revoked lease
after it expires), the immutable original target/child/lifecycle
binding, and the current mutation-capable manifest plus semantic live hash. A
`reconcile_only` recovery may report only original children now independently
confirmed `already_absent`/completed and can never authorize an external call.
A `retry_actions` recovery is required before any still-present original child
may receive another Sonarr call. It reruns every current gate, may authorize
only unresolved original children, and cannot replace file IDs, series ID,
season, lifecycle version, or target fingerprint. Previously accepted backend
results and the local SQLite audit seed both paths; neither path reconstructs
an impossible pre-delete inventory. While the history blocker remains, only
`reconcile_only` may be created; `retry_actions` requires the blocker resolved
and every current gate on a new manifest.

Key Mongo child results uniquely by `(AuthorizationId, ActionId)`, not by the
root event/action pair, and index the root event separately for cross-attempt
recovery history. This preserves every explicit authorization attempt while
making a replay of the same result idempotent.

Canonicalize every accepted child result over all immutable request fields
(authorization/event/action/child/target IDs, status, destructive-call flag,
reason, observed time, every nullable postcondition, and both query booleans),
then store its lowercase SHA-256 `ResultHash` in
`MongoTvCleanupChildResultDocument`. Sort parent children by action ID before
processing. An exact `(AuthorizationId, ActionId, ResultHash)` replay returns
the already accepted child/result without rewriting state. The same key with a
different hash returns `409 cleanup_result_conflict`, preserves the first
payload, and cannot advance parent convergence. A later partial submission may
add previously unseen children, but it may never revise an accepted child;
retries that need a different outcome use a new recovery authorization ID.

- [ ] **Step 3: Run the service/repository tests and confirm RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvCleanupAuthorizationServiceTests|FullyQualifiedName~MongoTvCleanupAuthorizationRepositoryTests|FullyQualifiedName~TvSyncServiceTests"
```

Expected: compile failure for missing authorization service/repository.

- [ ] **Step 4: Implement the mutable projection without mutating lifecycle events**

Store `pending`, `leased`, `converged`, `canceled`, or `expired` in
`tv_cleanup_authorizations`. The immutable `tv_lifecycle_events` row remains
unchanged. A later eligible manifest may refresh an unclaimed pending
projection; claimed, canceled, expired, and converged rows cannot be silently
renewed. An explicit recovery creates a separate projection row with a unique
authorization ID and `recoveryOfAuthorizationId`; it never changes the expired
row. Recovery modes are exactly `reconcile_only` or `retry_actions`. Give each
pending/leased document an `activeEventKey` equal to the root event ID and add a
partial unique index for that non-null key; clear it only on a terminal
projection state. Combined with atomic create/claim, this permits at most one
active initial-or-recovery authorization per event even under concurrent
requests.

Current history health is cancellation/revocation authority, never creation
authority. The next lifecycle refresh cancels a pending mutation projection when
pending or conflicting post-cutover routing, unresolved outbox work, or
quarantine appears; it marks an already leased projection mutation-revoked
without discarding its audit/result channel. The claim/revalidation transaction
performs the same transition immediately so a previously published manifest
cannot preserve stale deletion permission.

That transition applies only to mutation-bearing initial/`retry_actions`
projections. A `reconcile_only` projection is explicitly result-only, may exist
under the blocker, and is never interpreted as external-action permission.

Reuse Task 3's `TvCleanupActionType` and map its values exactly to wire strings
`season_files` and `terminal_series`; define `TvCleanupAuthorizationState` with
the five states above. The application
`TvCleanupAuthorization` holds the exact authorization/recovery/event/action/
Trakt/TVDB/season/
lifecycle/predicate/manifest/source-generation bindings, evidence timestamps,
original Sonarr series and expected-child binding, mutable state, and optional
lease fields plus nullable immutable `MutationRevokedAt` and stable
`MutationRevocationReason`. Revocation never clears or extends the original
lease. Never add cleanup members to the reversible destination action enum or
reuse a lifecycle event as a mutable lease record.

Persist both revocation fields in `MongoTvCleanupAuthorizationDocument` with a
one-way null-to-value compare-and-set on the active lease. An exact repeated
revocation is idempotent; a different later reason cannot overwrite the first
audit fact. Repository concurrency tests race claim/revalidation/revocation and
prove there remains one lease, one worker, and one immutable revocation.

Wire the same transition into `TvSyncService` after a successful complete
scheduled generation evaluates current Phase 2 history health. A scheduled-
generation integration test proves a new routing blocker cancels a pending
mutation authorization, mutation-revokes a leased one without losing its
result channel, leaves a strictly audit-only `reconcile_only` projection
available, and cannot create a replacement authorization in that generation.

- [ ] **Step 5: Map and protect the endpoints**

```text
POST /api/worker/tv/cleanup-authorizations/{eventId}/claim
POST /api/worker/tv/cleanup-authorizations/{authorizationId}/result
```

Use the existing `X-Watchlist-Sync-Key` filter. Return `404` for an unknown
event, `409` plus a stable reason for a disallowed claim against stale,
different-worker leased, revoked, canceled, expired, or converged state, `400`
for malformed bindings, and `200` for an accepted lease or result. The result
route still accepts exact same-worker audit/results for a mutation-revoked
unexpired lease as defined in Step 2. An exact same-worker claim replay is the pre-action revalidation path,
returns the unchanged lease only while current health remains clean, and never
extends `LeaseExpiresAt`; only `LeaseValidatedAt` advances to expose the
server-observed remaining budget. A recovery claim addresses the original event but returns the new
authorization ID that must be used for its result. Never echo paths, keys, or
raw upstream bodies.

- [ ] **Step 6: Verify service and API behavior, then commit**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvCleanupAuthorizationServiceTests|FullyQualifiedName~MongoTvCleanupAuthorizationRepositoryTests|FullyQualifiedName~TvSyncServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvWorkerApiTests"
git add backend/src backend/tests
git commit -m "feat(tv): lease and audit season cleanup authorization"
```

### Task 5: Parse And Claim Season Authorizations In The Worker

**Files:**
- Modify: `contracts/tv/worker-sync-state-v1.json`
- Modify: `contracts/tv/enums-v1.json`
- Modify: `backend/src/Watchlist.Application/WorkerTvCleanupAuthorizationDto.cs`
- Modify: `backend/src/Watchlist.Application/TvExportService.cs`
- Modify: `workers/vod-filter/src/models/tv_sync.py`
- Create: `workers/vod-filter/src/models/tv_cleanup.py`
- Modify: `workers/vod-filter/src/clients/tv_backend_client.py`
- Test: `backend/tests/Watchlist.Api.Tests/TvWorkerContractTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvExportServiceTests.cs`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_backend_client.py`

- [ ] **Step 1: Add failing strict-parser tests**

Parse initial season authorizations with equal authorization/event IDs plus
action/Trakt/TVDB/season/lifecycle/
predicate/manifest bindings, authorization and expiry times, expected progress,
Plex evidence collection time, nullable Plex history watermark, complete/
successful collection flags, and binding-wide observed-event count. Reject unknown
action type, duplicate authorization/event ID, nonpositive season, nonpositive TVDB ID,
timezone-free time, expired rows presented as pending, and authorization
`manifestId != snapshot.generationId`. Accept a null watermark only with both
flags true and count zero; reject null with positive count or either false
flag, and reject non-null with count zero.

Extend the canonical export DTO and sole shared fixture with
`authorizationId`; for exported initial pending rows it must equal `eventId`.
Recovery authorizations are returned by the claim API and are not republished as
new deletion permission in an older snapshot.

- [ ] **Step 2: Add failing claim/result client tests**

Assert exact URL and sync-key header, sorted file IDs, no terminal fingerprint
for season cleanup, timezone-aware lease expiry, stable `404`/`409` mapping,
and redacted child-result serialization. Initial claim responses require
`recoveryMode="initial"`; recovery responses require a distinct authorization
ID, the original authorization link, and exactly `reconcile_only` or
`retry_actions`. Results address the returned authorization ID and include it in
the body. A `reconcile_only` response must be rejected by the executor if any
external action is proposed. Reject a lease with a nonpositive or non-ten-minute
server `LeaseIssuedAt`/`LeaseExpiresAt` interval, or `LeaseValidatedAt` outside
that interval; use monotonic elapsed time for the 60-second execution reserve. A
delayed-response test advances monotonic time during the claim call and proves
that the delay is subtracted from the usable lease.

Add a same-worker exact-claim replay test that receives the identical lease
without extending it. Then make current history report one pending/conflicting
post-cutover route: revalidation must return
`409 cleanup_authorization_revoked`, and the worker
must not construct or execute a cleanup child. A different worker's replay is
always rejected. Replay a clean lease near expiry and assert its advanced
`LeaseValidatedAt` preserves only the original remaining server budget; the
stored local monotonic deadline is unchanged or reduced, never reset to ten
minutes.

- [ ] **Step 3: Run the parser tests and confirm RED**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_backend_client.py -q
Pop-Location
```

Expected: failures for missing cleanup DTO and client methods.

- [ ] **Step 4: Implement strict DTOs and client calls**

The worker accepts only `schemaVersion == "1"`, timezone-aware timestamps, and
one published generation. HTTP timeouts before a known claim response do not
grant permission; the next run fetches current authorization state rather than
assuming a lease.

Keep destructive planning in `tv_cleanup.py` with these separate types:

```python
class TvCleanupDecisionType(str, Enum):
    SEASON_FILES = "season_files"
    TERMINAL_SERIES = "terminal_series"


class TvCleanupChildActionType(str, Enum):
    DELETE_EPISODE_FILE = "delete_episode_file"
    UNMONITOR_SEASON = "unmonitor_season"
    DELETE_SERIES = "delete_series"


@dataclass(frozen=True)
class TvCleanupDecision:
    event_id: str
    decision_type: TvCleanupDecisionType
    tvdb_id: int
    sonarr_series_id: int
    season_number: int | None
    lifecycle_version: int
    manifest_id: str
    reason: str
    child_actions: tuple[TvCleanupChildActionType, ...]
```

Phase 4 constructs only `SEASON_FILES` decisions and never constructs
`DELETE_SERIES`. Do not add destructive action values to Phase 3's
`TvActionType`/`TvDecision`, and do not give `TvDestinationExecutor` a cleanup
client. Only `TvCleanupExecutor` may consume `TvCleanupDecision`.

- [ ] **Step 5: Verify both fixture owners and commit**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvExportServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvWorkerContractTests"

Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_backend_client.py -q
Pop-Location

git add backend/src/Watchlist.Application contracts backend/tests workers/vod-filter/src workers/vod-filter/tests
git commit -m "feat(tv): consume season cleanup authorizations"
```

### Task 6: Group Exact Episode Files And Evaluate Live Season Gates

**Files:**
- Modify: `workers/vod-filter/src/models/tv_destination.py`
- Create: `workers/vod-filter/src/services/tv_live_gates.py`
- Modify: `workers/vod-filter/src/services/tv_sync_collector.py`
- Modify: `workers/vod-filter/src/clients/sonarr_client.py`
- Modify: `workers/vod-filter/src/clients/plex_tv_client.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_live_gates.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_sync_collector.py`
- Test: `workers/vod-filter/tests/vod_filter/test_sonarr_client.py`
- Test: `workers/vod-filter/tests/vod_filter/test_plex_tv_client.py`

- [ ] **Step 1: Write failing file-group tests**

Group every Sonarr episode sharing an `episodeFileId` into one immutable child:

```python
@dataclass(frozen=True)
class SeasonEpisodeFileGroup:
    episode_file_id: int
    sonarr_episode_ids: tuple[int, ...]
    episode_keys: tuple[str, ...]
    all_linked_episodes_watched: bool
```

Cover a one-episode file, two fully watched episodes in one file, one watched
plus one unwatched episode in one file, an episode without a file, a file with
no linked episode, duplicate links, special-season links, and conflicting
season mappings.

- [ ] **Step 2: Write every failing live-gate test**

Require current manifest under 30 minutes, current authorization binding to the
newly published generation, an observation sourced from that manifest's
`previousGenerationId` with unchanged `lifecycleVersion`, exact TVDB and
expected Sonarr series ID, owned/adopted destination, fresh complete
Sonarr/Plex collections, fresh configured-account Plex evidence, no future or
unknown-air-date regular episode, no source/Sonarr next-episode disagreement,
every file group fully watched, no unknown mapping, no explicit Trakt
watchlist membership, zero pending/conflicting post-cutover routes, no
unresolved outbox/quarantine blocker, and identical live predicate facts.

Unknown is blocking. A zero-file concluded season can pass only when the full
Sonarr episode/file collection proves zero files; it still plans an unmonitor
child.

- [ ] **Step 3: Run the gate tests and confirm RED**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_live_gates.py tests\vod_filter\test_tv_sync_collector.py -q
Pop-Location
```

Expected: failures for the missing grouping and live-gate evaluator.

- [ ] **Step 4: Implement deterministic live facts and hash**

The canonical `livePredicateHash` includes only stable semantic facts:
manifest/event IDs, TVDB/Sonarr IDs, season, sorted file and episode IDs, Plex
machine/account/library binding, played facts, next/future/unknown facts, and
ownership. Do not include collection timestamps, secrets, or absolute paths.
Validate Sonarr, Plex, ledger, and source freshness independently for both the
initial and final collection, persist their timestamps as separate redacted
audit fields, and require each to be within its limit. This lets a safe
recollection produce the same semantic hash while proving that both reads were
fresh.

- [ ] **Step 5: Add Sonarr and Plex collection methods only**

Read episodes and episode files by exact series ID. Read current configured-
account Plex episode state by exact external identity. Do not add any deletion
call in this step and do not expose a Plex library deletion method.

- [ ] **Step 6: Verify and commit live gates**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_live_gates.py tests\vod_filter\test_tv_sync_collector.py tests\vod_filter\test_sonarr_client.py tests\vod_filter\test_plex_tv_client.py -q
Pop-Location

git add workers/vod-filter/src workers/vod-filter/tests
git commit -m "feat(tv): recheck live season cleanup predicates"
```

### Task 7: Enforce Independent Season Gate And Destructive Cap

**Files:**
- Modify: `workers/vod-filter/src/config.py`
- Create: `workers/vod-filter/src/services/tv_cleanup_planner.py`
- Create: `workers/vod-filter/src/services/tv_cleanup_policy.py`
- Modify: `workers/vod-filter/example.env`
- Modify: `deploy/production/worker.env.example`
- Test: `workers/vod-filter/tests/vod_filter/test_config.py`
- Create: `workers/vod-filter/tests/vod_filter/test_tv_cleanup_planner.py`
- Create: `workers/vod-filter/tests/vod_filter/test_tv_cleanup_policy.py`

- [ ] **Step 1: Write failing configuration tests**

Require:

```dotenv
TV_SYNC_ALLOW_SEASON_FILE_DELETION=false
TV_SYNC_MAX_SEASON_CLEANUPS_PER_RUN=2
```

Validate the cap as an integer from 0 through 2. `TV_SYNC_APPLY=true` must not
imply the season switch. The terminal and no-recycle-bin switches must have no
effect on a season decision.

Replace Phase 3's temporary "reject every destructive switch" validation only
for the now-implemented season switch: Phase 4 accepts
`TV_SYNC_ALLOW_SEASON_FILE_DELETION=true`, but it must still reject startup when
`TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION` or
`TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE` is true. Checked-in/example values remain
false.

- [ ] **Step 2: Write failing planner/policy tests**

Plan at most one season decision per `(eventId, tvdbId, seasonNumber)`. When
three eligible season actions are proposed against the hard cap of two,
execute zero destructive TV actions for the entire run. Reversible actions may
continue only when their own collections are healthy and the report states
`destructive_cap_exceeded`.

Also block report-to-apply transition for stale manifest, mutation-capable
false, missing ownership, claim unavailable, any required collection failure,
season gate false, `source_removed`, `TV_SYNC_APPLY=false`, or
`TV_SYNC_ALLOW_SEASON_FILE_DELETION=false`. Global apply is necessary for any
external mutation but is never sufficient cleanup authorization.

- [ ] **Step 3: Run policy tests and confirm RED**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_config.py tests\vod_filter\test_tv_cleanup_planner.py tests\vod_filter\test_tv_cleanup_policy.py -q
Pop-Location
```

Expected: missing option/cap assertions fail.

- [ ] **Step 4: Implement policy without side effects**

The separate cleanup planner emits deterministic `TvCleanupDecision` values
even in report-only mode. The cleanup policy
marks destructive decisions `dry_run`, `blocked`, or `claim_required`; only the
cleanup executor changes state. The Phase 3 reversible planner, policy, and
`TvDestinationExecutor` retain their original destructive-free contracts.

- [ ] **Step 5: Verify and commit the gate**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_config.py tests\vod_filter\test_tv_cleanup_planner.py tests\vod_filter\test_tv_cleanup_policy.py -q
Pop-Location

git add workers/vod-filter/src workers/vod-filter/tests workers/vod-filter/example.env deploy/production/worker.env.example
git commit -m "feat(tv): gate and cap season cleanup plans"
```

### Task 8: Delete Exact Files, Audit Children, And Unmonitor The Season

**Files:**
- Create: `workers/vod-filter/src/services/tv_cleanup_executor.py`
- Modify: `workers/vod-filter/src/clients/sonarr_client.py`
- Modify: `workers/vod-filter/src/services/tv_state_store.py`
- Create: `workers/vod-filter/src/models/migrations/0002_tv_cleanup_state.sql`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_cleanup_executor.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_state_store.py`
- Test: `workers/vod-filter/tests/vod_filter/test_sonarr_client.py`

- [ ] **Step 1: Write failing Sonarr deletion tests**

Delete one file only through:

```text
DELETE /api/v3/episodefile/{episodeFileId}
```

Assert the exact numeric ID, verify the file is absent after the response, and
never send a path. Add a separate full-series resource update that leaves
`monitored=true`, leaves `monitorNewItems="all"`, leaves season 0 unmonitored,
and sets only the converged numbered season to `monitored=false`.

- [ ] **Step 2: Write failing executor and crash-recovery tests**

Prove:

- no deletion happens before a successful backend claim;
- after claim, the worker fetches a new snapshot and recollects live Sonarr and
  Plex state before the first mutation;
- immediately before every Sonarr mutation, the worker replays the exact claim
  as a live backend revalidation, requires the identical unextended lease, and
  blocks if current history now has a pending/conflicting post-cutover route,
  unresolved outbox row, or quarantine;
- a clean revalidation near expiry advances `LeaseValidatedAt` but retains the
  minimum local deadline for that authorization/lease, so the 60-second reserve
  blocks the mutation instead of resetting its budget;
- each unresolved file group is one child action and one DELETE at most;
- a successful child is persisted immediately in SQLite and reported;
- if a routing blocker revokes the backend lease after a Sonarr call but before
  its result, the worker still submits the exact already-issued child audit;
  the backend accepts it on the unexpired revoked lease and never treats that
  result channel as permission for another call;
- a crash after deletion but before reporting becomes `already_absent` on the
  next run through a new `reconcile_only` authorization without another DELETE;
- partial failure retries only unresolved original file IDs from fresh state
  and only under a new current-manifest `retry_actions` authorization;
- the season is unmonitored only after every file child converges;
- a zero-file season converges by unmonitoring;
- the series remains monitored with new seasons monitored; and
- result status is `partial`, `failed`, or `converged` with the exact lease,
  authorization, manifest, and live hash.

Use injected UTC and monotonic clocks to prove the worker renews its matching SQLite run lease
before every external boundary when half or less of its duration remains. Also
require at least 60 seconds of backend cleanup-lease time before each Sonarr
mutation and before result submission. If either lease check fails, persist a
blocked/partial audit and stop before the next Sonarr call; an expired backend
lease is never used or locally extended.

Inject those clocks and a local-lease heartbeat callback into
`TvCleanupExecutor`; do not read wall time through an untestable module-global
inside the gate or executor.

- [ ] **Step 3: Run executor tests and confirm RED**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_cleanup_executor.py tests\vod_filter\test_tv_state_store.py tests\vod_filter\test_sonarr_client.py -q
Pop-Location
```

Expected: failures for missing executor/delete method and child idempotency.

- [ ] **Step 4: Add an immutable second migration and its tests**

Never edit the checksum-protected Phase 3
`0001_tv_worker_state.sql`. Create `0002_tv_cleanup_state.sql` with this safe
audit surface:

```sql
CREATE TABLE IF NOT EXISTS tv_cleanup_observations (
    run_id TEXT NOT NULL,
    boundary TEXT NOT NULL,
    tvdb_id INTEGER NOT NULL,
    target_id TEXT NOT NULL,
    source_generation_id TEXT NOT NULL,
    lifecycle_version INTEGER NOT NULL,
    complete INTEGER NOT NULL,
    state_hash TEXT NOT NULL,
    observed_at TEXT NOT NULL,
    PRIMARY KEY (run_id, boundary, tvdb_id, target_id),
    CHECK (boundary IN (
        'sonarr',
        'plex_library',
        'path_inventory'
    )),
    CHECK (tvdb_id > 0),
    CHECK (lifecycle_version > 0),
    CHECK (complete IN (0, 1))
);

CREATE TABLE IF NOT EXISTS tv_cleanup_action_history (
    authorization_id TEXT NOT NULL,
    recovery_of_authorization_id TEXT,
    recovery_mode TEXT NOT NULL,
    event_id TEXT NOT NULL,
    action_id TEXT NOT NULL,
    authorization_manifest_id TEXT NOT NULL,
    source_generation_id TEXT NOT NULL,
    action_type TEXT NOT NULL,
    child_type TEXT NOT NULL,
    tvdb_id INTEGER NOT NULL,
    sonarr_series_id INTEGER NOT NULL,
    season_number INTEGER,
    lifecycle_version INTEGER NOT NULL,
    target_id TEXT NOT NULL,
    lease_id TEXT NOT NULL,
    original_predicate_hash TEXT NOT NULL,
    live_predicate_hash TEXT NOT NULL,
    terminal_path_fingerprint TEXT,
    status TEXT NOT NULL,
    destructive_call_issued INTEGER NOT NULL,
    delete_files INTEGER,
    add_import_list_exclusion INTEGER,
    episode_file_absent INTEGER,
    season_unmonitored INTEGER,
    series_absent INTEGER,
    terminal_inventory_empty INTEGER,
    reason TEXT NOT NULL,
    attempted_at TEXT NOT NULL,
    observed_at TEXT NOT NULL,
    completed_at TEXT,
    PRIMARY KEY (authorization_id, action_id),
    CHECK (recovery_mode IN (
        'initial',
        'reconcile_only',
        'retry_actions'
    )),
    CHECK (action_type IN ('season_files', 'terminal_series')),
    CHECK (child_type IN (
        'delete_episode_file',
        'unmonitor_season',
        'delete_series'
    )),
    CHECK (status IN (
        'started',
        'completed',
        'already_absent',
        'failed',
        'blocked'
    )),
    CHECK (destructive_call_issued IN (0, 1)),
    CHECK (delete_files IS NULL OR delete_files IN (0, 1)),
    CHECK (
        add_import_list_exclusion IS NULL
        OR add_import_list_exclusion IN (0, 1)
    ),
    CHECK (
        (
            child_type = 'delete_series'
            AND delete_files = 1
            AND add_import_list_exclusion = 0
        )
        OR (
            child_type <> 'delete_series'
            AND delete_files IS NULL
            AND add_import_list_exclusion IS NULL
        )
    ),
    CHECK (episode_file_absent IS NULL OR episode_file_absent IN (0, 1)),
    CHECK (season_unmonitored IS NULL OR season_unmonitored IN (0, 1)),
    CHECK (series_absent IS NULL OR series_absent IN (0, 1)),
    CHECK (
        terminal_inventory_empty IS NULL
        OR terminal_inventory_empty IN (0, 1)
    ),
    CHECK (tvdb_id > 0),
    CHECK (sonarr_series_id > 0),
    CHECK (lifecycle_version > 0)
);

CREATE INDEX IF NOT EXISTS idx_tv_cleanup_action_status
    ON tv_cleanup_action_history(event_id, status, observed_at);
CREATE INDEX IF NOT EXISTS idx_tv_cleanup_action_recovery
    ON tv_cleanup_action_history(recovery_of_authorization_id, observed_at);
CREATE INDEX IF NOT EXISTS idx_tv_cleanup_observation_generation
    ON tv_cleanup_observations(
        source_generation_id,
        lifecycle_version,
        observed_at
    );
```

Tests apply migrations 0001 then 0002, reconstruct the store, verify both
checksums, prove unique `(authorization_id, action_id)`, preserve every recovery
attempt under the original event, and confirm no column stores a
raw media path, token, API key, or response body. Phase 4 season rows require a
null `terminal_path_fingerprint`, `delete_files`, and
`add_import_list_exclusion`; the forward-compatible columns are populated only
by Phase 5. Persist each Phase 4 child's applicable nullable postcondition in
`episode_file_absent` or `season_unmonitored` and leave unrelated postconditions
null. The forward-compatible `series_absent` and `terminal_inventory_empty`
columns are populated only by Phase 5. Cleanup-observation tests cover complete Sonarr and Plex-library
boundaries now and a redacted `path_inventory` hash/count fixture for Phase 5;
every row carries the source generation and lifecycle version.

- [ ] **Step 5: Implement child-first durable execution**

Use unique SQLite `(authorization_id, action_id)` records while querying all
prior attempts by original event before any call. Name file children
`episode_file:{id}` and the final monitoring child
`season_monitor:{seriesId}:{seasonNumber}`. Never mark the backend
authorization converged before all required children are confirmed absent or
completed.

After an expired lease, including one previously mutation-revoked, load the
original durable series, season, expected-file, lifecycle, and target binding
plus every SQLite attempt for that event. Recollect
current non-deletion blockers and target identity, but do not rebuild the
pre-delete file inventory from whatever remains. An independently confirmed
absent child is reported `already_absent` with
`destructiveCallIssued=false` only under a new `reconcile_only` authorization.
A still-present original child can receive a new call only under a separate
current-manifest `retry_actions` authorization. A different live series ID,
revival/new episode, ownership change, lifecycle version, target binding, failed
local lease renewal, or insufficient backend lease time blocks recovery.

- [ ] **Step 6: Verify and commit exact season execution**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_cleanup_executor.py tests\vod_filter\test_tv_state_store.py tests\vod_filter\test_sonarr_client.py -q
Pop-Location

git add workers/vod-filter/src workers/vod-filter/tests
git commit -m "feat(tv): clean exact concluded-season files"
```

### Task 9: Integrate Report-Only And Apply Workflows

**Files:**
- Modify: `workers/vod-filter/sync_tv.py`
- Modify: `workers/vod-filter/src/services/tv_sync_report.py`
- Modify: `workers/vod-filter/healthcheck.py`
- Test: `workers/vod-filter/tests/vod_filter/test_sync_tv_cli.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_sync_report.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_workflow_simulation.py`

- [ ] **Step 1: Write failing orchestration tests**

Assert the order:

```text
local run lease
  -> initial backend/Sonarr/Plex collection
  -> renew local lease as required
  -> deterministic plan and cap policy
  -> authorization claim
  -> new backend/Sonarr/Plex collection
  -> final live-gate/hash check
  -> renew local lease and check backend lease budget before every exact child
  -> local audit plus backend result
  -> redacted report/heartbeat
  -> local lease release
```

The local lease is renewed throughout collection/execution and releases on
success, dry run, blocker, exception, and interruption. Renewal failure or less
than 60 seconds remaining on the backend lease stops before the next mutation.
Local reporting still succeeds if backend run-summary reporting fails.
`TvDestinationExecutor` runs only the Phase 3 reversible evaluation;
`TvCleanupExecutor` receives only a separately gated `TvCleanupDecision` after
`TV_SYNC_APPLY`, the season switch, cap, claim, and final live recheck all pass.

- [ ] **Step 2: Write the season workflow simulation**

Cover a fully watched concluded season, partial multi-episode watch, Mark
Unwatched reset, future episode blocker, stale evidence, cap overflow, crash
after one file deletion, zero-file convergence, and a new episode re-monitoring
a previously cleaned season. Assert movie planner output is unchanged.

- [ ] **Step 3: Run orchestration tests and confirm RED**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sync_tv_cli.py tests\vod_filter\test_tv_sync_report.py tests\vod_filter\test_tv_workflow_simulation.py -q
Pop-Location
```

Expected: failures until the executor is wired behind the independent switch.

- [ ] **Step 4: Implement redacted reporting and stable exit codes**

Reports include generation, event/lease IDs, collection health/counts, feature
gate values, cap result, deterministic decisions, child statuses, convergence,
and stable blockers. They omit tokens, API keys, OAuth data, raw response
bodies, and full paths. Exit codes distinguish success, policy block, partial
execution, and collection failure.

The worker-run summary continues to use `POST /api/worker/tv/runs` and must
receive the Phase 3 `202 Accepted` `{runId, acceptedAt}` response even when the
request includes cleanup observations/results. Do not regress it to 200.

- [ ] **Step 5: Verify and commit workflow integration**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sync_tv_cli.py tests\vod_filter\test_tv_sync_report.py tests\vod_filter\test_tv_workflow_simulation.py -q
python -m compileall -q src sync_tv.py continuous_sync.py healthcheck.py
Pop-Location

git add workers/vod-filter
git commit -m "feat(tv): orchestrate guarded season cleanup"
```

### Task 10: Document Authority And Perform A Supervised Rollout

**Files:**
- Create: `docs/decisions/tv_media_cleanup_authority.md`
- Modify: `docs/decisions/index.md`
- Modify: `docs/architecture/tv_sync_production.md`
- Modify: `docs/apis/backend_api.md`
- Modify: `docs/apis/export_endpoints.md`
- Modify: `docs/integrations/plex.md`
- Modify: `docs/integrations/sonarr.md`
- Modify: `docs/data_models/tv_lifecycle_event.md`
- Modify: `docs/systems/vod_filter_worker.md`
- Modify: `docs/runbooks/tv_sync_operations.md`
- Modify: `docs/runbooks/validation.md`
- Modify: `docs/reports/tv_integration_rollout.md`
- Modify: `docs/backlog/roadmap.md`
- Modify: `docs/log.md`
- Modify: `.github/workflows/movie-ci.yml`
- Create: `tests/deployment/test_tv_phase4_deployment.py`

- [ ] **Step 1: Add the explicit season-deletion decision**

Authorize only Sonarr episode-file deletion for a concluded, fully watched
numbered season under the exact evidence, ownership, grace, claim, recheck,
cap, audit, and feature-gate rules in the approved design. Explicitly retain
the ban on Plex library deletion and on independent season-0 cleanup.

- [ ] **Step 2: Extend CI with focused TV cleanup tests**

Run backend lifecycle/authorization tests, worker live-gate/executor/simulation
tests, deployment defaults, secret scanning, and existing movie tests. Do not
rename the `Movie CI` workflow because the exact-SHA deployer relies on it.
The deployment test asserts the checked-in season, terminal, and no-recycle
switches are false, there is still no `/app/tv-ro` media mount, and the Phase 3
SQLite migration checksum is not changed.

- [ ] **Step 3: Run complete pre-rollout validation**

```powershell
python tests\validate_okf.py
dotnet test backend\Watchlist.sln --configuration Release

Push-Location workers\vod-filter
python -m pytest -q
python -m compileall -q src sync_tv.py continuous_sync.py healthcheck.py
Pop-Location

python -m pytest tests\deployment -q
git diff --check
```

Expected: every command passes while the checked-in season deletion gate is
false.

- [ ] **Step 4: Observe report-only eligibility for seven continuous days**

Record every candidate generation, observed duration, cancellation, exact
file grouping, configured-account evidence age, ownership, cap, and live gate.
A source gap longer than two hours or mutation-incapable scheduled generation
restarts the observation window.

- [ ] **Step 5: Execute one supervised season cleanup**

Set the runtime season cap to `1`, require `TV_SYNC_APPLY=true`, enable only
`TV_SYNC_ALLOW_SEASON_FILE_DELETION=true`, keep the terminal and no-recycle
switches false, and run one apply cycle. Immediately disable the season switch,
run a second convergence cycle, and inspect Sonarr, the
SQLite child audit, Mongo lifecycle event/authorization, and backend result.
Do not arm terminal deletion.

- [ ] **Step 6: Record evidence and commit documentation**

```powershell
git add docs .github/workflows/movie-ci.yml tests/deployment/test_tv_phase4_deployment.py
git commit -m "docs(tv): authorize and record season cleanup rollout"
```
