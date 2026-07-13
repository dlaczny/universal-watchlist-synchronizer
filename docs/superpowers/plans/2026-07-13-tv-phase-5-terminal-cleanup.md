---
type: Backlog
title: TV Phase 5 Terminal-Series Cleanup And Revival Implementation
description: TDD plan for ended/canceled show authorization, read-only media verification, exact Sonarr whole-series deletion, crash recovery, revival, and supervised rollout.
tags:
  - tv
  - trakt
  - sonarr
  - cleanup
  - revival
timestamp: 2026-07-13T00:00:00Z
version: 0.1.0
---

# TV Phase 5 Terminal-Series Cleanup And Revival Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove one exact fully watched `ended` or `canceled` TV series and its files from Sonarr only after complete source evidence, seven continuously observed eligible days, a read-only filesystem inventory, recycle-bin verification, one-use authorization, and a final live recheck; later restore Sonarr management if the show revives.

**Architecture:** The backend refreshes terminal candidates from Trakt on every scheduled generation, maintains immutable candidate/revival lifecycle events, and publishes a short-lived terminal cleanup authorization. The worker binds that event to current Sonarr, configured-account Plex, recycle-bin, and read-only filesystem facts; enforces ownership and a one-series cap; claims once; calls the exact Sonarr delete endpoint; verifies absence; and audits convergence. Retired rows remain refreshable, while Plex watchlist restoration remains governed separately by explicit Trakt watchlist membership or an aired unwatched episode.

**Tech Stack:** .NET 10, MongoDB 8, Trakt API, Python 3.11, httpx, SQLite, Sonarr API v3, Plex API, read-only Docker bind mounts, xUnit, FluentAssertions, and pytest.

---

## Prerequisites

Complete Phases 1 through 4, including one supervised season cleanup and its
immediate convergence run. Keep terminal deletion and the no-recycle-bin
override false until the final supervised step:

```dotenv
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false
```

### Task 1: Evaluate Exact Terminal Status And Revival

**Files:**
- Create: `backend/src/Watchlist.Application/TvTerminalCleanupDecision.cs`
- Create: `backend/src/Watchlist.Application/TvTerminalCleanupEvaluator.cs`
- Modify: `backend/src/Watchlist.Application/TvLifecycleEvaluator.cs`
- Modify: `backend/src/Watchlist.Domain/TvShow.cs`
- Modify: `backend/src/Watchlist.Application/TvCleanupCandidateState.cs`
- Modify: `backend/src/Watchlist.Application/TvSyncService.cs`
- Modify: `backend/src/Watchlist.Application/TvBlockerCodes.cs`
- Modify: `backend/src/Watchlist.Infrastructure/TraktTvClient.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvShowDocument.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TraktTvClientTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvTerminalCleanupEvaluatorTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvLifecycleEvaluatorTests.cs`

- [ ] **Step 1: Write failing exact-status tests**

Test every known status string. Only ordinal exact `ended` and `canceled` are
terminal. Do not trim, lowercase, or otherwise normalize upstream values:
`Ended`, `CANCELED`, `cancelled`, ` ended `, `returning series`, `continuing`,
`planned`, `upcoming`, `pilot`, `in production`, empty, null, and unknown values
are nonterminal or unknown and cannot start terminal grace.

For a terminal candidate, assert each `scheduled_full` generation refetches
full show metadata with `extended=full`; two qualifying generations may not
reuse one cached status response. A status fetch failure makes the generation
mutation-incapable and does not advance the candidate.

- [ ] **Step 2: Write failing source-predicate tests**

The positive entry case requires two distinct consecutive hourly
`scheduled_full` generations, not rapid retries or activity generations, both
proving:

- status exactly terminal;
- `aired > 0` and `completed == aired`;
- no next episode;
- no explicit current Trakt watchlist membership;
- exact verified TVDB identity; and
- no pending, leased, ambiguous, retry-wait, dead-letter Trakt outbox row or
  quarantined Plex event.

The lifecycle must be current `active` or `caught_up`; `source_removed` is an
explicit blocker even when retained historical progress and an old terminal
status happen to match.

Create one negative test for every predicate and prove one terminal snapshot
does not start the seven-day clock.

- [ ] **Step 3: Write failing revival tests**

Cover:

- retired show plus aired unwatched episode -> `active` and one `reactivated`
  event;
- retired show explicitly re-added to Trakt watchlist -> `active` and one
  `reactivated` event;
- retired `ended`/`canceled` show changes to nonterminal with no aired unwatched
  episode -> `caught_up` and one `reactivated` event;
- caught-up terminal candidate becomes nonterminal -> candidate reset and
  `cleanup_canceled`;
- replaying the same generation creates no duplicate event; and
- a converged old terminal authorization is never renewed during revival.

- [ ] **Step 4: Run the focused tests and confirm RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TraktTvClientTests|FullyQualifiedName~TvTerminalCleanupEvaluatorTests|FullyQualifiedName~TvLifecycleEvaluatorTests"
```

Expected: compile/assertion failures for the missing terminal evaluator and
fresh-status behavior.

- [ ] **Step 5: Implement pure status/candidate transitions**

```csharp
public sealed record TvTerminalCleanupDecision(
    bool Eligible,
    int ConsecutiveTerminalScheduledGenerations,
    DateTimeOffset? CandidateSince,
    TimeSpan EligibleObservedDuration,
    string PredicateHash,
    IReadOnlyList<string> Blockers,
    TvLifecycleEvent? Event);

