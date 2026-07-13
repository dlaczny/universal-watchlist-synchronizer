---
type: Backlog
title: TV Phase 2 Plex History And Trakt Writes Implementation Plan
description: TDD execution plan for configured-account Plex episode history ingestion, durable Trakt history delivery, safety gates, health, and supervised rollout.
tags:
  - tv
  - plex
  - trakt
  - history
  - outbox
  - backend
timestamp: 2026-07-13T00:00:00Z
version: 0.1.0
---

# TV Phase 2 Plex History And Trakt Writes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ingest episode plays from one configured Plex account and TV library, reconcile the initial backfill, and deliver new plays to Trakt with a durable effectively-once outbox while keeping all cleanup mutation disabled.

**Architecture:** The .NET backend polls Plex into a MongoDB event ledger with a durable overlapping watermark, resolves only exact published TV identities, and performs a one-time watched-state bootstrap before creating Trakt work. A separately leased outbox serializes one non-idempotent Trakt history write at a time, quarantines ambiguous results for two read-side reconciliations, and feeds health evidence into the published TV snapshot so later cleanup phases fail closed.

**Tech Stack:** .NET 10 minimal API, C# 12 primary constructors, MongoDB 8 with MongoDB.Driver 3.9, ASP.NET hosted services and options, Plex XML HTTP API, Trakt REST API v2/OAuth, xUnit 2.9, FluentAssertions 8, Docker Compose, OKF Markdown.

---

## Phase Boundary And Prerequisites

Execute this plan only after Phase 1 has delivered these exact contracts:

- `ITraktAccessTokenProvider` returns a valid access token and can force one
  refresh after a definite authentication rejection.
- `ITraktOperationCoordinator` serializes source generations and Trakt
  writes for the single configured account.
- `ITraktTvClient` can return detailed watched progress for an exact Trakt
  show ID.
- `ITvShowReadRepository` reads one coherent published generation.
- `TvSyncService`, `TvLifecycleEvaluator`, and `TvExportService` publish
  source health, blockers, and the worker TV snapshot.
- `TvEndpointRouteBuilderExtensions` maps the protected Trakt status route.
- `MongoTvShowDocument` retains Trakt episode IDs with season and episode
  numbers.

If the Phase 1 implementation has not met one of those signatures, change the
Phase 1 implementation first. Do not add a second source coordinator, TV
pointer, token store, or TV read model in this phase.

Phase 2 never calls Sonarr, never mutates the Plex library or Plex watchlist,
and never enables a cleanup authorization. The only external write introduced
here is the separately gated Trakt history POST.

## File Structure

### Application contracts and services

- `backend/src/Watchlist.Application/IPlexHistoryClient.cs` owns Plex
  history/metadata reads.
- `backend/src/Watchlist.Application/PlexServerIdentityDto.cs`,
  `PlexExternalIdsDto.cs`, `PlexHistoryItemDto.cs`,
  `PlexHistoryPageDto.cs`, `PlexHistoryPageRequest.cs`, and
  `PlexEpisodeIdentityDto.cs` contain focused immutable Plex contracts.
- `backend/src/Watchlist.Application/PlexWatchEvent.cs` is the normalized
  exact-identity ledger model.
- `backend/src/Watchlist.Application/PlexHistoryCheckpoint.cs` is the
  configured server/account/library bootstrap and watermark state.
- `backend/src/Watchlist.Application/IPlexWatchEventRepository.cs` and
  `IPlexHistoryCheckpointRepository.cs` are Mongo-independent persistence
  boundaries.
- `backend/src/Watchlist.Application/ITvEpisodeIdentityResolver.cs` resolves
  a Plex show and S/E pair against the current published TV generation.
- `backend/src/Watchlist.Application/PlexWatchEventKeyFactory.cs` creates
  stable primary and fallback event keys.
- `backend/src/Watchlist.Application/PlexHistoryIngestionService.cs` owns
  full backfill, overlap polling, quarantine, and watermark advancement.
- `backend/src/Watchlist.Application/PlexHistoryBootstrapService.cs` owns the
  one-time Trakt watched-state comparison.
- `backend/src/Watchlist.Application/ITraktHistoryClient.cs` owns one
  non-retried history write and exact reconciliation reads.
- `backend/src/Watchlist.Application/TraktHistoryOutboxItem.cs` and
  `ITraktHistoryOutboxRepository.cs` define durable work and leasing.
- `backend/src/Watchlist.Application/TraktHistoryDeliveryService.cs` and
  `TraktHistoryReconciliationService.cs` implement delivery outcomes.
- `backend/src/Watchlist.Application/TvHistoryHealthService.cs` aggregates
  capability, bootstrap, ledger, outbox, and blocker state.

### Infrastructure

- `backend/src/Watchlist.Infrastructure/PlexHistoryClient.cs` parses Plex XML
  and paginates `/status/sessions/history/all`.
- `backend/src/Watchlist.Infrastructure/MongoPlexWatchEventDocument.cs`,
  `MongoPlexHistoryCheckpointDocument.cs`, and their repositories persist
  the ledger and cursor.
- `backend/src/Watchlist.Infrastructure/MongoTvEpisodeIdentityResolver.cs`
  resolves exact TVDB/TMDB/IMDb evidence through the published generation.
- `backend/src/Watchlist.Infrastructure/TraktHistoryClient.cs` deliberately
  bypasses the generic retry policy for POST.
- `backend/src/Watchlist.Infrastructure/MongoTraktHistoryOutboxDocument.cs`
  and `MongoTraktHistoryOutboxRepository.cs` persist one outbox item per
  Plex event.
- `backend/src/Watchlist.Infrastructure/TvHistoryHostedService.cs` invokes
  ingestion, bootstrap, delivery, and reconciliation on bounded schedules.
- `backend/src/Watchlist.Infrastructure/MongoTvHistoryIndexHostedService.cs`
  creates all unique and lease indexes before polling starts.

### API, configuration, deployment, and knowledge

- `backend/src/Watchlist.Api/Program.cs`,
  `TvEndpointRouteBuilderExtensions.cs`, and
  `appsettings.json` expose redacted status and safe defaults.
- `deploy/production/backend.env.example`,
  `deploy/backend/watchlist-backend.env.example`, and both Compose files
  expose configured-account settings with
  `TRAKT_HISTORY_SYNC_APPLY=false`.
- OKF integration, system, API, operations, validation, roadmap, and changelog
  concepts record the implemented boundary and supervised rollout.

### Task 1: Add Phase 2 Contracts And Safe Configuration

**Files:**
- Create: `backend/src/Watchlist.Application/PlexServerIdentityDto.cs`
- Create: `backend/src/Watchlist.Application/PlexExternalIdsDto.cs`
- Create: `backend/src/Watchlist.Application/PlexHistoryItemDto.cs`
- Create: `backend/src/Watchlist.Application/PlexHistoryPageDto.cs`
- Create: `backend/src/Watchlist.Application/PlexHistoryPageRequest.cs`
- Create: `backend/src/Watchlist.Application/PlexEpisodeIdentityDto.cs`
- Create: `backend/src/Watchlist.Application/PlexWatchEvent.cs`
- Create: `backend/src/Watchlist.Application/PlexHistoryCheckpoint.cs`
- Create: `backend/src/Watchlist.Application/TraktHistoryOutboxItem.cs`
- Create: `backend/src/Watchlist.Application/PlexWatchEventDisposition.cs`
- Create: `backend/src/Watchlist.Application/PlexWatchEventBootstrapOutcome.cs`
- Create: `backend/src/Watchlist.Application/PlexWatchDeliveryMode.cs`
- Create: `backend/src/Watchlist.Application/PlexWatchEventRoutingState.cs`
- Create: `backend/src/Watchlist.Application/TraktHistoryOutboxState.cs`
- Create: `backend/src/Watchlist.Application/ITraktHistoryApplyGate.cs`
- Create: `backend/src/Watchlist.Infrastructure/TraktHistoryOptions.cs`
- Create: `backend/src/Watchlist.Infrastructure/TraktHistoryApplyGate.cs`
- Modify: `backend/src/Watchlist.Infrastructure/PlexOptions.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoDbOptions.cs`
- Modify: `backend/src/Watchlist.Api/appsettings.json`
- Test: `backend/tests/Watchlist.Application.Tests/TvHistoryConfigurationTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoDbOptionsTests.cs`

- [ ] **Step 1: Write failing configuration and enum tests**

Create tests that require a single positive Plex account ID, a nonblank TV
library section ID, a page size from 1 through 500, a 24-hour overlap default,
and a five-minute poll default. Assert that every Mongo collection has the
stable name shown below and that history apply is false when the top-level
switch is absent.

~~~csharp
[Fact]
public void Defaults_KeepTraktWritesDisabledAndUseStableCollections()
{
    MongoDbOptions mongo = new();
    IConfiguration configuration = new ConfigurationBuilder().Build();

    new TraktHistoryApplyGate(configuration).Enabled.Should().BeFalse();
    mongo.PlexWatchEventsCollectionName.Should().Be("plex_watch_events");
    mongo.PlexHistoryCheckpointsCollectionName.Should().Be("plex_history_checkpoints");
    mongo.TraktHistoryOutboxCollectionName.Should().Be("trakt_history_outbox");
}

[Fact]
public void ApplyGate_ReadsOnlyExplicitTrueSwitch()
{
    IConfiguration configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TRAKT_HISTORY_SYNC_APPLY"] = "true"
        })
        .Build();

    new TraktHistoryApplyGate(configuration).Enabled.Should().BeTrue();
}
~~~

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvHistoryConfigurationTests|FullyQualifiedName~MongoDbOptionsTests"
~~~

Expected: compilation fails because the Phase 2 records, options, and apply gate
do not exist.

- [ ] **Step 3: Add the immutable application models**

Use these exact public contracts. Store all timestamps as UTC
`DateTimeOffset`; normalize event comparison timestamps to a whole UTC
second without changing the original `ViewedAt`.

~~~csharp
public enum PlexWatchEventDisposition
{
    Accepted,
    Quarantined
}

public enum PlexWatchEventBootstrapOutcome
{
    Reconciled,
    Superseded,
    SelectedForDelivery,
    NotApplicable
}

public enum PlexWatchDeliveryMode
{
    TraktEligible,
    LocalEvidenceOnlySpecial
}

public enum PlexWatchEventRoutingState
{
    Pending,
    Enqueued,
    NotApplicable
}