public sealed class TvTerminalCleanupEvaluator
{
    public TvTerminalCleanupDecision Evaluate(
        TvShow current,
        TvShowCleanupEvidence evidence,
        TvGenerationManifest manifest,
        DateTimeOffset now);
}
```

After the two-generation entry proof, accrue only intervals between
mutation-capable scheduled generations no more than two hours apart. Seven
days means seven observed eligible days. Activity generations can cancel but
never accrue. Compare Trakt status with `StringComparer.Ordinal`; do not call
`Trim`, `ToLower`, or `ToUpper` on it.

This evaluator is deliberately source-scoped and does not consume Phase 4's
season-scoped `TvWorkerCleanupObservationDto`. It may advance or reset terminal
candidate continuity, but it cannot bind a Sonarr target or publish deletion
permission. Task 2 requires a separate complete show-level worker observation
before it creates a terminal authorization.

Its `PredicateHash` uses canonical stable source eligibility facts only. Do not
include generation IDs, observation timestamps, or grace counters in the
continuity hash; validate freshness and generation adjacency separately so
unchanged facts can accrue across scheduled generations.

- [ ] **Step 6: Verify and commit terminal lifecycle evaluation**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TraktTvClientTests|FullyQualifiedName~TvTerminalCleanupEvaluatorTests|FullyQualifiedName~TvLifecycleEvaluatorTests"
git add backend/src backend/tests
git commit -m "feat(tv): evaluate terminal cleanup and revival"
```

### Task 2: Publish And Lease Terminal Cleanup Authorizations

**Files:**
- Modify: `backend/src/Watchlist.Application/TvCleanupAuthorization.cs`
- Modify: `backend/src/Watchlist.Application/TvCleanupAuthorizationService.cs`
- Modify: `backend/src/Watchlist.Application/TvCleanupClaimRequestDto.cs`
- Modify: `backend/src/Watchlist.Application/TvCleanupResultRequestDto.cs`
- Create: `backend/src/Watchlist.Application/WorkerTvTerminalCandidateDto.cs`
- Modify: `backend/src/Watchlist.Application/WorkerTvShowDto.cs`
- Create: `backend/src/Watchlist.Application/TvWorkerShowCleanupObservationDto.cs`
- Modify: `backend/src/Watchlist.Application/TvWorkerRunSummaryRequestDto.cs`
- Modify: `backend/src/Watchlist.Application/TvWorkerRunService.cs`
- Modify: `backend/src/Watchlist.Application/ITvWorkerRunRepository.cs`
- Modify: `backend/src/Watchlist.Application/TvSyncService.cs`
- Modify: `backend/src/Watchlist.Application/TvExportService.cs`
- Modify: `backend/src/Watchlist.Application/WorkerTvCleanupAuthorizationDto.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvWorkerRunDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvWorkerRunRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvGenerationRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvCleanupAuthorizationRepository.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvCleanupAuthorizationServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvGenerationRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvCleanupAuthorizationRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvExportServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvWorkerRunServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvWorkerRunRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvSyncServiceTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/TvWorkerApiTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/TvWorkerContractTests.cs`
- Modify: `contracts/tv/worker-sync-state-v1.json`
- Modify: `workers/vod-filter/src/models/tv_destination.py`
- Modify: `workers/vod-filter/src/models/tv_sync.py`
- Modify: `workers/vod-filter/src/clients/tv_backend_client.py`
- Modify: `workers/vod-filter/src/services/tv_sync_report.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_backend_client.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_sync_report.py`

- [ ] **Step 1: Write failing authorization-publication tests**

First evolve the version-1 worker snapshot with an exact terminal-candidate
handoff on every show:

```csharp
public sealed record WorkerTvTerminalCandidateDto(
    int ContractVersion,
    string State,
    int ConsecutiveScheduledGenerations,
    DateTimeOffset? CandidateSince,
    double EligibleObservedHours);
```

`ContractVersion` is exactly `1`; `State` is exactly `none`, `observing`,
`evidence_required`, or `authorized`. `none` requires zero/null counters,
`observing` requires the two-generation entry proof but less than seven observed
days, `evidence_required` means source grace passed and a complete show-level
worker observation is needed, and `authorized` requires a pending exported
terminal authorization. Extend `WorkerTvShowDto`, the sole shared fixture,
backend contract tests, Python strict parser, and malformed-contract tests.
Unknown versions/states or impossible field combinations reject the complete
snapshot. The worker publishes `terminalCleanupObservations` only for
`evidence_required`/`authorized` shows; it performs the expensive read-only path
inventory only for `authorized` shows with a matching pending authorization.

After seven observed days, require this distinct complete show-level worker
observation before publishing exactly one `terminal_cleanup_authorized` event:

```csharp
public sealed record TvWorkerShowCleanupObservationDto(
    string SourceGenerationId,
    long LifecycleVersion,
    int TvdbId,
    int SonarrSeriesId,
    string Ownership,
    DateTimeOffset CollectedAt,
    bool SonarrCollectionComplete,
    int FutureRegularEpisodes,
    int UnknownAirDateRegularEpisodes,
    int DownloadedSpecialFiles,
    int UnmappedFileGroups,
    IReadOnlyList<TvWorkerEpisodeFileObservationDto> EpisodeFiles);
```

It represents the complete exact series, not one season: every numbered-season
file and downloaded special is included once, and partial collection is never
encoded as an empty inventory. Extend the protected worker-run body with
`terminalCleanupObservations`; validate and persist it with the same redaction,
source-generation, ownership, lifecycle, and 202-response rules as Phase 4.
Reject duplicate file IDs/episode mappings, `SonarrCollectionComplete=false`,
nonzero future/unknown-air-date regular episodes, nonzero unmapped groups,
stale collection time, wrong exact TVDB/Sonarr ownership binding, or an
observation sourced from anything except the draft manifest's
`PreviousGenerationId`.

Before publication, join every observed episode key in every file group,
including all season-0 specials, to both an accepted configured-account
`plex_watch_events` ledger row whose durable `Disposition == Accepted`
regardless of bootstrap outcome, and the fresh current Plex library snapshot with
`viewCount > 0`. Require all keys in a multi-episode file to pass, require the
observed downloaded-special count to agree with the mapped special groups, and
fail closed on missing, duplicate, quarantined, unwatched, or ambiguous
evidence. Put the stable per-episode watched/evidence facts in the terminal
authorization predicate hash while keeping ledger/Plex collection timestamps
as separately validated freshness fields. Add publication negatives for one
unwatched special, one missing special ledger row, and one partially watched
multi-episode file; none may publish or lease terminal cleanup.

Only then publish exactly one `terminal_cleanup_authorized`
event and one pending projection bound to Trakt ID, exact TVDB ID, lifecycle
version, exact observed Sonarr series ID, current generation, expected
`aired/completed`, predicate hash,
configured-account Plex evidence time/watermark, authorization time, and a
30-minute expiry. Terminal authorization suppresses season authorizations for
that show in the same generation.

Populate the Phase 4 `TvCleanupIntentPayload` on this immutable event with
`ActionType=TerminalSeries`, null season, sorted complete-series file IDs, and a
non-null canonical `ExpectedInventoryHash` computed from the full show-level
observation. Its typed progress, candidate/authorization, source generation,
worker observation, and Plex evidence fields must match the projection exactly;
any event/projection mismatch makes publication mutation-incapable.

The qualifying complete show-level worker observation must name the draft manifest's
`PreviousGenerationId` and the unchanged lifecycle version. The published
terminal authorization names the newly published current generation. A
`source_removed` show cannot create or refresh this projection.

Contradictory later source facts cancel the pending projection. Claimed,
canceled, expired, or converged projections are never silently refreshed.
Current Phase 2 history blockers are also immediate cancellation facts: a
pending/conflicting post-cutover route, unresolved outbox row, or quarantined
Plex event cancels an active pending mutation-bearing terminal projection. If it is already
leased, retain the lease/result channel but set Phase 4's immutable mutation-
revocation fields; the same-worker claim revalidation returns `409` and grants
no further call. Phase 4's strictly result-only `reconcile_only` exception is
unchanged and never grants a terminal DELETE.

Extend Phase 4's scheduled-generation `TvSyncServiceTests` for terminal
projections: a new routing blocker cancels a pending mutation-bearing terminal
authorization, mutation-revokes a leased terminal authorization while retaining
its result channel, leaves audit-only reconciliation available, and publishes
no replacement terminal permission.

- [ ] **Step 2: Write failing terminal-claim tests**

Require `actionType == "terminal_series"`, exact expected Sonarr series ID,
current manifest ID, a nonempty canonical `terminalPathFingerprint`, and the
worker's `livePredicateHash`. Reject a season number, wrong TVDB/series binding,
stale manifest, mutation-incapable manifest, mismatched predicate, empty path
fingerprint, or a non-pending projection for every new/different-worker claim.
An exact same-worker replay of the one active leased projection is the sole
exception: it revalidates current gates and returns the same durable lease when
clean, with no expiry extension.

The ten-minute lease and concurrent one-winner rule are unchanged from Phase
4: one durable lease/worker may produce multiple idempotent successful
revalidation responses. A terminal claim may send an empty `expectedEpisodeFileIds` list because
the path fingerprint and live facts bind the complete series inventory.
Add the terminal form of Phase 4's current-health test: one newly pending or
conflicting post-cutover route after initial claim makes an exact same-worker
claim replay return `409 cleanup_authorization_revoked` without extending the
lease, and no terminal child may execute. Also prove a clean exact replay succeeds with the identical lease,
advances `LeaseValidatedAt`, and retains the minimum original monotonic deadline
near expiry rather than resetting ten minutes.

- [ ] **Step 3: Write failing convergence tests**

Accept a terminal target child status of `completed` or `already_absent`, then
mark the authorization `converged` and publish `terminal_cleanup_completed`.
Persist the expected query values `deleteFiles=true` and
`addImportListExclusion=false` in the nullable strict boolean fields already
created by Phase 4's result DTO, Mongo child document, and immutable SQLite
`0002_tv_cleanup_state.sql`. A later observed Sonarr reappearance creates
`destination_drift`, not another deletion event.

Inherit Phase 4's post-lease revocation result semantics. If the terminal DELETE
completed before a new routing blocker revoked the lease, accept the exact
same-worker child result while the original lease remains unexpired and
converge only from strict series-absent/inventory-empty postconditions. The
revoked result channel never grants another DELETE; after expiry, recovery is
reconcile-only while the blocker remains.