public enum TraktHistoryOutboxState
{
    Pending,
    Leased,
    Confirmed,
    Ambiguous,
    RetryWait,
    DeadLetter
}

public interface ITraktHistoryApplyGate
{
    bool Enabled { get; }
}

public sealed record PlexServerIdentityDto(string MachineIdentifier);

public sealed record PlexExternalIdsDto(
    int? TvdbId,
    int? TmdbId,
    string? ImdbId);

public sealed record PlexHistoryItemDto(
    string? HistoryKey,
    string EpisodeRatingKey,
    string ShowRatingKey,
    int SeasonNumber,
    int EpisodeNumber,
    DateTimeOffset ViewedAt);

public sealed record PlexHistoryPageDto(
    int Offset,
    int Size,
    int TotalSize,
    IReadOnlyList<PlexHistoryItemDto> Items);

public sealed record PlexEpisodeIdentityDto(
    long TraktShowId,
    long TraktEpisodeId,
    int TvdbShowId,
    int SeasonNumber,
    int EpisodeNumber);

public sealed record PlexWatchEvent(
    string EventId,
    string KeyKind,
    string MachineIdentifier,
    long AccountId,
    string LibrarySectionId,
    string? HistoryKey,
    string EpisodeRatingKey,
    string ShowRatingKey,
    int SeasonNumber,
    int EpisodeNumber,
    DateTimeOffset ViewedAt,
    DateTimeOffset ViewedAtSecond,
    PlexExternalIdsDto ExternalIds,
    PlexEpisodeIdentityDto? EpisodeIdentity,
    PlexWatchEventDisposition Disposition,
    PlexWatchDeliveryMode? DeliveryMode,
    PlexWatchEventBootstrapOutcome? BootstrapOutcome,
    PlexWatchEventRoutingState? PostCutoverRoutingState,
    string? QuarantineReason);

public sealed record PlexHistoryCheckpoint(
    string MachineIdentifier,
    long AccountId,
    string LibrarySectionId,
    string? LibrarySectionTitle,
    bool CapabilityAvailable,
    bool BootstrapComplete,
    DateTimeOffset? BootstrapCutoverAt,
    DateTimeOffset? Watermark,
    bool LastCollectionComplete,
    bool LastCollectionSucceeded,
    long ObservedEventCount,
    DateTimeOffset? LastCollectedAt,
    string? LastErrorCode);

public sealed record TraktHistoryOutboxItem(
    string Id,
    string PlexWatchEventId,
    PlexEpisodeIdentityDto EpisodeIdentity,
    DateTimeOffset WatchedAt,
    TraktHistoryOutboxState State,
    int AttemptCount,
    DateTimeOffset? NextAttemptAt,
    string? LeaseId,
    DateTimeOffset? LeaseExpiresAt,
    int ReconciliationCount,
    DateTimeOffset? LastReconciledAt,
    string? ReceiptId,
    string? FailureCode);
~~~

Disposition is durable evidence and is orthogonal to bootstrap processing.
Accepted events always retain `Disposition=Accepted`, a non-null exact episode
identity, and a non-null delivery mode. Quarantined events require null delivery
mode/outcome/routing state and a stable reason. An accepted event belongs to
exactly one processing path: pre-cutover rows have a non-null bootstrap outcome
and null post-cutover routing state; rows first inserted after a completed
cutover have a null bootstrap outcome and routing state `Pending`, then
`Enqueued` or `NotApplicable`. Bootstrap and routing CAS filters reject a row
already owned by the other path. Neither path may change disposition.

`ObservedEventCount` is the durable distinct event count for the exact
machine/account/library binding after a complete collection, not the number in
the latest overlap page. A complete successful empty history has count zero and
may have a null watermark; any complete successful nonempty history requires a
non-null watermark.

- [ ] **Step 4: Add options and collection names**

Extend `PlexOptions` and `MongoDbOptions` with these defaults. Use
`init` accessors, never expose the Plex token through a DTO, and validate
configured account/library values only when Phase 2 polling is enabled.

~~~csharp
public long AccountId { get; init; }
public string TvLibrarySectionId { get; init; } = string.Empty;
public string? TvLibrarySectionTitle { get; init; }
public int HistoryPageSize { get; init; } = 100;
public int HistoryPollIntervalMinutes { get; init; } = 5;
public int HistoryOverlapHours { get; init; } = 24;

public string PlexWatchEventsCollectionName { get; init; } = "plex_watch_events";
public string PlexHistoryCheckpointsCollectionName { get; init; } = "plex_history_checkpoints";
public string TraktHistoryOutboxCollectionName { get; init; } = "trakt_history_outbox";

public sealed class TraktHistoryOptions
{
    public const string SectionName = "TraktHistory";
    public bool Enabled { get; init; }
    public int OutboxPollSeconds { get; init; } = 30;
    public int LeaseMinutes { get; init; } = 5;
    public int AmbiguousQuarantineMinutes { get; init; } = 15;
    public int MaxAttempts { get; init; } = 8;
}
~~~

`TraktHistoryApplyGate.Enabled` must return true only when
`TRAKT_HISTORY_SYNC_APPLY` parses as true. The appsettings value remains
false and cannot override a missing top-level switch.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the command from Step 2.

Expected: all selected tests pass, including invalid account, blank library,
and out-of-range page-size cases.

- [ ] **Step 6: Commit the contracts**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/src/Watchlist.Api/appsettings.json backend/tests/Watchlist.Application.Tests
git commit -m "feat(tv): define Plex and Trakt history contracts"
~~~

### Task 2: Implement The Read-Only Plex History Client

**Files:**
- Create: `backend/src/Watchlist.Application/IPlexHistoryClient.cs`
- Create: `backend/src/Watchlist.Application/PlexHistoryParseException.cs`
- Create: `backend/src/Watchlist.Application/PlexHistoryCapabilityUnavailableException.cs`
- Create: `backend/src/Watchlist.Infrastructure/PlexHistoryClient.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/PlexHistoryClientTests.cs`

- [ ] **Step 1: Write failing client tests**

Use a recording `HttpMessageHandler` to prove:

- `/identity` returns a nonblank `machineIdentifier`;
- every history request sends the configured `accountID`,
  `librarySectionID`, ascending `viewedAt` sort, container offset, and
  container size;
- a page parses only `type="episode"` rows and preserves history key,
  episode/show rating keys, season, episode, and `viewedAt`;
- episode metadata and show metadata are fetched with exact rating keys;
- nested show GUIDs parse TVDB, TMDB, and IMDb IDs;
- malformed XML or missing required attributes throws
  `PlexHistoryParseException`;
- 401/403/404 on history maps to
  `PlexHistoryCapabilityUnavailableException`; and
- timeouts and 5xx map to `PlexUnavailableException`.

~~~csharp
[Fact]
public async Task GetHistoryPageAsync_SendsConfiguredScopeAndParsesEpisode()
{
    RecordingPlexHandler handler = RecordingPlexHandler.WithResponse(
        "/status/sessions/history/all",
        """
        <MediaContainer size="1" totalSize="1" offset="0">
          <Video type="episode" historyKey="/status/sessions/history/900"
                 ratingKey="501" grandparentRatingKey="100"
                 parentIndex="2" index="3" viewedAt="1783936800" />
        </MediaContainer>
        """);
    PlexHistoryClient client = CreateClient(handler);

    PlexHistoryPageDto page = await client.GetHistoryPageAsync(
        new PlexHistoryPageRequest(0, 100, null),
        CancellationToken.None);

    page.Items.Should().ContainSingle().Which.Should().Match<PlexHistoryItemDto>(
        item => item.HistoryKey == "/status/sessions/history/900"
            && item.SeasonNumber == 2
            && item.EpisodeNumber == 3);
    handler.LastQuery["accountID"].Should().Be("42");
    handler.LastQuery["librarySectionID"].Should().Be("7");
}
~~~

- [ ] **Step 2: Run the client tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter FullyQualifiedName~PlexHistoryClientTests
~~~

Expected: compilation fails because `IPlexHistoryClient`,
`PlexHistoryPageRequest`, and `PlexHistoryClient` do not exist.

- [ ] **Step 3: Add the client boundary**

~~~csharp
public sealed record PlexHistoryPageRequest(
    int Offset,
    int Size,
    DateTimeOffset? ViewedAtOrAfter);

public interface IPlexHistoryClient
{
    Task<PlexServerIdentityDto> GetServerIdentityAsync(
        CancellationToken cancellationToken);

    Task<PlexHistoryPageDto> GetHistoryPageAsync(
        PlexHistoryPageRequest request,
        CancellationToken cancellationToken);

    Task<PlexExternalIdsDto> GetShowExternalIdsAsync(
        string showRatingKey,
        CancellationToken cancellationToken);
}
~~~

- [ ] **Step 4: Implement exact XML requests and parsing**

`PlexHistoryClient` must:

1. Append `X-Plex-Token` without logging the request URI.
2. Request
   `/status/sessions/history/all?sort=viewedAt:asc&accountID=...&librarySectionID=...&X-Plex-Container-Start=...&X-Plex-Container-Size=...`.
3. Add a `viewedAt>=<unix-seconds>` filter only for overlap polls.
4. Reject any non-episode row that claims episode fields; ignore ordinary
   movie rows.
5. Read show GUIDs from
   `/library/metadata/{showRatingKey}?includeGuids=1`.
6. Parse GUID prefixes case-insensitively but never infer identity from title.

Use `HttpClient.SendAsync` once per GET through the existing retry policy;
Plex reads are idempotent. Dispose every response.

- [ ] **Step 5: Register and verify the client**

Register a typed `IPlexHistoryClient` using the same Plex base URL as
`IPlexLibraryClient`. Run the command from Step 2.

Expected: all Plex history client tests pass and existing
`PlexLibraryClientTests` still pass.

- [ ] **Step 6: Commit the Plex client**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests/PlexHistoryClientTests.cs
git commit -m "feat(tv): read configured Plex episode history"
~~~

### Task 3: Add Exact Identity Resolution And Stable Event Keys

**Files:**
- Create: `backend/src/Watchlist.Application/ITvEpisodeIdentityResolver.cs`
- Create: `backend/src/Watchlist.Application/PlexWatchEventKey.cs`
- Create: `backend/src/Watchlist.Application/PlexWatchEventKeyFactory.cs`
- Create: `backend/src/Watchlist.Application/TvEpisodeIdentityResolution.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvEpisodeIdentityResolver.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/PlexWatchEventKeyFactoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvEpisodeIdentityResolverTests.cs`

- [ ] **Step 1: Write failing key and identity tests**

Cover stable history-key identity, fallback identity, second normalization,
exact TVDB success, verified TMDB/IMDb fallback to the same TVDB show,
conflicting IDs, missing episode, duplicate published identity, and title-only
input. Title-only input must return an explicit quarantine result. Seed the
Phase 1 published generation with a separate S00E03 identity row and prove the
resolver returns its exact Trakt/TVDB episode IDs; prove a special absent from
that identity-only list returns `episode_missing` rather than falling back to
title or numbered progress.

~~~csharp
[Fact]
public void Create_WhenHistoryKeyMissing_UsesWholeSecondFallback()
{
    DateTimeOffset viewedAt = DateTimeOffset.Parse("2026-07-13T10:00:00.987Z");

    PlexWatchEventKey key = PlexWatchEventKeyFactory.Create(
        "machine-a",
        42,
        null,
        "episode-501",
        viewedAt);

    key.Kind.Should().Be("fallback");
    key.CanonicalValue.Should().Be(
        "machine-a|42|episode-501|2026-07-13T10:00:00Z");
}

[Fact]
public async Task ResolveAsync_WhenTvdbAndTmdbPointToDifferentShows_Quarantines()
{
    TvEpisodeIdentityResolution result = await resolver.ResolveAsync(
        new PlexExternalIdsDto(121361, 999999, null),
        2,
        3,
        CancellationToken.None);

    result.Identity.Should().BeNull();
    result.QuarantineReason.Should().Be("identity_conflict");
}
~~~

- [ ] **Step 2: Run focused tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~PlexWatchEventKeyFactoryTests|FullyQualifiedName~MongoTvEpisodeIdentityResolverTests"
~~~

Expected: compilation fails for missing key and resolver types.

- [ ] **Step 3: Add exact contracts**

~~~csharp
public sealed record PlexWatchEventKey(
    string EventId,
    string Kind,
    string CanonicalValue,
    DateTimeOffset ViewedAtSecond);

public sealed record TvEpisodeIdentityResolution(
    PlexEpisodeIdentityDto? Identity,
    string? QuarantineReason);

public interface ITvEpisodeIdentityResolver
{
    Task<TvEpisodeIdentityResolution> ResolveAsync(
        PlexExternalIdsDto externalIds,
        int seasonNumber,
        int episodeNumber,
        CancellationToken cancellationToken);
}
~~~

Compute `EventId` as lowercase hexadecimal SHA-256 of the canonical value.
The primary canonical value is
`machine|account|historyKey`. The fallback is
`machine|account|episodeRatingKey|wholeSecondUtc`.

- [ ] **Step 4: Implement published-generation resolution**

`MongoTvEpisodeIdentityResolver` must read the published pointer once, match
TVDB first, verify any supplied TMDB/IMDb IDs against that same row, and then
select one exact episode. Numbered episodes come only from `TvSeasonProgress`;
season 0 comes only from Phase 1's published `SpecialEpisodeIdentities` loaded
from `GET /shows/{id}/seasons/0?extended=full`. A missing TVDB may use TMDB or IMDb only
when it resolves to exactly one published row whose verified TVDB ID is
present. Return these stable quarantine reasons:

~~~text
identity_missing
identity_conflict
identity_ambiguous
episode_missing
episode_identity_missing
~~~

Resolve season 0 against the exact published special just like any other local
episode. A verified special is accepted with
`DeliveryMode=LocalEvidenceOnlySpecial`; it remains usable as configured-account
Plex watched evidence but is never enqueued to the Trakt outbox in Phase 2.
Missing/conflicting special identity is quarantined normally. Numbered episodes
use `DeliveryMode=TraktEligible`.

- [ ] **Step 5: Verify focused and prior Mongo tests**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~PlexWatchEventKeyFactoryTests|FullyQualifiedName~MongoTvEpisodeIdentityResolverTests|FullyQualifiedName~MongoTvShowReadRepositoryTests"
~~~

Expected: all selected tests pass against MongoDB 8 on
`localhost:27017`.

- [ ] **Step 6: Commit exact identity resolution**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests
git commit -m "feat(tv): resolve Plex plays by exact TV identity"
~~~

### Task 4: Persist The Plex Event Ledger And Durable Checkpoint

**Files:**
- Create: `backend/src/Watchlist.Application/IPlexWatchEventRepository.cs`
- Create: `backend/src/Watchlist.Application/IPlexHistoryCheckpointRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoPlexWatchEventDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoPlexHistoryCheckpointDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoPlexWatchEventRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoPlexHistoryCheckpointRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvHistoryIndexHostedService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoPlexWatchEventRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoPlexHistoryCheckpointRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvHistoryIndexHostedServiceTests.cs`

- [ ] **Step 1: Write failing ledger idempotency tests**

Use a unique Mongo database per test class. Prove:

- two upserts of the same history-key event leave one row;
- two fallback events in the same whole second leave one row;
- the same episode watched in a later second creates a second row;
- a stable history key on two different configured accounts creates two rows;
- quarantined events retain their reason and cannot be changed to accepted by
  a later overlapping poll with weaker identity; and
- bootstrap-outcome transitions are compare-and-set and idempotent while
  disposition never changes; and
- post-cutover routing transitions are compare-and-set
  `Pending -> Enqueued|NotApplicable`, cannot coexist with a bootstrap
  outcome, and never reset on an overlap upsert.

~~~csharp
[Fact]
public async Task UpsertAsync_WhenOverlapRepeatsEvent_PersistsOneRow()
{
    PlexWatchEvent item = CreateAcceptedEvent("event-a");

    await repository.UpsertAsync(item, CancellationToken.None);
    await repository.UpsertAsync(item, CancellationToken.None);

    IReadOnlyList<PlexWatchEvent> rows =
        await repository.GetBootstrapEventsAsync(CancellationToken.None);
    rows.Should().ContainSingle();
}
~~~

- [ ] **Step 2: Write failing checkpoint tests**

Require a singleton checkpoint key composed from machine identifier, account
ID, and library section ID. A failed collection may update capability/error
health but must preserve `Watermark`, `BootstrapCutoverAt`, and
`BootstrapComplete`. A successful collection may advance, but never move,
the watermark backward. Assert a complete successful zero-event backfill may
store a null watermark, while a complete successful nonempty collection is
rejected without a watermark. `ObservedEventCount` is the binding-wide durable
ledger count and is never replaced with only the overlap-page count.

~~~csharp
[Fact]
public async Task RecordFailureAsync_PreservesLastSuccessfulWatermark()
{
    await repository.RecordCollectionSuccessAsync(
        checkpoint with { Watermark = At("2026-07-13T10:00:00Z") },
        CancellationToken.None);

    await repository.RecordFailureAsync(
        checkpointKey,
        "plex_history_unavailable",
        At("2026-07-13T10:05:00Z"),
        CancellationToken.None);

    PlexHistoryCheckpoint stored =
        (await repository.GetAsync(checkpointKey, CancellationToken.None))!;
    stored.Watermark.Should().Be(At("2026-07-13T10:00:00Z"));
    stored.LastErrorCode.Should().Be("plex_history_unavailable");
}
~~~

- [ ] **Step 3: Run Mongo tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~MongoPlexWatchEventRepositoryTests|FullyQualifiedName~MongoPlexHistoryCheckpointRepositoryTests|FullyQualifiedName~MongoTvHistoryIndexHostedServiceTests"
~~~

Expected: compilation fails because the repositories and Mongo documents do
not exist.

- [ ] **Step 4: Add persistence interfaces**