- [ ] **Step 4: Run the tests and confirm RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvCleanupAuthorizationServiceTests|FullyQualifiedName~MongoTvGenerationRepositoryTests|FullyQualifiedName~MongoTvCleanupAuthorizationRepositoryTests|FullyQualifiedName~TvExportServiceTests|FullyQualifiedName~TvWorkerRunServiceTests|FullyQualifiedName~MongoTvWorkerRunRepositoryTests|FullyQualifiedName~TvSyncServiceTests"
```

Expected: terminal-action assertions fail until the shared authorization path
supports the second action type.

- [ ] **Step 5: Implement terminal projection/export/claim/result behavior**

Keep lifecycle events immutable. Map a converged show to
`retired_terminal` only after the worker result proves the current exact Sonarr
target absent. Browse retains the row under `state=retired`; export retains it
for daily status/schedule refresh and revival.

Retain the Phase 4 claim response and child/result contracts exactly. Terminal
results use child type `delete_series`, target ID equal to the authorized
Sonarr series ID, one of the four child statuses, a stable reason,
`destructiveCallIssued`, `observedAt`, and the nullable `seriesAbsent`/
`terminalInventoryEmpty` post-query booleans plus strict
`deleteFiles=true`/`addImportListExclusion=false`. The response returns projection
state, parent status `partial|failed|converged`, and unresolved action IDs.

- [ ] **Step 6: Verify API and shared-contract behavior, then commit**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvCleanupAuthorizationServiceTests|FullyQualifiedName~MongoTvGenerationRepositoryTests|FullyQualifiedName~MongoTvCleanupAuthorizationRepositoryTests|FullyQualifiedName~TvExportServiceTests|FullyQualifiedName~TvWorkerRunServiceTests|FullyQualifiedName~MongoTvWorkerRunRepositoryTests|FullyQualifiedName~TvSyncServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvWorkerApiTests|FullyQualifiedName~TvWorkerContractTests"

Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_backend_client.py tests\vod_filter\test_tv_sync_report.py -q
Pop-Location

git add backend/src backend/tests contracts/tv workers/vod-filter/src workers/vod-filter/tests
git commit -m "feat(tv): authorize exact terminal-series cleanup"
```

Expected: the safety-critical application filter, both backend API/contract
suites, and both Python consumer/report suites pass against the same modified
shared fixture. This phase owns and
commits the terminal-candidate contract evolution; the later program
checkpoint only reads and verifies that fixture.

### Task 3: Configure And Verify Read-Only TV Root Mappings

**Files:**
- Create: `workers/vod-filter/src/services/tv_path_verifier.py`
- Modify: `workers/vod-filter/src/config.py`
- Modify: `workers/vod-filter/example.env`
- Modify: `deploy/production/worker.env.example`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_path_verifier.py`
- Test: `workers/vod-filter/tests/vod_filter/test_config.py`

- [ ] **Step 1: Write failing configuration tests**

Use one canonical option:

```dotenv
TV_SYNC_ROOT_MAPPINGS_JSON=[{"sonarrRoot":"/tv","workerRoot":"/app/tv-ro"}]
```

Treat an absent, blank, or `[]` value as unconfigured. For a nonempty mapping,
reject invalid JSON, relative roots, duplicate or overlapping Sonarr roots,
duplicate worker roots, ambiguous longest-prefix
matches, and any `workerRoot` that is not `/app/tv-ro` or a canonical descendant
of it. The environment option itself is optional: an absent/empty mapping must
not stop report-only startup, but every terminal candidate receives the stable
blocker `terminal_root_mapping_unconfigured` and cannot be claimed or executed.
Movie-only and Phase 3/4 configurations therefore remain valid.

- [ ] **Step 2: Write failing containment and mount tests**

Cover canonical longest-root translation, `..` traversal, separator tricks,
case behavior appropriate to the running host, resolved symlink escape,
missing source/translated roots, a translated path outside the selected root,
and a writable production mount. Production verification must fail unless the
selected filesystem mount is read-only. Determine read-only state from injected
mount metadata (`/proc/self/mountinfo` plus `statvfs(...).f_flag & ST_RDONLY` on
Linux), not by attempting to create, modify, rename, or delete a probe file.
Tests supply synthetic mount metadata for `ro`, `rw`, nested, missing, and
ambiguous mount cases.

- [ ] **Step 3: Write failing inventory/fingerprint tests**

Require every Sonarr episode file to exist and every discovered media file to
be represented by Sonarr. Count known sidecars in the audit but do not treat
them as untracked media. Reject untracked media and unknown subdirectories.
A complete empty Sonarr inventory plus empty filesystem is a valid zero-media
inventory.

The SHA-256 fingerprint is stable across enumeration order and changes when a
relative path, file size, or Sonarr file ID changes. Reports expose counts and
the hash, never absolute/full paths.

- [ ] **Step 4: Run path tests and confirm RED**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_config.py tests\vod_filter\test_tv_path_verifier.py -q
Pop-Location
```

Expected: failures for the missing mapping parser and verifier.

- [ ] **Step 5: Implement read-only translation and inventory**

```python
@dataclass(frozen=True)
class TvPathInventory:
    sonarr_series_id: int
    media_file_count: int
    sidecar_count: int
    untracked_media_count: int
    unknown_directory_count: int
    fingerprint: str
    complete: bool
```

The verifier reads metadata and directory entries only. It never opens a file
for writing, renames, moves, deletes, or changes permissions.

Parse configured mappings independently from terminal feature switches. Only
for a show whose worker handoff state is `authorized` and whose pending terminal
authorization matches the current generation, resolve the longest canonical Sonarr root, require
its worker root under `/app/tv-ro`, prove the translated path remains contained,
and locate the most-specific covering mount. Accept it only when both mount
metadata sources report read-only. Missing mappings, roots, or metadata become
redacted blockers in report-only mode; they are not configuration shortcuts
that make a terminal action disappear.

Persist only the resulting counts, completeness flag, fingerprint,
`sourceGenerationId`, and `lifecycleVersion` in Phase 4's
`tv_cleanup_observations` row with boundary `path_inventory`; do not alter
either applied SQLite migration.

- [ ] **Step 6: Verify and commit path safety**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_config.py tests\vod_filter\test_tv_path_verifier.py -q
Pop-Location