~~~csharp
public interface IPlexWatchEventRepository
{
    Task UpsertAsync(
        PlexWatchEvent watchEvent,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlexWatchEvent>> GetBootstrapEventsAsync(
        CancellationToken cancellationToken);

    Task<bool> TrySetBootstrapOutcomeAsync(
        string eventId,
        PlexWatchEventBootstrapOutcome? expected,
        PlexWatchEventBootstrapOutcome next,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlexWatchEvent>> GetPostCutoverPendingAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<bool> TrySetPostCutoverRoutingStateAsync(
        string eventId,
        PlexWatchEventRoutingState expected,
        PlexWatchEventRoutingState next,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<long, int>> GetQuarantinedCountsByTraktShowAsync(
        CancellationToken cancellationToken);

    Task<long> CountByDispositionAsync(
        PlexWatchEventDisposition disposition,
        CancellationToken cancellationToken);
}

public interface IPlexHistoryCheckpointRepository
{
    Task<PlexHistoryCheckpoint?> GetAsync(
        string checkpointId,
        CancellationToken cancellationToken);

    Task RecordCollectionSuccessAsync(
        PlexHistoryCheckpoint checkpoint,
        CancellationToken cancellationToken);

    Task RecordFailureAsync(
        string checkpointId,
        string errorCode,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken);

    Task MarkBootstrapCompleteAsync(
        string checkpointId,
        DateTimeOffset cutoverAt,
        DateTimeOffset? watermark,
        CancellationToken cancellationToken);
}
~~~

- [ ] **Step 5: Implement immutable event upsert and monotonic checkpoint**

Use `SetOnInsert` for every identity, time, disposition, delivery-mode, and
initial post-cutover routing field in the event document. Bootstrap outcome and
post-cutover routing state have separate compare-and-set methods. Bootstrap
CAS additionally requires null routing state; routing CAS additionally requires
null bootstrap outcome, requires `Pending`, and permits only `Enqueued` or
`NotApplicable`.
A duplicate poll may update only `LastObservedAt`; it cannot
replace a quarantined identity or original `ViewedAt`.

Create these indexes:

~~~text
plex_watch_events:
  unique (machineIdentifier, accountId, historyKey)
    partial where historyKey is a nonempty string
  unique (machineIdentifier, accountId, episodeRatingKey, viewedAtSecond)
    partial where historyKey is null or empty
  (disposition, bootstrapOutcome, viewedAt)
  (disposition, postCutoverRoutingState, viewedAt)
  (episodeIdentity.traktShowId, disposition, deliveryMode)

plex_history_checkpoints:
  unique (machineIdentifier, accountId, librarySectionId)
~~~

The hosted index service must complete before history polling is registered.
If index creation fails, log only collection/index names and let startup health
fail; do not run without dedupe indexes.

- [ ] **Step 6: Run Mongo tests and verify GREEN**

Run the command from Step 3.

Expected: all selected tests pass with one-row overlap dedupe, two-row genuine
rewatch preservation, compare-and-set bootstrap outcomes, immutable
disposition, and monotonic watermark.

- [ ] **Step 7: Commit the ledger**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests
git commit -m "feat(tv): persist deduplicated Plex watch events"
~~~

### Task 5: Implement Complete Backfill And Overlapping Polls

**Files:**
- Create: `backend/src/Watchlist.Application/IPlexHistoryIngestionService.cs`
- Create: `backend/src/Watchlist.Application/PlexHistoryIngestionResultDto.cs`
- Create: `backend/src/Watchlist.Application/PlexHistoryIngestionService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/PlexHistoryIngestionServiceTests.cs`

- [ ] **Step 1: Write failing ingestion tests**

Use fakes for the Plex client, identity resolver, event repository, checkpoint,
and `TimeProvider`. Cover:

- initial backfill requests every page from offset zero;
- a later poll starts 24 hours before the durable watermark;
- the service advances pages using `offset + size`, not response item count;
- only the configured account/library binding is written to each event;
- exact identities become accepted;
- exact season-0 identities become accepted local evidence with no Trakt
  outbox eligibility;
- every accepted event first inserted after `BootstrapComplete=true` starts in
  post-cutover routing state `Pending`, including a late overlap arrival whose
  `ViewedAt` predates cutover and a distinct rewatch; pre-cutover rows keep a
  null routing state;
- replaying an overlap preserves an existing `Enqueued` routing state;
- missing/conflicting identities become quarantined with the resolver's stable
  reason;
- an exception on any page leaves the previous watermark unchanged;
- empty complete history marks collection successful without inventing a
  watermark;
- capability rejection records `CapabilityAvailable=false`; and
- the next successful poll restores capability without silently completing
  bootstrap.

~~~csharp
[Fact]
public async Task CollectAsync_WhenSecondPageFails_DoesNotAdvanceWatermark()
{
    checkpointRepository.Stored = ExistingCheckpoint(
        watermark: At("2026-07-12T10:00:00Z"));
    plexClient.Pages.Enqueue(Page(0, 100, 101, EventsAt("2026-07-13T09:00:00Z")));
    plexClient.FailNextPage = true;

    Func<Task> action = () => service.CollectAsync(CancellationToken.None);

    await action.Should().ThrowAsync<PlexUnavailableException>();
    checkpointRepository.SuccessfulWrites.Should().BeEmpty();
    checkpointRepository.Stored!.Watermark.Should().Be(
        At("2026-07-12T10:00:00Z"));
}
~~~

- [ ] **Step 2: Run the ingestion tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter FullyQualifiedName~PlexHistoryIngestionServiceTests
~~~

Expected: compilation fails because the ingestion service and result DTO do
not exist.

- [ ] **Step 3: Add the service contract**

~~~csharp
public interface IPlexHistoryIngestionService
{
    Task<PlexHistoryIngestionResultDto> CollectAsync(
        CancellationToken cancellationToken);
}

public sealed record PlexHistoryIngestionResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int PagesFetched,
    int EventsObserved,
    int EventsAccepted,
    int EventsQuarantined,
    DateTimeOffset? Watermark,
    bool BootstrapComplete,
    string? ErrorCode);
~~~

- [ ] **Step 4: Implement collection ordering**

The service performs this exact sequence:

1. Read and validate configured Plex account/library options.
2. Fetch the current Plex machine identifier.
3. Load only that binding's checkpoint.
4. Use no lower bound for incomplete bootstrap; otherwise subtract exactly
   `HistoryOverlapHours` from the watermark.
5. Fetch every page to `TotalSize`.
6. Fetch/collapse show external IDs once per show rating key during the run.
7. Create the stable key, resolve exact episode identity, and upsert each event.
   If the checkpoint snapshot was already bootstrap-complete, initialize each
   newly inserted accepted event with `PostCutoverRoutingState=Pending`
   regardless of `ViewedAt`; initialize pre-cutover or quarantined events with
   null routing state. `SetOnInsert` makes this classification overlap-safe.
8. After every event from every page is durable, record capability, collection
   time, the binding-wide distinct ledger count, complete/success flags, and
   the greatest observed `ViewedAt`. Null watermark is valid only for a
   complete successful binding-wide count of zero.

Do not catch caller cancellation. For dependency and parse failures, record a
redacted stable failure code, preserve the last successful watermark, and
rethrow the typed exception.

- [ ] **Step 5: Verify overlap, failure, and quarantine behavior**

Run the command from Step 2.

Expected: all ingestion tests pass; request assertions show no lower bound for
backfill and a 24-hour lower bound for post-cutover polling.

- [ ] **Step 6: Commit ingestion**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure/DependencyInjection.cs backend/tests/Watchlist.Application.Tests/PlexHistoryIngestionServiceTests.cs
git commit -m "feat(tv): backfill and poll Plex history safely"
~~~

### Task 6: Persist The Outbox, Reconcile Bootstrap, And Route New Events

**Files:**
- Create: `backend/src/Watchlist.Application/ITraktHistoryOutboxRepository.cs`
- Create: `backend/src/Watchlist.Application/ITraktWatchedEpisodeReader.cs`
- Create: `backend/src/Watchlist.Application/IPlexHistoryBootstrapService.cs`
- Create: `backend/src/Watchlist.Application/PlexHistoryBootstrapService.cs`
- Create: `backend/src/Watchlist.Application/PlexHistoryBootstrapResultDto.cs`
- Create: `backend/src/Watchlist.Application/IPlexPostCutoverRoutingService.cs`
- Create: `backend/src/Watchlist.Application/PlexPostCutoverRoutingService.cs`
- Create: `backend/src/Watchlist.Application/PlexPostCutoverRoutingResultDto.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTraktHistoryOutboxDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTraktHistoryOutboxRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/TraktWatchedEpisodeReader.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTvHistoryIndexHostedService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTraktHistoryOutboxRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/PlexHistoryBootstrapServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/PlexPostCutoverRoutingServiceTests.cs`

- [ ] **Step 1: Write failing outbox persistence tests**

Require one outbox row per Plex event, deterministic ID
`trakt-history:{plexEventId}`, atomic one-winner leasing, lease recovery, and
state compare-and-set. Prove a confirmed row cannot be recreated after a user
edits Trakt history.

~~~csharp
[Fact]
public async Task LeaseNextAsync_WithTwoCallers_GrantsOneLease()
{
    await repository.EnqueueAsync(CreateOutbox("event-a"), CancellationToken.None);

    TraktHistoryOutboxItem?[] results = await Task.WhenAll(
        repository.LeaseNextAsync(now, TimeSpan.FromMinutes(5), CancellationToken.None),
        repository.LeaseNextAsync(now, TimeSpan.FromMinutes(5), CancellationToken.None));

    results.Count(item => item is not null).Should().Be(1);
}
~~~

- [ ] **Step 2: Write failing bootstrap tests**

First set every accepted `LocalEvidenceOnlySpecial` event's bootstrap outcome
to `NotApplicable` without creating an outbox row. Group remaining accepted
`TraktEligible` events by exact Trakt episode ID. For an episode already watched
on Trakt, set every historical event outcome to `Reconciled`. For an unwatched
episode, enqueue only the newest event, set its outcome to
`SelectedForDelivery`, and set every older event outcome to `Superseded`.
Disposition remains `Accepted` throughout. Require the checkpoint to become
complete only after every accepted event has its appropriate outcome and every
eligible group succeeds.

~~~csharp
[Fact]
public async Task BootstrapAsync_WhenEpisodeUnwatched_EnqueuesOnlyLatestPlay()
{
    eventRepository.BootstrapEvents =
    [
        Event("old", episodeId: 7001, watchedAt: At("2025-01-01T10:00:00Z")),
        Event("new", episodeId: 7001, watchedAt: At("2026-07-13T10:00:00Z"))
    ];
    watchedReader.WatchedEpisodeIds = [];

    PlexHistoryBootstrapResultDto result =
        await service.BootstrapAsync(CancellationToken.None);

    outboxRepository.Enqueued.Should().ContainSingle(
        item => item.PlexWatchEventId == "new");
    eventRepository.OutcomeTransitions.Should().Contain(
        ("old", PlexWatchEventBootstrapOutcome.Superseded));
    eventRepository.Events.Single(item => item.EventId == "old")
        .Disposition.Should().Be(PlexWatchEventDisposition.Accepted);
    result.BootstrapComplete.Should().BeTrue();
}
~~~

Add a season-0 bootstrap test that records `NotApplicable`, retains accepted
disposition/identity, creates no outbox item, and still permits bootstrap
completion. Cleanup evidence queries in later phases select
`Disposition=Accepted` regardless of bootstrap outcome.

Write separate post-cutover router tests. After bootstrap is complete, every
accepted `TraktEligible` `Pending` event creates deterministic outbox ID
`trakt-history:{eventId}` and then CAS-transitions to `Enqueued`. Cover a late
overlap arrival older than `BootstrapCutoverAt`, a new first play, two genuine
rewatches, and a repeated overlap; each distinct event gets exactly one outbox
row and the repeat gets none. A `LocalEvidenceOnlySpecial` CAS-transitions to
`NotApplicable` with no outbox. Simulate a crash after outbox insert but before
the event CAS: rerun must accept the canonical existing outbox row and finish
the CAS without a second row. A conflicting existing outbox payload fails with
a stable blocker and leaves the event pending.

- [ ] **Step 3: Run focused tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~MongoTraktHistoryOutboxRepositoryTests|FullyQualifiedName~PlexHistoryBootstrapServiceTests|FullyQualifiedName~PlexPostCutoverRoutingServiceTests"
~~~

Expected: compilation fails for the missing outbox and bootstrap services.

- [ ] **Step 4: Add repository and bootstrap contracts**

~~~csharp
public interface ITraktHistoryOutboxRepository
{
    Task<bool> EnqueueAsync(
        TraktHistoryOutboxItem item,
        CancellationToken cancellationToken);

    Task<TraktHistoryOutboxItem?> LeaseNextAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> MarkConfirmedAsync(
        string id,
        string leaseId,
        string? receiptId,
        CancellationToken cancellationToken);

    Task MarkAmbiguousAsync(
        string id,
        string leaseId,
        DateTimeOffset reconcileAfter,
        string failureCode,
        CancellationToken cancellationToken);

    Task ScheduleRetryAsync(
        string id,
        string leaseId,
        DateTimeOffset nextAttemptAt,
        string failureCode,
        CancellationToken cancellationToken);

    Task MarkDeadLetterAsync(
        string id,
        string? leaseId,
        string failureCode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TraktHistoryOutboxItem>> GetAmbiguousDueAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<TraktHistoryOutboxState, long>> CountByStateAsync(
        CancellationToken cancellationToken);
}

public interface ITraktWatchedEpisodeReader
{
    Task<IReadOnlySet<long>> GetWatchedEpisodeIdsAsync(
        long traktShowId,
        CancellationToken cancellationToken);
}

public interface IPlexHistoryBootstrapService
{
    Task<PlexHistoryBootstrapResultDto> BootstrapAsync(
        CancellationToken cancellationToken);
}

public interface IPlexPostCutoverRoutingService
{
    Task<PlexPostCutoverRoutingResultDto> RoutePendingAsync(
        int limit,
        CancellationToken cancellationToken);
}
~~~

- [ ] **Step 5: Implement outbox indexes and bootstrap order**

Add indexes:

~~~text
trakt_history_outbox:
  unique (plexWatchEventId)
  (state, nextAttemptAt, createdAt)
  (state, leaseExpiresAt)
  (episodeIdentity.traktShowId, state)
~~~

`TraktWatchedEpisodeReader` calls the Phase 1 detailed-progress client and
returns exact watched Trakt episode IDs. Run the complete bootstrap read under
`ITraktOperationCoordinator` so no application history write changes
progress during reconciliation.

Bootstrap reads only accepted events with null post-cutover routing state, and
every bootstrap-outcome CAS also requires that state to remain null. It must
finish every such row before publishing `BootstrapComplete=true`.

For each unwatched `TraktEligible` episode, call `EnqueueAsync` before setting
the selected/superseded outcomes. Never enqueue `LocalEvidenceOnlySpecial`.
The unique Plex event index makes a crash/restart safe. Set
`BootstrapComplete`, `BootstrapCutoverAt`, and the final watermark last.
Never replay historical rewatches for an already watched episode.

`PlexPostCutoverRoutingService` is not bootstrap. It runs only against a
bootstrap-complete checkpoint and reads accepted rows whose durable routing
state is `Pending`, without comparing their `ViewedAt` to cutover. For a
`TraktEligible` row, construct the canonical deterministic outbox payload,
call idempotent `EnqueueAsync` first, then CAS `Pending -> Enqueued`. Exact
existing payload is success; a byte-different identity/timestamp for the same
ID is a conflict. This order recovers a crash between writes. For
`LocalEvidenceOnlySpecial`, CAS `Pending -> NotApplicable` without enqueueing.
No code may infer post-cutover work from a nullable bootstrap outcome alone.

- [ ] **Step 6: Verify Mongo and bootstrap tests**

Run the command from Step 3.

Expected: all selected tests pass, including process-restart replay of a
partially completed bootstrap and a one-winner outbox lease.

- [ ] **Step 7: Commit bootstrap and outbox persistence**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests
git commit -m "feat(tv): reconcile Plex backfill into Trakt outbox"
~~~

### Task 7: Implement Non-Retried Trakt History HTTP Operations

**Files:**
- Create: `backend/src/Watchlist.Application/ITraktHistoryClient.cs`
- Create: `backend/src/Watchlist.Application/TraktHistoryDtos.cs`
- Create: `backend/src/Watchlist.Application/TraktHistoryExceptions.cs`
- Create: `backend/src/Watchlist.Infrastructure/TraktHistoryClient.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TraktHistoryClientTests.cs`

- [ ] **Step 1: Write failing success and payload tests**

Record the complete request body and assert one Plex event produces exactly one
`POST /sync/history` with one episode, the exact Trakt episode ID, and the
original Plex `watched_at`. Require Trakt v2/API-key/Bearer headers and accept
success only when `added.episodes == 1` and `not_found.episodes` is empty.

~~~csharp
[Fact]
public async Task AddEpisodeAsync_SendsOneEpisodeAndOnePost()
{
    RecordingTraktHandler handler = RecordingTraktHandler.Success(
        """{"added":{"episodes":1},"not_found":{"episodes":[]}}""");
    TraktHistoryClient client = CreateClient(handler);

    TraktHistoryReceiptDto result = await client.AddEpisodeAsync(
        new TraktHistoryWriteDto(
            7001,
            DateTimeOffset.Parse("2026-07-13T10:00:00.987Z")),
        CancellationToken.None);

    handler.PostCount.Should().Be(1);
    handler.JsonBodies.Should().ContainSingle().Which.Should().Contain(
        "\"trakt\":7001");
    result.AddedEpisodes.Should().Be(1);
}
~~~

- [ ] **Step 2: Write failing outcome-classification tests**

Require:

- 401/403 -> `TraktHistoryAuthenticationException`;
- 429 -> `TraktHistoryRateLimitedException` carrying parsed
  `Retry-After`;
- timeout, connection loss, and every 5xx -> `TraktAmbiguousWriteException`;
- other 4xx or an invalid success receipt ->
  `TraktHistoryRejectedException`; and
- malformed response JSON -> `TraktHistoryParseException`.

For every exception case, assert `PostCount == 1`. The HTTP client itself
must never retry the non-idempotent operation.

Also test
`GET /sync/history/episodes/{traktEpisodeId}?start_at=...&end_at=...`
parsing history IDs and `watched_at` values. This GET may use the existing
idempotent retry policy.

- [ ] **Step 3: Run client tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter FullyQualifiedName~TraktHistoryClientTests
~~~

Expected: compilation fails for the missing Trakt history client and DTOs.

- [ ] **Step 4: Add exact history contracts**

~~~csharp
public sealed record TraktHistoryWriteDto(
    long TraktEpisodeId,
    DateTimeOffset WatchedAt);

public sealed record TraktHistoryReceiptDto(
    int AddedEpisodes,
    string? ReceiptId);

public sealed record TraktHistoryMatchDto(
    long HistoryId,
    long TraktEpisodeId,
    DateTimeOffset WatchedAt);

public interface ITraktHistoryClient
{
    Task<TraktHistoryReceiptDto> AddEpisodeAsync(
        TraktHistoryWriteDto request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TraktHistoryMatchDto>> GetEpisodeHistoryAsync(
        long traktEpisodeId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken);
}
~~~

- [ ] **Step 5: Implement a single-send POST**

Build a fresh `HttpRequestMessage` with this body:

~~~json
{
  "episodes": [
    {
      "watched_at": "2026-07-13T10:00:00.9870000+00:00",
      "ids": {
        "trakt": 7001
      }
    }
  ]
}
~~~

Obtain a valid token before constructing the request. Call
`HttpClient.SendAsync` directly once; do not call `HttpRetryPolicy`.
Never include response bodies, tokens, authorization headers, or the complete
request URI in exceptions or logs.

Treat any 5xx as ambiguous even when a response arrived, because the remote
commit outcome is not proven. Parse `Retry-After` as delta or absolute UTC
time. The reconciliation GET requests a one-second window on either side of
the normalized submitted second.

- [ ] **Step 6: Verify one-send behavior**

Run the command from Step 3.

Expected: every test passes; the timeout, network, and 5xx tests each observe
exactly one POST.

- [ ] **Step 7: Commit the Trakt history client**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests/TraktHistoryClientTests.cs
git commit -m "feat(tv): add non-retried Trakt history writes"
~~~

### Task 8: Deliver And Reconcile Outbox Work Effectively Once

**Files:**
- Create: `backend/src/Watchlist.Application/ITraktHistoryDeliveryService.cs`
- Create: `backend/src/Watchlist.Application/TraktHistoryDeliveryResultDto.cs`
- Create: `backend/src/Watchlist.Application/TraktHistoryDeliveryService.cs`
- Create: `backend/src/Watchlist.Application/TraktHistoryReconciliationService.cs`
- Create: `backend/src/Watchlist.Application/ITvSyncRequestQueue.cs`
- Modify: `backend/src/Watchlist.Application/ITraktHistoryOutboxRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTraktHistoryOutboxRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TraktHistoryDeliveryServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TraktHistoryReconciliationServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTraktHistoryOutboxRepositoryTests.cs`

- [ ] **Step 1: Write failing delivery state-machine tests**

Cover these exact transitions:

| Input | Required result |
|---|---|
| Apply switch false | no lease, no HTTP request, `disabled` result |
| Definite success | `confirmed`, receipt stored, TV refresh queued |
| First 401 | force token refresh, one safe second POST |
| Second 401 | `dead_letter/authentication_revoked` |
| 429 | `retry_wait` at Retry-After |
| Timeout/network/5xx | `ambiguous` for at least 15 minutes |
| Invalid identity/rejected 4xx | `dead_letter` |
| Lost lease during completion | no state overwrite; operator-visible conflict |

~~~csharp
[Fact]
public async Task DeliverOneAsync_WhenWriteAmbiguous_DoesNotRetry()
{
    outbox.Leased = PendingItem("outbox-a");
    traktClient.Exception = new TraktAmbiguousWriteException("ambiguous");

    TraktHistoryDeliveryResultDto result =
        await service.DeliverOneAsync(CancellationToken.None);

    traktClient.AddCalls.Should().Be(1);
    outbox.AmbiguousWrites.Should().ContainSingle();
    outbox.RetryWrites.Should().BeEmpty();
    result.Status.Should().Be("ambiguous");
}
~~~

- [ ] **Step 2: Write failing two-poll reconciliation tests**

For an ambiguous event after quarantine:

- one exact whole-second match -> confirm;
- more than one exact match -> dead letter
  `duplicate_remote_matches`;
- first completed poll with zero matches -> remain ambiguous and schedule a
  second poll;
- second completed poll with zero matches -> `retry_wait` using bounded
  exponential backoff; and
- a dependency failure does not count as a completed poll.

~~~csharp
[Fact]
public async Task ReconcileAsync_AfterTwoCleanNoMatchPolls_AllowsRetry()
{
    TraktHistoryOutboxItem item = AmbiguousItem(reconciliationCount: 1);
    historyClient.Matches = [];

    await service.ReconcileAsync(item, CancellationToken.None);

    outbox.RetryWrites.Should().ContainSingle(
        write => write.FailureCode == "ambiguous_no_match_twice");
}
~~~

- [ ] **Step 3: Run service tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TraktHistoryDeliveryServiceTests|FullyQualifiedName~TraktHistoryReconciliationServiceTests|FullyQualifiedName~MongoTraktHistoryOutboxRepositoryTests"
~~~

Expected: compilation fails for missing services and reconciliation repository
methods.

- [ ] **Step 4: Extend the repository contract**

~~~csharp
Task<bool> RecordReconciliationAsync(
    string id,
    int expectedCount,
    DateTimeOffset reconciledAt,
    DateTimeOffset nextReconcileAt,
    CancellationToken cancellationToken);

Task<bool> ConfirmAmbiguousAsync(
    string id,
    string receiptId,
    CancellationToken cancellationToken);

Task<bool> ReleaseAmbiguousForRetryAsync(
    string id,
    DateTimeOffset nextAttemptAt,
    string failureCode,
    CancellationToken cancellationToken);
~~~

Each method filters on the expected current state and count. Never update by
ID alone.

- [ ] **Step 5: Implement delivery under the account coordinator**

`DeliverOneAsync` checks `TraktHistoryApplyGate.Enabled` before leasing.
When enabled, lease one due row and run only the token/write/classification
block under `ITraktOperationCoordinator`. A 401/403 response proves no write;
force one refresh and permit exactly one second POST. All ambiguous outcomes
leave the coordinator without an immediate retry.

After a confirmed write, release the coordinator and enqueue a complete
progress refresh using:

~~~csharp
public interface ITvSyncRequestQueue
{
    Task EnqueueAsync(
        string reason,
        long traktShowId,
        CancellationToken cancellationToken);
}
~~~

Use reason `trakt_history_confirmed`. Do not mark a show caught up from the
outbox receipt; only the later complete TV generation may change lifecycle.

- [ ] **Step 6: Implement two completed reconciliation reads**

Compare `TraktEpisodeId` and `WatchedAt` normalized to a whole UTC second.
Use separate repository timestamps for the two completed polls and enforce a
minimum 15-minute interval. The retry delay is:

~~~csharp
TimeSpan delay = TimeSpan.FromSeconds(
    Math.Min(Math.Pow(2, Math.Max(0, attemptCount - 1)) * 30, 21600));
~~~

Cap at six hours. When `AttemptCount >= MaxAttempts`, use
`dead_letter/attempts_exhausted` instead of retry.

- [ ] **Step 7: Verify state transitions and concurrency**

Run the command from Step 3.

Expected: all selected tests pass, including one-winner state changes and
exactly one initial POST for every ambiguous outcome.

- [ ] **Step 8: Commit delivery and reconciliation**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests
git commit -m "feat(tv): deliver Trakt history effectively once"
~~~

### Task 9: Feed History Health Into TV Mutation Gates And Export

**Files:**
- Create: `backend/src/Watchlist.Application/ITvHistoryHealthService.cs`
- Create: `backend/src/Watchlist.Application/TvHistoryHealthDto.cs`
- Create: `backend/src/Watchlist.Application/TvHistoryHealthService.cs`
- Modify: `backend/src/Watchlist.Application/TvSyncService.cs`
- Modify: `backend/src/Watchlist.Application/TvLifecycleEvaluator.cs`
- Modify: `backend/src/Watchlist.Application/TvExportService.cs`
- Modify: `backend/src/Watchlist.Application/WorkerTvPlexHistoryDto.cs`
- Modify: `backend/src/Watchlist.Application/WorkerTvCleanupAuthorizationDto.cs`
- Modify: `backend/src/Watchlist.Application/TraktConnectionStatusDto.cs`
- Modify: `backend/src/Watchlist.Api/TvEndpointRouteBuilderExtensions.cs`
- Modify: `contracts/tv/worker-sync-state-v1.json`
- Test: `backend/tests/Watchlist.Application.Tests/TvHistoryHealthServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvSyncServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvLifecycleEvaluatorTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvExportServiceTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/TraktIntegrationApiTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/TvWorkerContractTests.cs`

- [ ] **Step 1: Write failing health aggregation tests**

Require configured binding, capability, bootstrap state, last collection,
watermark, event counts, outbox counts by every state, oldest unresolved age,
post-cutover routing counts, and apply-gate state. Pending, leased, ambiguous,
retry-wait, and dead-letter outbox rows are unresolved. A pending post-cutover
route is separately unresolved even before its deterministic outbox row exists.
Quarantined Plex events are separate blockers.

~~~csharp
[Fact]
public async Task GetAsync_WhenBootstrapIncomplete_IsNotMutationCapable()
{
    checkpointRepository.Stored = Checkpoint(
        capabilityAvailable: true,
        bootstrapComplete: false);

    TvHistoryHealthDto result =
        await service.GetAsync(CancellationToken.None);

    result.MutationCapable.Should().BeFalse();
    result.HealthReasons.Should().Contain("plex_backfill_incomplete");
}
~~~

- [ ] **Step 2: Write failing generation, lifecycle, and export tests**

Prove:

- unavailable history or incomplete bootstrap allows a browse generation but
  forces `MutationCapable=false`;
- an affected show with unresolved outbox gets
  `trakt_outbox_unresolved`;
- an affected show with a pending post-cutover route gets
  `plex_post_cutover_routing_unresolved`;
- a quarantined event gets `plex_event_quarantined`;
- neither condition advances a season or terminal candidate;
- cleanup authorization is absent in Phase 2;
- worker export binds machine identifier, account, library section,
  `CollectedAt`, nullable watermark, complete/success flags, and binding-wide
  observed-event count; and
- a future cleanup authorization copies `PlexEvidenceCollectedAt` and
  the exact nullable-watermark collection tuple.

Evolve the sole worker fixture and backend serialization test in this same
slice. Its `plexHistory` object carries `lastCollectionComplete`,
`lastCollectionSucceeded`, binding-wide `observedEventCount`, and
`pendingPostCutoverRoutes`. A capable mutation-capable complete-empty example
uses a null watermark only with both flags true and count zero; a positive
count requires a timestamp watermark. A positive pending-route count requires
`mutationCapable=false` and the exact routing blocker.

- [ ] **Step 3: Run focused tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvHistoryHealthServiceTests|FullyQualifiedName~TvSyncServiceTests|FullyQualifiedName~TvLifecycleEvaluatorTests|FullyQualifiedName~TvExportServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TraktIntegrationApiTests|FullyQualifiedName~TvWorkerContractTests"
~~~

Expected: assertions fail because Phase 1 health does not include the Phase 2
ledger/outbox facts.

- [ ] **Step 4: Add the exact health DTO**

~~~csharp
public sealed record TvHistoryHealthDto(
    bool MutationCapable,
    bool PlexHistoryCapable,
    bool BootstrapComplete,
    string MachineIdentifier,
    long AccountId,
    string LibrarySectionId,
    string? LibrarySectionTitle,
    DateTimeOffset? CollectedAt,
    DateTimeOffset? Watermark,
    bool LastCollectionComplete,
    bool LastCollectionSucceeded,
    long ObservedEventCount,
    long AcceptedEvents,
    long QuarantinedEvents,
    long PendingPostCutoverRoutes,
    IReadOnlyDictionary<string, long> OutboxCounts,
    DateTimeOffset? OldestUnresolvedAt,
    bool HistorySyncApply,
    IReadOnlyList<string> HealthReasons,
    IReadOnlyDictionary<long, IReadOnlyList<string>> ShowBlockers);
~~~

Map outbox state keys as snake case:
`pending`, `leased`, `confirmed`, `ambiguous`,
`retry_wait`, and `dead_letter`.

- [ ] **Step 5: Integrate fail-closed health**

The TV generation reads one completed health snapshot and embeds its binding,
collection time, and watermark in the manifest. These stable reasons affect
mutation:

~~~text
plex_history_unavailable
plex_backfill_incomplete
plex_evidence_stale
plex_event_quarantined
plex_post_cutover_routing_unresolved
trakt_outbox_unresolved
trakt_history_apply_disabled
~~~

`trakt_history_apply_disabled` is operator health, but by itself does not
invalidate a clean confirmed history state. Incomplete bootstrap, unavailable
capability, collection older than 30 minutes, quarantined events, and
unresolved outbox rows do block candidate advancement and cleanup.

Extend the protected Trakt status response with a `history` property. It may
contain non-secret machine/account/library binding values, counts, state, and
timestamps. It must not contain the Plex token, Trakt tokens, raw OAuth codes,
request bodies, or exception response bodies.

- [ ] **Step 6: Verify health, export, and status contracts**

Run the commands from Step 3.

Expected: all selected tests pass; API JSON contains redacted `history`
health and no credential-shaped keys.

- [ ] **Step 7: Commit health gates**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Api backend/tests contracts/tv/worker-sync-state-v1.json
git commit -m "feat(tv): gate lifecycle on clean history state"
~~~

### Task 10: Orchestrate Polling, Bootstrap, Delivery, And Reconciliation

**Files:**
- Create: `backend/src/Watchlist.Application/ITvHistoryOrchestrator.cs`
- Create: `backend/src/Watchlist.Application/TvHistoryOrchestrator.cs`
- Create: `backend/src/Watchlist.Application/TvHistoryRunResultDto.cs`
- Create: `backend/src/Watchlist.Infrastructure/TvHistoryHostedService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/TraktHistoryOptions.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvHistoryOrchestratorTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvHistoryHostedServiceTests.cs`

- [ ] **Step 1: Write failing orchestration-order tests**

Require each run to:

1. collect Plex when the five-minute poll is due;
2. run bootstrap only after a successful complete collection;
3. after bootstrap is complete, route every pending post-cutover event before
   delivery, even while apply is false;
4. deliver no outbox writes until bootstrap is complete and routing finishes;
5. continue collection/bootstrap/routing/reporting while apply is false;
6. process no more than five writes serially per run;
7. reconcile due ambiguous rows even when new writes are disabled;
8. request health refresh after each state-changing stage; and
9. isolate one row failure so later reconciliation health remains visible.

~~~csharp
[Fact]
public async Task RunOnceAsync_WhenBootstrapIncomplete_CollectsAndBootstrapsBeforeDelivery()
{
    checkpoint.BootstrapComplete = false;

    TvHistoryRunResultDto result =
        await orchestrator.RunOnceAsync(CancellationToken.None);

    calls.Should().Equal("collect", "bootstrap", "health");
    delivery.Calls.Should().Be(0);
    result.Status.Should().Be("completed");
}
~~~

- [ ] **Step 2: Write failing hosted-service cancellation tests**

Use a fake `TimeProvider` and orchestrator. Assert immediate first run,
30-second bounded loop interval, clean cancellation, and typed exception
logging by stable code. Capture logs and assert they do not contain configured
Plex or Trakt tokens.

- [ ] **Step 3: Run focused tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvHistoryOrchestratorTests|FullyQualifiedName~TvHistoryHostedServiceTests"
~~~

Expected: compilation fails because the orchestrator and hosted service do not
exist.

- [ ] **Step 4: Add bounded orchestration contracts**

~~~csharp
public interface ITvHistoryOrchestrator
{
    Task<TvHistoryRunResultDto> RunOnceAsync(
        CancellationToken cancellationToken);
}

public sealed record TvHistoryRunResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool CollectionRan,
    bool BootstrapRan,
    int PostCutoverRoutesProcessed,
    int WritesAttempted,
    int ReconciliationsAttempted,
    IReadOnlyList<string> HealthReasons);
~~~

Extend `TraktHistoryOptions`:

~~~csharp
public int DeliveryBatchSize { get; init; } = 5;
public int ReconciliationBatchSize { get; init; } = 20;
public int HostedLoopSeconds { get; init; } = 30;
~~~

Validate each value from 1 through 100.

- [ ] **Step 5: Implement one deterministic run**

The orchestrator holds no long-running global lock. Plex reads run outside the
Trakt coordinator. Bootstrap obtains the coordinator for its complete Trakt
watched-state read. Each delivery obtains and releases the coordinator around
one write. Reconciliation GETs also use the coordinator so they cannot race a
source generation or the application's write.

Post-cutover routing is a durable local Mongo stage and runs after collection
and any required bootstrap, before delivery. It does not acquire the Trakt
coordinator or require the apply gate. A routing conflict blocks delivery for
that row, remains visible in health, and does not prevent reconciliation of
other already-created rows.

When apply is false, leave pending rows pending rather than leasing and
releasing them repeatedly. Ambiguous reconciliation remains active because it
is read-only and reduces uncertainty.

- [ ] **Step 6: Implement the cancellation-safe hosted wrapper**

Use `PeriodicTimer` with the configured 30-second loop. Catch only known
dependency/history exceptions at the run boundary, log a stable code plus
counts, and continue. Let caller cancellation end the service. Register the
index hosted service before `TvHistoryHostedService`.

- [ ] **Step 7: Verify orchestration and no-secret logging**

Run the command from Step 3.

Expected: all selected tests pass; the log capture contains stable reason
codes and no configured secret values.

- [ ] **Step 8: Commit orchestration**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests
git commit -m "feat(tv): run bounded TV history synchronization"
~~~

### Task 11: Add API And End-To-End Pipeline Verification

**Files:**
- Modify: `backend/src/Watchlist.Api/TvEndpointRouteBuilderExtensions.cs`
- Create: `backend/tests/Watchlist.Api.Tests/SeededTvApiFactory.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/TraktIntegrationApiTests.cs`
- Create: `backend/tests/Watchlist.Application.Tests/TvHistoryPipelineSimulationTests.cs`

- [ ] **Step 1: Write failing protected-status API tests**

`GET /api/integrations/trakt/status` already uses the sync-key boundary.
Extend its seeded response and assert:

- missing/wrong key returns 401 when a key is configured;
- the correct key returns connection plus nested history health;
- counts serialize with the exact snake-case outbox keys;
- machine/account/library binding is present;
- no property name contains `token`, `secret`, `deviceCode`, or
  `authorization`; and
- a Mongo outage returns 503 without a raw exception body.

~~~csharp
[Fact]
public async Task GetTraktStatus_ReturnsRedactedHistoryHealth()
{
    using SeededTvApiFactory factory = new(syncApiKey: "test-key");
    HttpClient client = factory.CreateClient();
    client.DefaultRequestHeaders.Add("X-Watchlist-Sync-Key", "test-key");

    JsonDocument body = await GetJsonAsync(
        client,
        "/api/integrations/trakt/status");

    JsonElement history = body.RootElement.GetProperty("history");
    history.GetProperty("bootstrapComplete").GetBoolean().Should().BeTrue();
    body.RootElement.ToString().Should().NotContain("accessToken");
}
~~~

- [ ] **Step 2: Write the failing pipeline simulation**

Construct the real key factory, ingestion service, bootstrap service, delivery
service, and reconciliation service around deterministic in-memory
repositories and recording HTTP clients. Simulate:

1. one accessible Plex play absent from Trakt;
2. bootstrap creating one outbox row;
3. apply false making zero POSTs;
4. apply true making one accepted POST;
5. a repeated 24-hour-overlap poll creating no second outbox row;
6. a late overlap event first observed after cutover, despite an older
   `ViewedAt`, being routed once by the post-cutover stage;
7. a later rewatch creating one new event, one routed outbox row, and one
   intentional POST; and
8. an ambiguous third play reconciling to one remote match without retry.

~~~csharp
[Fact]
public async Task Pipeline_DeduplicatesOverlapPreservesRewatchAndReconcilesAmbiguity()
{
    await fixture.RunBackfillAndBootstrapAsync();
    fixture.ApplyGate.Enabled = true;
    await fixture.DeliverAllAsync();
    await fixture.RunOverlapAsync();
    await fixture.RunRewatchAsync();
    await fixture.RunAmbiguousThenRemoteMatchAsync();

    fixture.Trakt.PostedEpisodeIds.Should().Equal(7001, 7001, 7001);
    fixture.Trakt.BlindRetryCount.Should().Be(0);
    fixture.Outbox.Count(TraktHistoryOutboxState.Confirmed).Should().Be(3);
}
~~~

- [ ] **Step 3: Run API and simulation tests and verify RED**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter FullyQualifiedName~TvHistoryPipelineSimulationTests
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter FullyQualifiedName~TraktIntegrationApiTests
~~~

Expected: the simulation or status assertions fail until all Phase 2 services
are wired into the status projection.

- [ ] **Step 4: Wire status and deterministic fixtures**

Inject `ITvHistoryHealthService` into the Phase 1 Trakt status service and
return it as `history`. Extend `SeededTvApiFactory` with one fake health
service and remove `TvHistoryHostedService` from the test host so HTTP tests
never contact a real Plex or Trakt server.

The pipeline fixture may use in-memory persistence because Mongo uniqueness
and lease concurrency are already tested in focused repository tests. It must
use the production state-machine services and exact JSON client handlers.

- [ ] **Step 5: Verify API and pipeline GREEN**

Run the commands from Step 3.

Expected: all selected tests pass, exactly three intentional writes are
recorded, overlap adds none, and ambiguous delivery adds no blind retry.

- [ ] **Step 6: Run the complete backend regression suite**

With MongoDB 8 running on `localhost:27017`:

~~~powershell
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
dotnet test backend\Watchlist.sln --configuration Release --no-build
~~~

Expected: every Application and API test passes; existing movie sync,
Letterboxd lifecycle, TMDB enrichment, and Plex movie inventory tests remain
green.

- [ ] **Step 7: Commit API and simulation coverage**

~~~powershell
git add backend/src/Watchlist.Api backend/tests
git commit -m "test(tv): verify Plex to Trakt history pipeline"
~~~

### Task 12: Add Production-Safe Configuration And Container Validation

**Files:**
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/appsettings.json`
- Modify: `backend/src/Watchlist.Api/appsettings.Development.Local.example.json`
- Modify: `deploy/production/backend.env.example`
- Modify: `deploy/backend/watchlist-backend.env.example`
- Modify: `deploy/production/compose.yaml`
- Modify: `deploy/backend/compose.yaml`
- Create: `tests/deployment/test_tv_history_config.py`

- [ ] **Step 1: Write failing deployment-contract tests**

Parse both env examples and Compose files. Require:

- `Plex__AccountId` and `Plex__TvLibrarySectionId`;
- explicit `TraktHistory__Enabled=false` in generic examples;
- exact `TRAKT_HISTORY_SYNC_APPLY=false`;
- page/poll/overlap values;
- no real token, account ID, or server URL;
- no Plex/Trakt credential passed to the worker;
- all Phase 1 worker TV apply/adoption/deletion switches remain false; and
- the backend's existing Data Protection keyring mount remains writable while
  its root filesystem stays read-only.

~~~python
def test_production_history_writes_default_disabled() -> None:
    env = (ROOT / "deploy/production/backend.env.example").read_text(
        encoding="utf-8"
    )
    assert "TRAKT_HISTORY_SYNC_APPLY=false" in env
    assert "TraktHistory__Enabled=false" in env
    assert "Plex__AccountId=replace-on-host" in env
    assert "Plex__TvLibrarySectionId=replace-on-host" in env
~~~

- [ ] **Step 2: Run the deployment test and verify RED**

Run:

~~~powershell
python -m pytest tests\deployment\test_tv_history_config.py -q
~~~

Expected: test fails because the Phase 2 settings are absent.

- [ ] **Step 3: Bind and validate production options**

In `AddWatchlistInfrastructure`, bind `TraktHistoryOptions` and validate on
start:

~~~csharp
services.AddOptions<TraktHistoryOptions>()
    .Bind(configuration.GetSection(TraktHistoryOptions.SectionName))
    .Validate(options => options.OutboxPollSeconds is >= 1 and <= 300,
        "TraktHistory:OutboxPollSeconds must be between 1 and 300.")
    .Validate(options => options.LeaseMinutes is >= 1 and <= 30,
        "TraktHistory:LeaseMinutes must be between 1 and 30.")
    .Validate(options => options.AmbiguousQuarantineMinutes >= 15,
        "TraktHistory:AmbiguousQuarantineMinutes must be at least 15.")
    .ValidateOnStart();
~~~

When `TraktHistory:Enabled=true`, validate positive Plex account ID,
nonblank TV library section, configured Plex token/base URL, and available
Phase 1 Trakt connection configuration. Startup errors name only missing
setting keys.

- [ ] **Step 4: Add non-secret examples**

Use these values in both backend env examples:

~~~dotenv
Plex__AccountId=replace-on-host
Plex__TvLibrarySectionId=replace-on-host
Plex__TvLibrarySectionTitle=replace-on-host
Plex__HistoryPageSize=100
Plex__HistoryPollIntervalMinutes=5
Plex__HistoryOverlapHours=24
TraktHistory__Enabled=false
TraktHistory__OutboxPollSeconds=30
TraktHistory__LeaseMinutes=5
TraktHistory__AmbiguousQuarantineMinutes=15
TraktHistory__MaxAttempts=8
TraktHistory__DeliveryBatchSize=5
TraktHistory__ReconciliationBatchSize=20
TRAKT_HISTORY_SYNC_APPLY=false
~~~

Do not duplicate these credentials or switches in the worker environment.

- [ ] **Step 5: Verify deployment contract and Compose rendering**

Run:

~~~powershell
python -m pytest tests\deployment -q
$validationRoot = Join-Path $env:TEMP "watchlist-tv-phase2-compose"
$configDir = Join-Path $validationRoot "config"
$dataDir = Join-Path $validationRoot "data"
New-Item -ItemType Directory -Force $configDir, "$dataDir/backend/data-protection-keys", "$dataDir/worker" | Out-Null
Copy-Item deploy\production\backend.env.example "$configDir/backend.env"
Copy-Item deploy\production\worker.env.example "$configDir/worker.env"
$env:WATCHLIST_CONFIG_DIR = $configDir
$env:WATCHLIST_DATA_DIR = $dataDir
$env:WATCHLIST_RUNTIME_UID = "10001"
$env:WATCHLIST_RUNTIME_GID = "10001"
$env:WATCHLIST_RELEASE = "tv-phase-2-validation"
$env:WATCHLIST_BACKEND_ENV_FILE = (Resolve-Path deploy\backend\watchlist-backend.env.example).Path
docker compose -f deploy\backend\compose.yaml config --quiet
docker compose -f deploy\production\compose.yaml config --quiet
Remove-Item Env:WATCHLIST_BACKEND_ENV_FILE, Env:WATCHLIST_CONFIG_DIR, Env:WATCHLIST_DATA_DIR, Env:WATCHLIST_RUNTIME_UID, Env:WATCHLIST_RUNTIME_GID, Env:WATCHLIST_RELEASE
Remove-Item -Recurse -Force $validationRoot
~~~

Expected: deployment tests pass and Compose exits 0. The temporary copied env
files stay outside the repository, contain non-secret example values only, and
are removed after validation.

- [ ] **Step 6: Build the backend container**

Run:

~~~powershell
docker build -f backend\src\Watchlist.Api\Dockerfile -t watchlist-api:tv-phase2 .
~~~

Expected: image build succeeds and no credential appears in build output.

- [ ] **Step 7: Commit production-safe configuration**

~~~powershell
git add backend/src deploy tests/deployment/test_tv_history_config.py
git commit -m "build(tv): configure guarded history synchronization"
~~~

### Task 13: Update OKF And Complete Phase 2 Verification

**Files:**
- Modify: `docs/architecture/system_boundaries.md`
- Modify: `docs/systems/backend_service.md`
- Modify: `docs/apis/backend_api.md`
- Modify: `docs/apis/export_endpoints.md`
- Modify: `docs/integrations/plex.md`
- Modify: `docs/integrations/trakt.md`
- Create: `docs/runbooks/tv_history_operations.md`
- Modify: `docs/runbooks/local_development.md`
- Modify: `docs/runbooks/validation.md`
- Modify: `docs/reports/tv_integration_rollout.md`
- Modify: `docs/backlog/roadmap.md`
- Modify: `docs/log.md`
- Modify: `docs/index.md`

- [ ] **Step 1: Update durable architecture and integration knowledge**

Document:

- backend ownership of configured-account Plex history and Trakt writes;
- exact account/library/machine binding;
- primary and fallback event keys;
- complete backfill, 24-hour overlap, and watermark-last rule;
- bootstrap reconciled/superseded semantics;
- every outbox state and stable blocker code;
- one-event non-idempotent POST behavior;
- 15-minute quarantine and two completed reconciliation polls;
- Plex library read-only status;
- `TRAKT_HISTORY_SYNC_APPLY=false` default; and
- cleanup phases remaining disabled.

Update each concept's frontmatter version and timestamp. Link
`tv_history_operations.md` from `docs/index.md`.

- [ ] **Step 2: Write the operator runbook**

The runbook must give exact commands and expected observations for:

1. configure account/library with apply false;
2. verify Play History capability;
3. wait for complete backfill;
4. compare accepted/quarantined/reconciled/superseded counts;
5. require bootstrap complete before enabling writes;
6. enable a supervised five-item batch;
7. inspect confirmed, ambiguous, retry-wait, and dead-letter counts;
8. immediately disable apply on any unexplained ambiguity or duplicate;
9. wait for a clean outbox and a new complete TV generation; and
10. keep Phase 3 and destructive worker gates disabled.

Do not include real URLs, tokens, account IDs, OAuth codes, or media paths.

- [ ] **Step 3: Update API and validation contracts**

`backend_api.md` documents the protected Trakt status `history` object.
`export_endpoints.md` documents Plex binding, watermark, and stable per-show
blockers in `GET /api/export/tv/sync-state`.
`validation.md` adds the focused Phase 2 tests and keeps full Mongo
repository tests mandatory. Add a pending Phase 2 evidence table to the
cumulative `tv_integration_rollout.md` ledger created by Phase 1. It has rows
for UTC timestamps, deployed commit SHA, configured Plex account/library
binding, backfill and watermark state, bootstrap counts, supervised Trakt
batch action IDs, outbox and quarantine counts, blocker review, and all gate
values. Do not mark outcomes successful before the supervised handoff.

- [ ] **Step 4: Run OKF and placeholder validation**

Run:

~~~powershell
python tests\validate_okf.py
rg -n "TBD|TODO|implement later|fill in details|Add appropriate error handling|Write tests for the above|Similar to Task" docs
~~~

Expected: OKF validation passes. The scan finds no new placeholder in the
Phase 2 documents; existing unrelated historical matches are reviewed without
editing unrelated files.

- [ ] **Step 5: Run the full local verification matrix**

With MongoDB 8 on `localhost:27017`:

~~~powershell
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
dotnet test backend\Watchlist.sln --configuration Release --no-build
python tests\validate_okf.py
python -m pytest tests\deployment -q
python -m py_compile scripts\check-movie-ci.py
git diff --check
~~~

Expected: every command exits 0. No test is excluded because it requires
MongoDB.

- [ ] **Step 6: Run redacted publishable-tree secret scans**

From a clean exact-tree worktree, run:

~~~powershell
docker run --rm -v "$($PWD.Path):/repo" zrichezav/gitleaks:v8.30.1 git --redact --no-banner /repo
docker run --rm -v "$($PWD.Path):/repo" zrichezav/gitleaks:v8.30.1 dir --redact --no-banner /repo
~~~

Expected: both scans exit 0 and print no secret value. Any confirmed finding
blocks commit, push, and rollout.

- [ ] **Step 7: Commit documentation and the Phase 2 evidence table**

~~~powershell
git add docs
git commit -m "docs(tv): document Plex to Trakt history operations"
~~~

Expected: this commit includes the pending Phase 2 section of
`docs/reports/tv_integration_rollout.md`; the report already exists from Phase
1 and is modified, never recreated or marked successful without evidence.

- [ ] **Step 8: Hand off the supervised rollout**

Leave these values in every committed example:

~~~dotenv
TraktHistory__Enabled=false
TRAKT_HISTORY_SYNC_APPLY=false
TV_SYNC_APPLY=false
TV_SYNC_ALLOW_SEASON_FILE_DELETION=false
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
~~~

As a host-only supervised override, the operator first sets
`TraktHistory__Enabled=true` while keeping `TRAKT_HISTORY_SYNC_APPLY=false` to
collect and review the bootstrap. The operator may enable
`TRAKT_HISTORY_SYNC_APPLY` only after the runbook's bootstrap review. Never
commit either enabled value. Phase 2 is complete when one supervised batch converges, the
outbox is clean or every exception is explicitly quarantined, a fresh complete
TV generation reflects confirmed progress, and no Sonarr or Plex mutation
method was called.

- [ ] **Step 9: Record and commit the Phase 2 exit evidence**

Replace pending outcomes in the Phase 2 ledger section with UTC timestamps,
the deployed commit SHA, redacted action IDs/counts, and reviewed gate values
from Step 8. Update `docs/log.md` only after every Phase 2 completion condition
is true.

~~~powershell
python tests\validate_okf.py
git diff --check
git add docs/reports/tv_integration_rollout.md docs/log.md
git commit -m "docs(tv): record Phase 2 integration evidence"
~~~

Phase 3 may start only after this evidence commit exists. Phase 3 modifies the
same cumulative ledger and never creates a replacement.

## Phase 2 Completion Checklist

- [ ] All accessible configured-account Plex episode history is in the ledger.
- [ ] Overlapping polls do not duplicate one play.
- [ ] A legitimate post-cutover rewatch creates a distinct event.
- [ ] Every newly accepted post-cutover Trakt-eligible event, including a late
  overlap arrival, reaches one deterministic outbox row through the separate
  crash-safe routing stage.
- [ ] Bootstrap does not replay historical rewatches for already watched
  Trakt episodes.
- [ ] Every Trakt POST contains exactly one episode.
- [ ] Timeout, connection loss, and 5xx receive no blind retry.
- [ ] Ambiguous rows wait at least 15 minutes and require two completed
  no-match reads before retry.
- [ ] Capability, bootstrap, watermark, quarantine, and outbox state appear in
  redacted status and worker export.
- [ ] Incomplete or unhealthy history freezes lifecycle mutation.
- [ ] `TRAKT_HISTORY_SYNC_APPLY` defaults false independently of every worker
  switch.
- [ ] Existing movie synchronization and Android read-only behavior are
  unchanged.

## Links

- [TV Integration Design](../specs/2026-07-13-tv-show-integration-design.md)
- [System Boundaries](../../architecture/system_boundaries.md)
- [Backend API](../../apis/backend_api.md)
- [Plex Integration](../../integrations/plex.md)
- [Validation](../../runbooks/validation.md)