git add workers/vod-filter/src workers/vod-filter/tests workers/vod-filter/example.env deploy/production/worker.env.example
git commit -m "feat(tv): verify terminal media through read-only roots"
```

### Task 4: Collect Recycle-Bin, Specials, And Complete Series State

**Files:**
- Modify: `workers/vod-filter/src/clients/sonarr_client.py`
- Modify: `workers/vod-filter/src/clients/plex_tv_client.py`
- Modify: `workers/vod-filter/src/services/tv_sync_collector.py`
- Modify: `workers/vod-filter/src/models/tv_destination.py`
- Test: `workers/vod-filter/tests/vod_filter/test_sonarr_client.py`
- Test: `workers/vod-filter/tests/vod_filter/test_plex_tv_client.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_sync_collector.py`

- [ ] **Step 1: Write failing Sonarr collection tests**

Read the exact series, all episodes, all episode files, root folder, and
`GET /api/v3/config/mediamanagement`. Parse a nonempty recycle-bin path as
configured; missing, empty, malformed, or failed reads are unknown/blocked.
Collect Sonarr's next-airing fact and fail closed when it disagrees with the
backend or cannot be determined.

- [ ] **Step 2: Write failing specials tests**

Enumerate every downloaded season-0 episode file. Each linked special must map
exactly to a configured-account Plex episode currently played and to an
accepted ledger event. One unwatched, missing, ambiguous, or conflicting
special blocks whole-series cleanup. Season 0 remains excluded from
independent cleanup/search.

- [ ] **Step 3: Write failing zero-file and boundary-health tests**

Zero-file terminal eligibility requires complete Sonarr episode/file
collection, complete configured-account Plex collection, and a complete empty
filesystem inventory. A failed boundary is never converted into an empty safe
list. Path inventory runs only for terminal candidates and has its own health
flag.

- [ ] **Step 4: Run collection tests and confirm RED**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sonarr_client.py tests\vod_filter\test_plex_tv_client.py tests\vod_filter\test_tv_sync_collector.py -q
Pop-Location
```

Expected: failures for recycle-bin, specials, next-airing, and path inventory
facts.

- [ ] **Step 5: Implement collection without mutation**

Bind Plex machine/account/library to the backend snapshot and Sonarr series
to exact TVDB ID. Include no Plex library delete method. Record only counts,
stable IDs, watched booleans, health flags, and the path fingerprint in the
collected state/report.

- [ ] **Step 6: Verify and commit complete terminal collection**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sonarr_client.py tests\vod_filter\test_plex_tv_client.py tests\vod_filter\test_tv_sync_collector.py -q
Pop-Location

git add workers/vod-filter/src workers/vod-filter/tests
git commit -m "feat(tv): collect complete terminal-series evidence"
```

### Task 5: Enforce Terminal Live Gates And One-Series Cap

**Files:**
- Modify: `workers/vod-filter/src/services/tv_live_gates.py`
- Modify: `workers/vod-filter/src/models/tv_cleanup.py`
- Modify: `workers/vod-filter/src/services/tv_cleanup_planner.py`
- Modify: `workers/vod-filter/src/services/tv_cleanup_policy.py`
- Modify: `workers/vod-filter/src/config.py`
- Modify: `workers/vod-filter/example.env`
- Modify: `deploy/production/worker.env.example`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_live_gates.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_cleanup_planner.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_cleanup_policy.py`
- Test: `workers/vod-filter/tests/vod_filter/test_config.py`

- [ ] **Step 1: Write failing switch and cap tests**

```dotenv
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false
TV_SYNC_MAX_TERMINAL_SERIES_DELETIONS_PER_RUN=1
```

Validate the cap as `0..1`. Neither `TV_SYNC_APPLY` nor the season-deletion
switch implies terminal deletion. The no-recycle-bin override bypasses only
the missing recycle-bin blocker and no other fact.

Replace Phase 4's temporary rejection of terminal/no-recycle switches now that
their implementation exists. Phase 5 configuration accepts the terminal and
no-recycle switches independently, while every checked-in/example value stays
false. An enabled no-recycle override has no effect unless a separately
eligible terminal decision also has global apply and terminal deletion enabled.

- [ ] **Step 2: Write the complete failing terminal-gate matrix**

Require:

- every applicable Phase 4 gate: current authorization and mutation-capable
  manifest, previous-generation worker observation with unchanged lifecycle,
  exact identity/ownership/series binding, `source_removed` false, no explicit
  Trakt watchlist membership, zero pending/conflicting post-cutover routes, no
  unresolved outbox/quarantined Plex event,
  accepted configured-account Plex ledger evidence plus current played state,
  complete fresh Sonarr/Plex collections, no unknown mapping, no future or
  unknown-air-date regular episode, and no source/Sonarr next-episode
  disagreement;
- current published mutation-capable manifest and authorization under 30
  minutes;
- current Trakt status ordinal-exact `ended` or `canceled`, the two-generation
  entry proof, and seven continuously observed eligible days;
- exact authorized TVDB ID and expected current Sonarr series ID;
- owned/adopted destination;
- Sonarr reports no next airing and does not disagree with Trakt;
- complete configured-account Plex collection;
- every numbered-season file and every downloaded special maps to watched
  episodes;
- no unknown/conflicting file mapping;
- complete read-only filesystem inventory with no untracked media/unknown
  directory;
- configured Sonarr recycle bin unless the independent override is true;
- a canonical path fingerprint matching the claim; and
- `TV_SYNC_APPLY=true`, the terminal feature switch true, and the final plan
  within both destructive caps.

Add one test per blocker and the complete zero-file positive case.

- [ ] **Step 3: Write cap-overflow and priority tests**

If two terminal actions are proposed, execute zero destructive actions in the
run, including otherwise eligible season cleanup. Terminal cleanup suppresses
season cleanup for the same show; if terminal is not authorized, an
independently authorized older season may proceed.

- [ ] **Step 4: Run policy tests and confirm RED**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_config.py tests\vod_filter\test_tv_live_gates.py tests\vod_filter\test_tv_cleanup_planner.py tests\vod_filter\test_tv_cleanup_policy.py -q
Pop-Location
```

Expected: failures for missing terminal-specific switch, cap, and gates.

- [ ] **Step 5: Implement canonical terminal facts and policy**

Include stable generation/event/identity, series ID, all file/episode watched
facts, specials, next-airing facts, Plex binding, recycle-bin boolean, and path
fingerprint in `livePredicateHash`. Do not include collection timestamps,
secrets, or full paths. Validate and persist each source/Sonarr/Plex/path
collection timestamp separately at the initial and final checks; both sets must
be fresh even though only their unchanged semantic facts are hash-compared.

Extend only the Phase 4 `TvCleanupDecision`/cleanup action enums with the
terminal series case. The reversible `TvDecision`, `TvActionType`, planner,
policy, and `TvDestinationExecutor` remain destructive-free; only
`TvCleanupExecutor` receives the terminal decision after every gate passes.

- [ ] **Step 6: Verify and commit terminal policy**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_config.py tests\vod_filter\test_tv_live_gates.py tests\vod_filter\test_tv_cleanup_planner.py tests\vod_filter\test_tv_cleanup_policy.py -q
Pop-Location

git add workers/vod-filter/src workers/vod-filter/tests workers/vod-filter/example.env deploy/production/worker.env.example
git commit -m "feat(tv): gate and cap terminal-series cleanup"
```

### Task 6: Execute The Exact Sonarr Terminal Delete With Crash Recovery

**Files:**
- Modify: `workers/vod-filter/src/clients/sonarr_client.py`
- Modify: `workers/vod-filter/src/services/tv_cleanup_executor.py`
- Modify: `workers/vod-filter/src/services/tv_state_store.py`
- Test: `workers/vod-filter/tests/vod_filter/test_sonarr_client.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_cleanup_executor.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_state_store.py`

- [ ] **Step 1: Write the exact HTTP contract test**

The only whole-series mutation is:

```text
DELETE /api/v3/series/{id}?deleteFiles=true&addImportListExclusion=false
```

Assert lowercase boolean query values, exact numeric series ID, no broad
lookup/delete by title, and a follow-up exact-TVDB/series-ID read proving the
target absent.

- [ ] **Step 2: Write failing executor tests**

Prove no DELETE before a successful terminal claim and final recollection.
Test ownership, fingerprint, recycle-bin, specials, next-airing, manifest,
evidence, and cap rechecks immediately before the call. Immediately before the
DELETE, replay the exact same-worker terminal claim as Phase 4's live backend
revalidation and require the identical, unextended lease with zero current
pending/conflicting post-cutover routes, unresolved outbox rows, or quarantine.
Persist the exact request facts and target child before/after state without full
paths. Inherit Phase 4's lease rules: renew the matching SQLite run lease as
needed and require at least 60 seconds on the backend authorization lease
immediately before the Sonarr DELETE and before result submission. A canceled,
failed, or short revalidation/lease stops without calling Sonarr.

Advance the injected clocks to replay near expiry and prove
`LeaseValidatedAt` plus the stored minimum deadline preserves only the original
remaining budget. Also inject a routing blocker after a successful DELETE but
before result submission: the backend marks the lease mutation-revoked yet
accepts the exact already-issued terminal child audit/convergence result, and
the executor makes no second DELETE.

- [ ] **Step 3: Write crash and drift tests**

A crash after Sonarr deletes the series but before backend result reporting is
recovered under a newly created current-manifest `reconcile_only`
authorization as `already_absent`; the worker must not issue another DELETE. A
later independent reappearance creates a drift action/report and is not
deleted without a new lifecycle event and authorization.

The expired-authorization branch, including a lease mutation-revoked before it
expired, loads the original event's durable target
binding, lifecycle version, terminal fingerprint, exact query booleans, and
SQLite `0002_tv_cleanup_state` child audit. It recollects current source,
identity, ownership-history, Plex, and target state, but never renews or reuses
the expired lease and never replaces the old target with a newly discovered
one. If the original target is absent, the backend may create only a fresh
`reconcile_only` authorization; the now-impossible pre-delete directory
inventory is not recomputed. If the original target is still present, a second
DELETE requires a distinct current-manifest `retry_actions` authorization,
every terminal gate, a complete fresh read-only inventory matching the original
fingerprint, and sufficient local/backend lease time. Any new live Sonarr
series ID is drift and requires revival handling, not deletion.

Assert no Plex library deletion method is called or available.

- [ ] **Step 4: Run executor tests and confirm RED**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sonarr_client.py tests\vod_filter\test_tv_cleanup_executor.py tests\vod_filter\test_tv_state_store.py -q
Pop-Location
```

Expected: failures for missing exact terminal delete and convergence branch.

- [ ] **Step 5: Implement one audited target child**

Use stable action ID `terminal_series:{eventId}:{sonarrSeriesId}`. Record
`deleteFiles=true`, `addImportListExclusion=false`, whether the call was issued,
and `completed`/`already_absent`/`failed`/`blocked`. Mark the backend result
converged only after fresh absence verification.

Reuse the checksum-protected `0002_tv_cleanup_state.sql` schema without editing
either migration. Persist the terminal child with `child_type=delete_series`,
the original fingerprint and target binding, stable reason, observed time, and
the migration's strict `delete_files=1` and
`add_import_list_exclusion=0` columns plus nullable checked
`series_absent`/`terminal_inventory_empty` postconditions; never persist a
raw series or media path. Store each retry under its distinct authorization ID
while retaining the original event/action audit chain.

- [ ] **Step 6: Verify and commit exact deletion**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sonarr_client.py tests\vod_filter\test_tv_cleanup_executor.py tests\vod_filter\test_tv_state_store.py -q
Pop-Location

git add workers/vod-filter/src workers/vod-filter/tests
git commit -m "feat(tv): remove authorized terminal Sonarr series"
```

### Task 7: Restore Sonarr Management On Revival

**Files:**
- Modify: `workers/vod-filter/src/services/tv_sync_planner.py`
- Modify: `workers/vod-filter/src/services/tv_destination_executor.py`
- Modify: `workers/vod-filter/src/services/tv_state_store.py`
- Modify: `workers/vod-filter/src/clients/sonarr_client.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_sync_planner.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_destination_executor.py`
- Test: `workers/vod-filter/tests/vod_filter/test_tv_state_store.py`

- [ ] **Step 1: Write failing revival planning tests**

For a retired show whose backend lifecycle changes:

- status reversal with no aired unwatched episode -> add/restore exact-TVDB
  Sonarr series, keep it monitored with new seasons monitored, leave Plex
  watchlist absent;
- new aired unwatched episode -> restore Sonarr, monitor the exact season/
  episode, search only the exact aired missing episode IDs, and add Plex
  watchlist;
- explicit current Trakt watchlist membership -> restore Sonarr and Plex;
- missing/conflicting TVDB identity -> report and mutate nothing.

- [ ] **Step 2: Write failing ownership-generation tests**

After terminal convergence, retain the old destination/action audit as
retired. A successful revival add creates a new owned Sonarr target ID bound to
the new lifecycle version; it does not overwrite the historical target row or
reuse the converged authorization.

- [ ] **Step 3: Run planner/executor tests and confirm RED**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_planner.py tests\vod_filter\test_tv_destination_executor.py tests\vod_filter\test_tv_state_store.py -q
Pop-Location
```

Expected: revival-specific assertions fail until retired ownership and desired
state are handled explicitly.

- [ ] **Step 4: Implement revival through the reversible Phase 3 path**

Reuse exact TVDB lookup/add/monitor and exact episode search. Keep
`addImportListExclusion=false` on terminal delete so revival is not blocked by
an exclusion created by this app. Plex desired state remains independent.

- [ ] **Step 5: Verify and commit revival**

```powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_planner.py tests\vod_filter\test_tv_destination_executor.py tests\vod_filter\test_tv_state_store.py -q
Pop-Location

git add workers/vod-filter/src workers/vod-filter/tests
git commit -m "feat(tv): restore Sonarr management after revival"
```

### Task 8: Complete End-To-End Terminal And Revival Simulations

**Files:**
- Create: `backend/tests/Watchlist.Application.Tests/TvLifecycleWorkflowSimulationTests.cs`
- Modify: `workers/vod-filter/tests/vod_filter/test_tv_workflow_simulation.py`
- Modify: `workers/vod-filter/tests/vod_filter/test_sync_tv_cli.py`
- Modify: `workers/vod-filter/tests/vod_filter/test_tv_sync_report.py`

- [ ] **Step 1: Add failing backend simulation cases**

Simulate two terminal hourly proofs, seven continuously observed days, one
stable authorization, worker convergence, `retired_terminal`, a status
reversal, and a later aired episode. Include >2h outage reset, explicit
watchlist blocker, unresolved outbox blocker, and one terminal snapshot.

- [ ] **Step 2: Add failing worker simulation cases**

Cover watched numbered files and specials, unwatched special, unknown mapping,
untracked media, writable/missing path root, recycle-bin absent, cap overflow,
zero files, exact successful delete, crash after delete, drift reappearance,
status-only revival, and new-episode revival. Verify movie behavior and Plex
library content are unchanged.

- [ ] **Step 3: Run simulations and confirm RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvLifecycleWorkflowSimulationTests"

Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_workflow_simulation.py tests\vod_filter\test_sync_tv_cli.py tests\vod_filter\test_tv_sync_report.py -q
Pop-Location
```

Expected: failures identify any terminal/revival behavior not yet integrated.

- [ ] **Step 4: Wire terminal execution behind its independent gate**

Preserve the Phase 4 claim/recollect/recheck/action/result sequence. Reports
include path counts/fingerprint, recycle-bin state, special counts, cap, exact
query booleans, convergence, revival, and stable blocker codes without paths or
secrets.

- [ ] **Step 5: Verify and commit complete simulations**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvLifecycleWorkflowSimulationTests"

Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_workflow_simulation.py tests\vod_filter\test_sync_tv_cli.py tests\vod_filter\test_tv_sync_report.py -q
Pop-Location

git add backend/tests workers/vod-filter
git commit -m "test(tv): simulate terminal cleanup and revival"
```

### Task 9: Wire The Read-Only Production Mount And Deployment Checks

**Files:**
- Modify: `deploy/production/compose.yaml`
- Modify: `deploy/production/worker.env.example`
- Modify: `deploy/local-cd/watchlist-deploy.env.example`
- Modify: `workers/vod-filter/docker-compose.yml`
- Modify: `scripts/deploy-movie-sync.sh`
- Modify: `tests/deployment/test_deploy_script.py`
- Create: `tests/deployment/test_tv_phase5_deployment.py`
- Modify: `.github/workflows/movie-ci.yml`

- [ ] **Step 1: Write failing deployment tests**

Require `WATCHLIST_TV_MEDIA_HOST_ROOT`, validate it exists and is a directory,
and render this worker mount:

```yaml
- ${WATCHLIST_TV_MEDIA_HOST_ROOT:?WATCHLIST_TV_MEDIA_HOST_ROOT is required}:/app/tv-ro:ro
```

Assert the backend remains read-only except its keyring, worker SQLite remains
persistent, the TV mount is read-only, rollback preserves both stores, and all
destructive switches/no-recycle-bin override remain false in checked-in
examples.

Parse every configured `TV_SYNC_ROOT_MAPPINGS_JSON` entry and assert its
`workerRoot` is `/app/tv-ro` or a descendant of the rendered `:ro` mount. Verify
read-only state from normalized Compose mount metadata and worker mount-info
fixtures; never use a write probe against the user's media root.

- [ ] **Step 2: Run deployment tests and confirm RED**

```powershell
python -m pytest tests\deployment\test_deploy_script.py -q
```

Expected: assertions fail until the compose/deployer include the safe TV root.

- [ ] **Step 3: Implement host validation and mount wiring**

Do not create or modify the media root. Abort cutover when it is missing,
ambiguous, overlaps application data, or Compose does not render it read-only.
Do not print its child paths or any secret values.

- [ ] **Step 4: Extend CI validation**

Create a temporary empty TV root and copy the committed non-secret example env
files into a real temporary config directory, then run Compose config,
deployment tests, worker tests/compile, backend tests, Android tests,
Docker builds, secret scan, and existing movie workflow tests. Retain the
workflow name used by exact-SHA deployment.

- [ ] **Step 5: Verify and commit deployment wiring**

```powershell
python -m pytest tests\deployment -q
$validationRoot = Join-Path $env:TEMP "watchlist-tv-phase5-compose"
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
$env:WATCHLIST_RELEASE = "tv-phase-5-validation"
docker compose -f deploy\production\compose.yaml config --quiet
Remove-Item Env:WATCHLIST_CONFIG_DIR, Env:WATCHLIST_DATA_DIR, Env:WATCHLIST_TV_MEDIA_HOST_ROOT, Env:WATCHLIST_RUNTIME_UID, Env:WATCHLIST_RUNTIME_GID, Env:WATCHLIST_RELEASE
Remove-Item -Recurse -Force $validationRoot
git add deploy workers/vod-filter/docker-compose.yml scripts/deploy-movie-sync.sh tests/deployment .github/workflows/movie-ci.yml
git commit -m "ops(tv): mount read-only roots for terminal verification"
```

### Task 10: Authorize And Supervise Terminal Cleanup

**Files:**
- Modify: `docs/decisions/tv_media_cleanup_authority.md`
- Modify: `docs/architecture/tv_sync_production.md`
- Modify: `docs/architecture/system_boundaries.md`
- Modify: `docs/apis/backend_api.md`
- Modify: `docs/apis/export_endpoints.md`
- Modify: `docs/integrations/trakt.md`
- Modify: `docs/integrations/plex.md`
- Modify: `docs/integrations/sonarr.md`
- Modify: `docs/data_models/tv_show.md`
- Modify: `docs/data_models/tv_lifecycle_event.md`
- Modify: `docs/systems/vod_filter_worker.md`
- Modify: `docs/systems/deployment_tooling.md`
- Modify: `docs/runbooks/tv_sync_operations.md`
- Modify: `docs/runbooks/homelab_cd.md`
- Modify: `docs/runbooks/validation.md`
- Modify: `docs/reports/tv_integration_rollout.md`
- Modify: `docs/backlog/roadmap.md`
- Modify: `docs/log.md`

- [ ] **Step 1: Add the explicit whole-series deletion decision**

Authorize only the exact Sonarr call with `deleteFiles=true` and
`addImportListExclusion=false` for a fully watched, exact-TVDB, owned/adopted,
ordinal-exact `ended` or `canceled` show after every applicable Phase 4 gate,
the approved terminal evidence, two-generation entry,
seven-day observed grace, current claim, filesystem, recycle-bin, cap, final
recheck, and audit gates. Preserve the Plex-library deletion ban and default-
false irreversible override.

- [ ] **Step 2: Run full validation with deletion still disabled**

```powershell
python tests\validate_okf.py
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
dotnet test backend\Watchlist.sln --configuration Release --no-build

Push-Location workers\vod-filter
python -m pytest -q
python -m compileall -q src continuous_sync.py sync_movies.py sync_tv.py reconcile_sync.py healthcheck.py
Pop-Location

Push-Location android
.\gradlew.bat :app:testDebugUnitTest :app:assembleDebug
Pop-Location

python -m pytest tests\deployment -q
git diff --check
```

Expected: every command passes and checked-in destructive switches remain
false.

- [ ] **Step 3: Observe terminal report-only behavior for seven additional days**

Record two-generation entry, every scheduled eligible interval/reset, Trakt
status fetch, outbox/quarantine state, exact identity/ownership, Plex watched
specials, next-airing agreement, Sonarr recycle bin, path inventory/fingerprint,
cap, and final blockers. Do not count the Phase 4 observation period as this
additional terminal review period.

- [ ] **Step 4: Execute at most one supervised terminal deletion**

Verify the real media root is mounted read-only and Sonarr recycle bin is
configured. Keep `TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false`; set terminal cap
to `1`; require `TV_SYNC_APPLY=true`; enable only
`TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=true` for one apply run; immediately
disable the terminal switch; and run convergence again.

Inspect Sonarr absence and recycle bin, Plex watchlist/library state, the path
fingerprint, SQLite action audit, Mongo lifecycle event/authorization, backend
worker result, and redacted reports before considering unattended use.

- [ ] **Step 5: Record normal-operation and revival evidence**

Keep continuing shows monitored, keep retired rows refreshable, retain hard
caps of two seasons and one terminal series per run, preserve Mongo/SQLite/
keyring state through rollback, and record a simulated or real status reversal
showing Sonarr restored while Plex follows its separate desired-state rule.

- [ ] **Step 6: Validate OKF and commit standing behavior**

```powershell
python tests\validate_okf.py
git diff --check
git add docs
git commit -m "docs(tv): authorize terminal cleanup and revival operations"
```
