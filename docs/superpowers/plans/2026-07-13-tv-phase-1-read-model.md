---
type: Backlog
title: TV Phase 1 Read Model Implementation Plan
description: TDD execution plan for Trakt device OAuth, complete TV generations, Polish provider enrichment, read APIs, Android TV progress UI, and secret-safe deployment with every TV mutation disabled.
tags:
  - tv
  - trakt
  - mongodb
  - android-tv
  - tmdb
  - ci-cd
timestamp: 2026-07-13T00:00:00Z
version: 0.1.0
---

# TV Phase 1 Read Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a non-destructive Trakt-backed TV read model with persistent device OAuth, complete publish-last generations, legacy-TV migration, Poland-specific provider data, browse/detail/export contracts, and a read-only Android TV experience.

**Architecture:** The .NET backend owns Trakt OAuth and source reads, TMDB enrichment, lifecycle reduction, and MongoDB generation publication. Readers resolve exactly one published TV generation; source or validation failures retain the previous pointer. Android consumes versioned backend DTOs and never calls a mutation endpoint, while the future worker receives an explicitly non-mutation-capable export.

**Tech Stack:** .NET 10 minimal API, ASP.NET Data Protection, MongoDB 8, `HttpClient`, xUnit, FluentAssertions, Java 17 Android TV, JUnit 4, Gradle 8, Docker Compose, GitHub Actions, Bash, and OKF Markdown.

---

## Phase Boundary And Locked Contracts

This plan implements only Phase 1 of the approved design. It deliberately does
not ingest Plex episode history, write Trakt history, mutate Sonarr, mutate the
Plex watchlist, create cleanup authorizations, or enable any worker TV path.

Every published Phase 1 TV worker export must use the same version-1 envelope
that Phase 3 consumes:

```json
{
  "schemaVersion": "1",
  "mutationCapable": false,
  "healthReasons": [
    "plex_history_phase_not_implemented",
    "worker_tv_mutation_disabled"
  ],
  "plexHistory": {
    "capable": false,
    "bootstrapComplete": false,
    "machineIdentifier": null,
    "accountId": null,
    "librarySectionId": null,
    "librarySectionTitle": null,
    "collectedAt": null,
    "watermark": null
  },
  "cleanupAuthorizations": []
}
```

The committed host examples must keep these switches false:

```dotenv
TRAKT_HISTORY_SYNC_APPLY=false
TV_SYNC_APPLY=false
TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false
TV_SYNC_ALLOW_SEASON_FILE_DELETION=false
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false
```

The public TV item identifier is `tv-trakt-{traktId}`. Trakt ID is the
canonical source key; TVDB, TMDB, and IMDb are exact supporting identities.
The only persistent show lifecycle values are `active`, `caught_up`,
`source_removed`, `terminal_cleanup_pending`, and `retired_terminal`.
`reactivated` is an event, not a persistent state. Phase 1 may publish only
`active`, `caught_up`, and `source_removed`; it parses the other values so the
versioned client contract does not need a breaking change later.

TV browse-state query rules are fixed:

- `collection=tv` with no `state` means `state=active`;
- accepted values are `active`, `caught_up`, and `retired`;
- `retired` maps to stored `retired_terminal`;
- a `state` query with `collection=movie` or `collection=all` returns `400`;
- `collection=all` returns movies plus active TV rows only; and
- legacy TV rows in `watchlist_items` are never read after migration.

### Task 1: Introduce Phase 1 TV Domain And Configuration Contracts

**Files:**
- Create: `backend/src/Watchlist.Domain/TvLifecycleState.cs`
- Create: `backend/src/Watchlist.Domain/TvIdentityStatus.cs`
- Create: `backend/src/Watchlist.Domain/TvProviderState.cs`
- Create: `backend/src/Watchlist.Domain/TvProviderCategory.cs`
- Create: `backend/src/Watchlist.Domain/TvGenerationKind.cs`
- Create: `backend/src/Watchlist.Domain/TvEpisodeProgress.cs`
- Create: `backend/src/Watchlist.Domain/TvSpecialEpisodeIdentity.cs`
- Create: `backend/src/Watchlist.Domain/TvProviderOffer.cs`
- Create: `backend/src/Watchlist.Domain/TvProviderAvailability.cs`
- Create: `backend/src/Watchlist.Domain/TvSeasonProgress.cs`
- Create: `backend/src/Watchlist.Domain/TvLifecycleEvent.cs`
- Create: `backend/src/Watchlist.Domain/TvShow.cs`
- Create: `backend/src/Watchlist.Infrastructure/TraktOptions.cs`
- Create: `backend/src/Watchlist.Infrastructure/DataProtectionKeyRingOptions.cs`
- Modify: `backend/src/Watchlist.Infrastructure/TmdbOptions.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoDbOptions.cs`
- Modify: `backend/src/Watchlist.Infrastructure/Watchlist.Infrastructure.csproj`
- Test: `backend/tests/Watchlist.Application.Tests/DomainEnumTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvOptionsTests.cs`

- [ ] **Step 1: Write failing enum and option-default tests**

Add assertions for every stable value and the production-safe defaults:

```csharp
[Fact]
public void TvContractValues_AreStable()
{
    Enum.GetValues<TvLifecycleState>().Should().Equal(
        TvLifecycleState.Active,
        TvLifecycleState.CaughtUp,
        TvLifecycleState.SourceRemoved,
        TvLifecycleState.TerminalCleanupPending,
        TvLifecycleState.RetiredTerminal);
    Enum.GetValues<TvProviderCategory>().Should().Equal(
        TvProviderCategory.Flatrate,
        TvProviderCategory.Free,
        TvProviderCategory.Ads,
        TvProviderCategory.Rent,
        TvProviderCategory.Buy);
}

[Fact]
public void TvOptions_DefaultsArePolandAndReadOnly()
{
    TraktOptions trakt = new();
    TmdbOptions tmdb = new();

    trakt.BaseUrl.Should().Be("https://api.trakt.tv");
    trakt.RedirectUri.Should().Be("urn:ietf:wg:oauth:2.0:oob");
    trakt.ActivityPollInterval.Should().Be(TimeSpan.FromMinutes(5));
    trakt.FullSyncInterval.Should().Be(TimeSpan.FromHours(1));
    trakt.TokenRefreshSkew.Should().Be(TimeSpan.FromMinutes(5));
    tmdb.ProviderRegion.Should().Be("PL");
    tmdb.OwnedProviderIds.Should().Equal(119, 1899, 1773);
    tmdb.ProviderCacheLifetime.Should().Be(TimeSpan.FromHours(24));
}
```

Add an options-binding test using `Tmdb__OwnedProviderIds__0` through `__2`
and prove the resulting integer IDs, not provider display names, are used. The
production list must represent the same subscribed services documented for the
movie path; changing an upstream display name must not change either movie or
TV matching.

- [ ] **Step 2: Run the tests and verify the missing types fail compilation**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~DomainEnumTests|FullyQualifiedName~TvOptionsTests"
```

Expected: build failure naming `TvLifecycleState`, `TvProviderCategory`, or
`TraktOptions` as missing.

- [ ] **Step 3: Add the exact domain enums**

Use one public type per file and these members:

```csharp
namespace Watchlist.Domain;

public enum TvLifecycleState
{
    Active = 0,
    CaughtUp = 1,
    SourceRemoved = 2,
    TerminalCleanupPending = 3,
    RetiredTerminal = 4
}
```

```csharp
namespace Watchlist.Domain;

public enum TvIdentityStatus
{
    Verified = 0,
    Missing = 1,
    Conflict = 2,
    LegacyUnresolved = 3
}
```

```csharp
namespace Watchlist.Domain;

public enum TvProviderState
{
    Available = 0,
    ConfirmedUnavailable = 1,
    Unknown = 2,
    Stale = 3
}
```

```csharp
namespace Watchlist.Domain;

public enum TvProviderCategory
{
    Flatrate = 0,
    Free = 1,
    Ads = 2,
    Rent = 3,
    Buy = 4
}
```

```csharp
namespace Watchlist.Domain;

public enum TvGenerationKind
{
    ScheduledFull = 0,
    ActivityFull = 1
}
```

- [ ] **Step 4: Add the immutable TV aggregate records**

Use the following public contracts. `TvProviderAvailability.Unknown` is the
only default provider state, and no destination or cleanup field belongs in
the TV show, season, episode, or provider source/read-model records. Lifecycle
events carry their canonical predicate hash from the start; Phase 4 adds a
typed cleanup-intent payload only to cleanup authorization events.

```csharp
namespace Watchlist.Domain;

public sealed record TvEpisodeProgress(
    long TraktEpisodeId,
    int? TvdbId,
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    DateTimeOffset? AiredAt,
    bool Watched,
    DateTimeOffset? WatchedAt);
```

```csharp
namespace Watchlist.Domain;

public sealed record TvSpecialEpisodeIdentity(
    long TraktEpisodeId,
    int? TvdbId,
    int SeasonNumber,
    int EpisodeNumber);
```

`TvSpecialEpisodeIdentity` is an identity-only handoff to the Phase 2 Plex
resolver. It requires `SeasonNumber == 0`, a positive Trakt episode ID, a
positive episode number, and a nullable positive TVDB episode ID. It never
contributes to watched/completed/aired totals, `LastWatchedEpisode`,
`NextEpisode`, `TvSeasonProgress`, provider claims, automatic search, or either
cleanup evaluator.

```csharp
namespace Watchlist.Domain;

public sealed record TvProviderOffer(
    int ProviderId,
    string ProviderName,
    TvProviderCategory Category,
    string? LogoUrl);
```

```csharp
namespace Watchlist.Domain;

public sealed record TvProviderAvailability(
    TvProviderState State,
    string Region,
    DateTimeOffset? FetchedAt,
    string? Link,
    IReadOnlyList<TvProviderOffer> Offers)
{
    public static TvProviderAvailability Unknown(string region) =>
        new(TvProviderState.Unknown, region, null, null, []);
}
```

```csharp
namespace Watchlist.Domain;

public sealed record TvSeasonProgress(
    int SeasonNumber,
    int AiredEpisodes,
    int CompletedEpisodes,
    bool HasKnownFutureEpisode,
    TvProviderAvailability Availability,
    IReadOnlyList<TvEpisodeProgress> Episodes);
```

```csharp
namespace Watchlist.Domain;

public sealed record TvLifecycleEvent(
    string Id,
    long TraktId,
    long Version,
    string GenerationId,
    string EventType,
    DateTimeOffset OccurredAt,
    string PredicateHash,
    string Reason);
```

Require `PredicateHash` to be a 64-character lowercase SHA-256 over canonical
ordered semantic event facts. It never includes timestamps, generation IDs,
serialized property order, secrets, or raw paths. Lifecycle evaluator and
Mongo round-trip tests assert the exact hash and reject blank/malformed values.

```csharp
namespace Watchlist.Domain;

public sealed record TvShow(
    string Id,
    long TraktId,
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    TvIdentityStatus IdentityStatus,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    string TraktStatus,
    bool InWatchlist,
    int AiredEpisodes,
    int CompletedEpisodes,
    TvEpisodeProgress? LastWatchedEpisode,
    TvEpisodeProgress? NextEpisode,
    IReadOnlyList<TvSeasonProgress> Seasons,
    IReadOnlyList<TvSpecialEpisodeIdentity> SpecialEpisodeIdentities,
    TvProviderAvailability Availability,
    TvLifecycleState LifecycleState,
    string? LastLifecycleEvent,
    long LifecycleVersion,
    int MissingScheduledConfirmations,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset MetadataFetchedAt,
    string GenerationId,
    string? LegacySourceId);
```

- [ ] **Step 5: Add validated infrastructure options**

Use these option properties and defaults:

```csharp
namespace Watchlist.Infrastructure;

public sealed class TraktOptions
{
    public const string SectionName = "Trakt";

    public string BaseUrl { get; init; } = "https://api.trakt.tv";
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = "urn:ietf:wg:oauth:2.0:oob";
    public TimeSpan ActivityPollInterval { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan FullSyncInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan MetadataRefreshInterval { get; init; } = TimeSpan.FromDays(1);
    public TimeSpan TokenRefreshSkew { get; init; } = TimeSpan.FromMinutes(5);
    public int PageSize { get; init; } = 100;
}
```

```csharp
namespace Watchlist.Infrastructure;

public sealed class DataProtectionKeyRingOptions
{
    public const string SectionName = "DataProtection";

    public string KeyRingPath { get; init; } = ".artifacts/data-protection-keys";
    public string ApplicationName { get; init; } = "watchlist-api";
}
```

Add to `TmdbOptions`:

```csharp
public string ProviderRegion { get; init; } = "PL";
public IReadOnlyList<int> OwnedProviderIds { get; init; } = [119, 1899, 1773];
public TimeSpan ProviderCacheLifetime { get; init; } = TimeSpan.FromHours(24);
```

Add to `MongoDbOptions`:

```csharp
public string TvShowsCollectionName { get; init; } = "tv_shows";
public string TvSyncManifestsCollectionName { get; init; } = "tv_sync_manifests";
public string TvLifecycleEventsCollectionName { get; init; } = "tv_lifecycle_events";
public string TraktConnectionsCollectionName { get; init; } = "trakt_connections";
```

Add `Microsoft.AspNetCore.DataProtection` version `10.0.9` to
`Watchlist.Infrastructure.csproj`; do not add a floating package version. This
servicing pin avoids GHSA-9mv3-2cwr-p262.

- [ ] **Step 6: Run focused and existing enum tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~DomainEnumTests|FullyQualifiedName~TvOptionsTests|FullyQualifiedName~MongoDbOptionsTests"
```

Expected: all selected tests pass, and existing movie enum numeric values remain
unchanged.

- [ ] **Step 7: Commit the Phase 1 domain contract**

```powershell
git add backend/src/Watchlist.Domain backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests
git commit -m "feat: define TV read model contracts"
```

### Task 2: Persist And Encrypt The Single Trakt Connection

**Files:**
- Create: `backend/src/Watchlist.Application/TraktConnectionStatusDto.cs`
- Create: `backend/src/Watchlist.Application/TraktConnection.cs`
- Create: `backend/src/Watchlist.Application/ITraktConnectionRepository.cs`
- Create: `backend/src/Watchlist.Application/ITraktTokenProtector.cs`
- Create: `backend/src/Watchlist.Infrastructure/DataProtectionTraktTokenProtector.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTraktConnectionDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTraktConnectionRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/DataProtectionKeyRingHostedService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/DataProtectionTraktTokenProtectorTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTraktConnectionRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/DataProtectionKeyRingHostedServiceTests.cs`

- [ ] **Step 1: Write failing encryption, restart, and repository tests**

Cover all of these behaviors:

```csharp
[Fact]
public void ProtectAndUnprotect_WithPersistedKeyRing_SurvivesProviderRestart()
{
    string path = tempDirectory.CreateSubdirectory("keys").FullName;
    IDataProtectionProvider first = BuildProvider(path);
    string ciphertext = new DataProtectionTraktTokenProtector(first).Protect("access-token");
    IDataProtectionProvider restarted = BuildProvider(path);

    new DataProtectionTraktTokenProtector(restarted)
        .Unprotect(ciphertext)
        .Should().Be("access-token");
    ciphertext.Should().NotContain("access-token");
}

[Fact]
public void Unprotect_WithDifferentKeyRing_ThrowsUnreadableConnectionException()
{
    string ciphertext = new DataProtectionTraktTokenProtector(BuildProvider(firstPath))
        .Protect("access-token");

    Action act = () => new DataProtectionTraktTokenProtector(BuildProvider(secondPath))
        .Unprotect(ciphertext);

    act.Should().Throw<TraktConnectionUnreadableException>();
}
```

Repository tests must save one pending connection, replace it with one
connected record, return no plaintext token from status reads, and erase the
record on disconnect. Assert raw Mongo fields contain ciphertext rather than
the supplied access token, refresh token, or device code.

- [ ] **Step 2: Run focused tests and verify they fail**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~DataProtectionTraktTokenProtectorTests|FullyQualifiedName~MongoTraktConnectionRepositoryTests|FullyQualifiedName~DataProtectionKeyRingHostedServiceTests"
```

Expected: compile failure for the missing repository and protector contracts.

- [ ] **Step 3: Add the connection and protection contracts**

Use one single-account record whose sensitive values are already protected
before repository persistence:

```csharp
namespace Watchlist.Application;

public sealed record TraktConnection(
    string State,
    string? ProtectedDeviceCode,
    string? UserCode,
    string? VerificationUrl,
    DateTimeOffset? DeviceCodeExpiresAt,
    TimeSpan? DevicePollInterval,
    DateTimeOffset? NextDevicePollAt,
    string? ProtectedAccessToken,
    string? ProtectedRefreshToken,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset UpdatedAt);
```

```csharp
namespace Watchlist.Application;

public interface ITraktTokenProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

public interface ITraktConnectionRepository
{
    Task<TraktConnection?> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(TraktConnection connection, CancellationToken cancellationToken);
    Task DeleteAsync(CancellationToken cancellationToken);
}
```

`TraktConnectionStatusDto` contains only `Status`, `ConnectedAt`,
`AccessTokenExpiresAt`, and `LastErrorCode`. It must not contain device code,
user code, access token, refresh token, client ID, or client secret.

- [ ] **Step 4: Implement purpose-scoped Data Protection**

`DataProtectionTraktTokenProtector` must create exactly this protector purpose:

```csharp
IDataProtector protector = provider.CreateProtector(
    "Watchlist.Trakt.SingleAccountTokens.v1");
```

Catch `CryptographicException` during unprotect and throw
`TraktConnectionUnreadableException` without including ciphertext or token
content in the message.

Register Data Protection in `AddWatchlistInfrastructure` using the configured
absolute keyring path and application name:

```csharp
DataProtectionKeyRingOptions keyRing = configuration
    .GetSection(DataProtectionKeyRingOptions.SectionName)
    .Get<DataProtectionKeyRingOptions>() ?? new();

services.AddDataProtection()
    .SetApplicationName(keyRing.ApplicationName)
    .PersistKeysToFileSystem(new DirectoryInfo(keyRing.KeyRingPath));
```

`DataProtectionKeyRingHostedService` creates the configured directory, protects
and immediately unprotects a random 32-byte probe, and fails startup if the
directory is missing, not absolute in Production, or not writable. The probe
value and protected payload are never logged.

- [ ] **Step 5: Implement the singleton Mongo document**

Use `_id=single-account`, replace-upsert writes, and these persisted fields:

```text
state
protectedDeviceCode
userCode
verificationUrl
deviceCodeExpiresAt
devicePollIntervalSeconds
nextDevicePollAt
protectedAccessToken
protectedRefreshToken
accessTokenExpiresAt
updatedAt
```

Delete the pending user code and protected device code when a token is stored.
Do not log or return the raw Mongo document.

- [ ] **Step 6: Verify encryption and Mongo persistence**

Run the focused tests from Step 2.

Expected: all selected tests pass; raw Mongo assertions prove that plaintext
token and device-code values are absent.

- [ ] **Step 7: Commit encrypted Trakt connection persistence**

```powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests
git commit -m "feat: persist encrypted Trakt connection"
```

### Task 3: Implement Protected Trakt Device OAuth And Token Refresh

**Files:**
- Create: `backend/src/Watchlist.Application/TraktDeviceStartDto.cs`
- Create: `backend/src/Watchlist.Application/TraktDeviceCode.cs`
- Create: `backend/src/Watchlist.Application/TraktTokenGrant.cs`
- Create: `backend/src/Watchlist.Application/ITraktOAuthClient.cs`
- Create: `backend/src/Watchlist.Application/ITraktAccessTokenProvider.cs`
- Create: `backend/src/Watchlist.Application/ITraktConnectionService.cs`
- Create: `backend/src/Watchlist.Application/TraktConnectionService.cs`
- Create: `backend/src/Watchlist.Infrastructure/TraktOAuthClient.cs`
- Create: `backend/src/Watchlist.Infrastructure/TraktDeviceAuthorizationHostedService.cs`
- Create: `backend/src/Watchlist.Application/TraktExceptions.cs`
- Create: `backend/src/Watchlist.Api/TvEndpointRouteBuilderExtensions.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TraktOAuthClientTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TraktConnectionServiceTests.cs`
- Create: `backend/tests/Watchlist.Api.Tests/TraktIntegrationApiTests.cs`

- [ ] **Step 1: Write failing OAuth client tests**

Use `HttpMessageHandler` fakes to assert the exact requests:

```text
POST /oauth/device/code
Content-Type: application/json
{"client_id":"client-id"}

POST /oauth/device/token
{"code":"device-code","client_id":"client-id","client_secret":"client-secret"}

POST /oauth/token
{"refresh_token":"refresh-token","client_id":"client-id","client_secret":"client-secret","redirect_uri":"urn:ietf:wg:oauth:2.0:oob","grant_type":"refresh_token"}
```

Map device polling status codes exactly:

```text
400 -> pending
404 -> invalid
409 -> already_used
410 -> expired
418 -> denied
429 -> slow_down and increase the next interval by five seconds
```

Malformed successful JSON throws `TraktParseException`; transport failure,
timeout, `401`, and `5xx` throw `TraktUnavailableException` with no response
body in the exception text.

- [ ] **Step 2: Write failing connection-service tests**

Test these transitions:

```text
disconnected -> pending after StartDeviceAsync
pending -> connected after a successful poll
pending -> pending on HTTP 400
pending -> revoked on denied, expired, invalid, or already-used response
connected -> connected with rotated tokens before five-minute expiry window
connected -> refresh_required on refresh rejection
any state -> disconnected after DisconnectAsync
unreadable ciphertext -> refresh_required with lastErrorCode=token_unreadable
```

Assert that `StartDeviceAsync` returns the user code once, while `GetStatusAsync`
never returns it.

- [ ] **Step 3: Run the focused tests and observe the missing services**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TraktOAuthClientTests|FullyQualifiedName~TraktConnectionServiceTests"
```

Expected: build failure for `ITraktOAuthClient` and
`TraktConnectionService`.

- [ ] **Step 4: Add the exact OAuth contracts**

```csharp
namespace Watchlist.Application;

public sealed record TraktDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUrl,
    TimeSpan ExpiresIn,
    TimeSpan Interval);

public sealed record TraktTokenGrant(
    string AccessToken,
    string RefreshToken,
    TimeSpan ExpiresIn,
    DateTimeOffset CreatedAt);

public sealed record TraktDeviceStartDto(
    string UserCode,
    string VerificationUrl,
    DateTimeOffset ExpiresAt,
    int PollIntervalSeconds);
```

```csharp
namespace Watchlist.Application;

public interface ITraktOAuthClient
{
    Task<TraktDeviceCode> StartDeviceAsync(CancellationToken cancellationToken);
    Task<TraktTokenGrant?> PollDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken);
    Task<TraktTokenGrant> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);
}
```

`ITraktConnectionService` exposes `StartDeviceAsync`, `PollPendingAsync`,
`GetStatusAsync`, and `DisconnectAsync`. It also implements
`ITraktAccessTokenProvider`, whose `GetValidAccessTokenAsync` refreshes within
the configured five-minute skew and whose `ForceRefreshAsync` is available
after a definite authentication rejection. Both methods throw
`TraktNotConnectedException` for every non-connected state.

- [ ] **Step 5: Implement the device polling hosted service**

`TraktDeviceAuthorizationHostedService` wakes once per second but calls
`PollPendingAsync` only when `NextDevicePollAt <= TimeProvider.GetUtcNow()`.
It stops polling at expiry, honors the persisted interval after restart, and
logs only stable state and error codes. Do not log user code, device code,
tokens, client secret, or response bodies.

- [ ] **Step 6: Add protected integration endpoints**

Create `TvEndpointRouteBuilderExtensions.MapTvEndpoints`, call it once from
`Program.cs`, and map an `/api/integrations` route group using the existing
`SyncApiKeyFilter`:

```text
POST   /api/integrations/trakt/device/start
GET    /api/integrations/trakt/status
DELETE /api/integrations/trakt/connection
```

Return `200` for start/status/delete, `409` with
`code=trakt_connection_pending` when start is repeated during an unexpired
pending flow, and `503` with `code=trakt_unavailable` for definite upstream
failure. No Android code calls these routes.

- [ ] **Step 7: Verify API authorization and redaction**

Run:

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TraktIntegrationApiTests"
```

Expected: missing or wrong `X-Watchlist-Sync-Key` returns `401`; a correct key
returns the documented DTO; serialized status contains none of `deviceCode`,
`accessToken`, `refreshToken`, `clientSecret`, or `protected` fields.

- [ ] **Step 8: Run all focused OAuth tests and commit**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TraktOAuthClientTests|FullyQualifiedName~TraktConnectionServiceTests|FullyQualifiedName~DataProtectionTraktTokenProtectorTests"
git add backend/src backend/tests
git commit -m "feat: add Trakt device authorization"
```

Expected: all selected tests pass and the commit contains no credential value.

### Task 4: Build The Paginated Trakt TV Read Client

**Files:**
- Create: `backend/src/Watchlist.Application/TraktActivityCursor.cs`
- Create: `backend/src/Watchlist.Application/TraktShowIds.cs`
- Create: `backend/src/Watchlist.Application/TraktWatchlistShow.cs`
- Create: `backend/src/Watchlist.Application/TraktWatchedShowProgress.cs`
- Create: `backend/src/Watchlist.Application/TraktDetailedShowProgress.cs`
- Create: `backend/src/Watchlist.Application/TraktDetailedSeasonProgress.cs`
- Create: `backend/src/Watchlist.Application/TraktDetailedEpisodeProgress.cs`
- Create: `backend/src/Watchlist.Application/TraktShowMetadata.cs`
- Create: `backend/src/Watchlist.Application/TraktSeasonEpisode.cs`
- Create: `backend/src/Watchlist.Application/ITraktTvClient.cs`
- Create: `backend/src/Watchlist.Infrastructure/TraktTvClient.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TraktTvClientTests.cs`

- [ ] **Step 1: Write failing request and pagination tests**

Test the exact request set:

```text
GET /sync/last_activities
GET /sync/watchlist/shows/added/asc?page=1&limit=100
GET /sync/progress/watched?hide_completed=false&hide_not_completed=false&only_rewatching=false&page=1&limit=100
GET /shows/{traktId}/progress/watched?hidden=false&specials=false&count_specials=false
GET /shows/{traktId}?extended=full
GET /shows/{traktId}/seasons/{seasonNumber}?extended=full
GET /shows/{traktId}/seasons/0?extended=full
```

Every authenticated request must carry:

```text
Authorization: Bearer access-token
trakt-api-version: 2
trakt-api-key: client-id
```

For watchlist and progress, return two pages from the fake handler with
`X-Pagination-Page-Count: 2`, assert both are fetched, and assert input order
does not affect the returned canonical Trakt-ID order.

- [ ] **Step 2: Add rejection tests for unsafe source data**

Assert `TraktParseException` for:

- a missing or nonpositive Trakt show ID;
- a missing or nonpositive Trakt episode ID in a season schedule;
- duplicate Trakt IDs across two rows on the same endpoint;
- missing page-count headers on a nonempty paginated response;
- `completed < 0`, `aired < 0`, or `completed > aired`;
- malformed next/last episode identity;
- duplicate season or episode numbers in detailed progress;
- a season-0 response containing a nonzero season number, duplicate episode
  number, or missing/nonpositive Trakt episode ID; and
- malformed JSON on any successful response.

Test `429` mapping to `TraktRateLimitedException` with the parsed
`Retry-After`, and ensure the client does not retry reads inside the adapter.

- [ ] **Step 3: Run the client test and verify it fails**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TraktTvClientTests"
```

Expected: build failure for `ITraktTvClient`.

- [ ] **Step 4: Add the exact read-client interface**

```csharp
namespace Watchlist.Application;

public interface ITraktTvClient
{
    Task<TraktActivityCursor> GetLastActivitiesAsync(
        string accessToken,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<TraktWatchlistShow>> GetWatchlistAsync(
        string accessToken,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<TraktWatchedShowProgress>> GetWatchedProgressAsync(
        string accessToken,
        CancellationToken cancellationToken);
    Task<TraktDetailedShowProgress> GetDetailedProgressAsync(
        string accessToken,
        long traktId,
        CancellationToken cancellationToken);
    Task<TraktShowMetadata> GetShowMetadataAsync(
        string accessToken,
        long traktId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<TraktSeasonEpisode>> GetSeasonAsync(
        string accessToken,
        long traktId,
        int seasonNumber,
        CancellationToken cancellationToken);
}

public sealed record TraktDetailedEpisodeProgress(
    int SeasonNumber,
    int EpisodeNumber,
    bool Completed,
    DateTimeOffset? LastWatchedAt);

public sealed record TraktSeasonEpisode(
    long TraktEpisodeId,
    int? TvdbId,
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    DateTimeOffset? FirstAired);
```

`TraktActivityCursor` stores the show-watchlist and episode-watched UTC
timestamps used for the pre/post race check. `TraktShowIds` stores positive
Trakt ID plus nullable positive TVDB/TMDB IDs and normalized IMDb ID.
`TraktWatchedShowProgress` stores aired/completed totals and next/last episode.
`TraktDetailedEpisodeProgress` is intentionally identity-free because the
Trakt progress response provides season/episode number, completion, and
`last_watched_at`, but not episode IDs. `TraktSeasonEpisode` retains the
positive Trakt episode ID plus nullable positive TVDB episode ID from the full
season schedule. The sync service joins those two responses exactly before it
constructs `TvEpisodeProgress`; from that point the positive Trakt episode ID
is persisted through Mongo generation rows and the worker DTO. The adapter
also reads `GET /shows/{traktId}/seasons/0?extended=full` for every tracked
show. It canonicalizes that response into `TvSpecialEpisodeIdentity` rows so
Phase 2 can resolve an exact local Plex S00E identity without title matching.
An empty season-0 response is valid. Specials are persisted in the separate
identity-only list on `TvShow`; they are never converted to
`TvEpisodeProgress` and remain excluded from progress, provider, search, and
cleanup semantics.

- [ ] **Step 5: Implement deterministic all-page reads**

Use page size from `TraktOptions`, fetch pages sequentially, require a stable
page count, concatenate, reject duplicates, then order by Trakt ID. Do not use
the first page as a complete snapshot. Deserialize with case-insensitive
properties plus explicit `JsonPropertyName` for snake-case fields.

- [ ] **Step 6: Verify every Trakt adapter case**

Run the command from Step 3.

Expected: every pagination, header, invariant, status, and malformed-response
test passes.

- [ ] **Step 7: Commit the Trakt read adapter**

```powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests/TraktTvClientTests.cs
git commit -m "feat: read complete Trakt TV state"
```

### Task 5: Reduce TV Lifecycle And Reject Unsafe Generations

**Files:**
- Create: `backend/src/Watchlist.Application/TvLifecycleDecision.cs`
- Create: `backend/src/Watchlist.Application/TvLifecycleEvaluator.cs`
- Create: `backend/src/Watchlist.Application/TvGenerationManifest.cs`
- Create: `backend/src/Watchlist.Application/TvGenerationDraft.cs`
- Create: `backend/src/Watchlist.Application/TvSnapshotValidator.cs`
- Create: `backend/src/Watchlist.Application/TvSourceSnapshotRejectedException.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvLifecycleEvaluatorTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvSnapshotValidatorTests.cs`

- [ ] **Step 1: Write the lifecycle transition table as failing tests**

Cover this complete Phase 1 table with fixed UTC times and generation IDs:

```text
no previous row + explicit watchlist                 -> active + added event, version 1
no previous row + completed < aired                  -> active + added event, version 1
active + completed == aired > 0, not watchlisted     -> caught_up + caught_up event
caught_up + newly completed < aired                  -> active + reactivated event
source_removed + explicit watchlist                  -> active + reactivated event
active + first scheduled absence                     -> active, missing confirmations 1, no event
active + second distinct scheduled absence           -> source_removed + source_removed event
active + any activity-triggered absence              -> retain state and confirmation count
caught_up + ended or canceled status in Phase 1      -> caught_up, never terminal_cleanup_pending
explicit watchlist + completed == aired              -> active
aired == 0 + explicit watchlist                      -> active
aired == 0 + progress-only row                       -> reject impossible tracked progress
```

Use assertions such as:

```csharp
TvLifecycleDecision result = evaluator.Evaluate(
    previous,
    presentInCurrentSource: false,
    inWatchlist: false,
    airedEpisodes: previous.AiredEpisodes,
    completedEpisodes: previous.CompletedEpisodes,
    TvGenerationKind.ScheduledFull,
    "generation-2",
    now);

result.State.Should().Be(TvLifecycleState.SourceRemoved);
result.MissingScheduledConfirmations.Should().Be(2);
result.Event!.EventType.Should().Be("source_removed");
```

- [ ] **Step 2: Write failing generation validation tests**

`TvSnapshotValidatorTests` must reject before repository calls when any of these
facts is present:

```text
pre-activity cursor differs from post-activity cursor
duplicate Trakt show ID
duplicate TVDB ID assigned to two current shows
nonpositive identity
completed greater than aired
negative counts
next or last episode references a different show
duplicate season number
duplicate episode number within a season
completed season count does not equal watched regular episodes
generation kind or generation ID is missing
```

It must accept a valid empty current source because two scheduled generations,
not a validator shortcut, control soft source removal.

- [ ] **Step 3: Run the tests and verify the evaluator is absent**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvLifecycleEvaluatorTests|FullyQualifiedName~TvSnapshotValidatorTests"
```

Expected: build failure naming `TvLifecycleEvaluator` or
`TvSnapshotValidator`.

- [ ] **Step 4: Add the evaluator contract and stable event IDs**

```csharp
namespace Watchlist.Application;

public sealed record TvLifecycleDecision(
    TvLifecycleState State,
    long LifecycleVersion,
    int MissingScheduledConfirmations,
    TvLifecycleEvent? Event);
```

The evaluator derives event IDs exactly as:

```csharp
string eventId = $"tv:{traktId}:{nextVersion}:{eventType}";
```

Increment lifecycle version only when an event is emitted. Use event types
`added`, `caught_up`, `reactivated`, and `source_removed`. A repeated equivalent
generation emits no event and retains the version.

- [ ] **Step 5: Add generation draft and manifest contracts**

```csharp
namespace Watchlist.Application;

public sealed record TvGenerationDraft(
    string GenerationId,
    TvGenerationKind Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    TraktActivityCursor ActivityBefore,
    TraktActivityCursor ActivityAfter,
    int WatchlistPageCount,
    int WatchlistItemCount,
    int ProgressPageCount,
    int ProgressItemCount,
    string RequestContractVersion,
    IReadOnlyDictionary<string, string> RequestFilters,
    string MembershipHash,
    string ProgressHash,
    IReadOnlyList<TvShow> Shows,
    IReadOnlyList<TvLifecycleEvent> LifecycleEvents,
    IReadOnlyList<string> EnrichmentErrors);

public sealed record TvGenerationManifest(
    string GenerationId,
    string? PreviousGenerationId,
    TvGenerationKind Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    DateTimeOffset PublishedAt,
    TraktActivityCursor ActivityCursor,
    int WatchlistPageCount,
    int WatchlistItemCount,
    int ProgressPageCount,
    int ProgressItemCount,
    string RequestContractVersion,
    IReadOnlyDictionary<string, string> RequestFilters,
    string MembershipHash,
    string ProgressHash,
    DateTimeOffset? PlexHistoryCollectedAt,
    DateTimeOffset? PlexHistoryWatermark,
    DateTimeOffset? ProviderEnrichmentCompletedAt,
    string ValidationStatus,
    IReadOnlyList<string> ValidationFailureReasons,
    IReadOnlyList<string> LifecycleEventIds,
    IReadOnlyList<string> CleanupEventIds,
    bool MutationCapable,
    IReadOnlyList<string> HealthReasons,
    IReadOnlyList<string> EnrichmentErrors);
```

Use `RequestContractVersion="trakt-tv-v1"`, persist the exact canonical query
filters, and set `ValidationStatus="valid"` only after every source invariant
and the pre/post activity cursor pass. Phase 1 stores null Plex fields and an
empty cleanup-event list; Phase 2 and cleanup phases populate those same
forward-compatible fields rather than changing the manifest shape. For Phase
1, manifest construction always supplies the two locked health
reasons and `MutationCapable=false`.

- [ ] **Step 6: Implement canonical hashing and validation**

Hash UTF-8 JSON with SHA-256 after ordering shows by Trakt ID, seasons by season
number, and episodes by episode number. Use lowercase hexadecimal. Membership
hash includes Trakt ID and `InWatchlist`; progress hash includes Trakt ID,
aired/completed, and every watched episode. Never include title, provider name,
or wall-clock generation time in either hash.

- [ ] **Step 7: Verify lifecycle and validator tests**

Run the command from Step 3.

Expected: every transition and rejection case passes, including the assertion
that `ended` and `canceled` do not enter a cleanup state in Phase 1.

- [ ] **Step 8: Commit pure lifecycle behavior**

```powershell
git add backend/src/Watchlist.Application backend/tests/Watchlist.Application.Tests
git commit -m "feat: reduce TV read lifecycle safely"
```

### Task 6: Add Exact-Identity TMDB TV And Poland Provider Enrichment

**Files:**
- Create: `backend/src/Watchlist.Application/TmdbTvProviderOfferDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbProviderRegionPresence.cs`
- Create: `backend/src/Watchlist.Application/TmdbTvProviderDataDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbWatchProviderCatalogDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbWatchProviderCatalogEntryDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbWatchProviderRegionsDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbProviderCatalogSnapshot.cs`
- Create: `backend/src/Watchlist.Application/ITmdbProviderCatalogRepository.cs`
- Create: `backend/src/Watchlist.Application/ITmdbTvEnrichmentService.cs`
- Create: `backend/src/Watchlist.Application/TmdbTvEnrichmentService.cs`
- Modify: `backend/src/Watchlist.Application/ITmdbTvMetadataClient.cs`
- Modify: `backend/src/Watchlist.Infrastructure/TmdbTvMetadataClient.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTmdbProviderCatalogDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTmdbProviderCatalogRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/TmdbProviderCatalogHostedService.cs`
- Modify: `backend/src/Watchlist.Application/TmdbMovieEnrichmentService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TmdbTvMetadataClientTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TmdbTvEnrichmentServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTmdbProviderCatalogRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TmdbMovieEnrichmentServiceTests.cs`

- [ ] **Step 1: Write failing provider endpoint and category tests**

Extend the client tests with exact requests:

```text
GET /tv/1399/watch/providers
GET /tv/1399/season/1/watch/providers
GET /watch/providers/tv
GET /watch/providers/regions
```

Return a `PL` payload containing provider IDs `119`, `1899`, `1773`, and `8`
across `flatrate`, `free`, `ads`, `rent`, and `buy`. Assert the client preserves
the TMDB provider ID, name, logo path, category, result link, and successful
fetch time without collapsing categories or matching names.

For the catalog endpoints, return two provider rows with stable IDs, display
priorities, names, and logo paths plus a regions payload containing `PL` and
`DE`. Assert exact paths, complete array parsing, positive unique IDs,
case-sensitive unique ISO region codes, and rejection of missing/duplicate IDs
or malformed arrays. No provider endpoint may be inferred from the series
response.

Add these response cases:

```text
successful PL response with configured offers -> available
successful PL response with no configured ID   -> confirmed_unavailable
successful response without a PL key           -> unknown
transport or 5xx with no previous data          -> unknown
refresh failure with data older than 24 hours   -> stale and preserve offers
refresh failure with fresh prior data           -> preserve available/confirmed state
```

- [ ] **Step 2: Write failing exact identity tests**

Test the enrichment service rules:

```text
Trakt TVDB 121361 + TMDB external TVDB 121361 -> verified
Trakt TVDB 121361 + TMDB external TVDB 999999 -> conflict
missing Trakt TVDB + TMDB external TVDB 121361 -> verified with resolved TVDB
no positive TVDB from either source            -> missing
invalid or conflicting identity                -> still visible, never omitted
```

Assert provider selection uses configured IDs `[119, 1899, 1773]`; changing a
provider name while retaining ID `119` must not change the match.
Apply the same ID set to movie subscription matching while continuing to emit
the upstream provider display names in the existing movie DTO; existing movie
eligibility and JSON remain unchanged.

- [ ] **Step 3: Run focused TMDB TV tests and verify failure**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TmdbTvMetadataClientTests|FullyQualifiedName~TmdbTvEnrichmentServiceTests"
```

Expected: compile failure for the provider methods or enrichment service.

- [ ] **Step 4: Extend the metadata-client port**

Add these methods without changing the existing metadata method:

```csharp
Task<TmdbTvProviderDataDto> GetTvProvidersAsync(
    int tmdbId,
    CancellationToken cancellationToken);

Task<TmdbTvProviderDataDto> GetSeasonProvidersAsync(
    int tmdbId,
    int seasonNumber,
    CancellationToken cancellationToken);

Task<TmdbWatchProviderCatalogDto> GetProviderCatalogAsync(
    CancellationToken cancellationToken);

Task<TmdbWatchProviderRegionsDto> GetProviderRegionsAsync(
    CancellationToken cancellationToken);
```

Use these records:

```csharp
public sealed record TmdbTvProviderOfferDto(
    int ProviderId,
    string ProviderName,
    string Category,
    string? LogoPath);

public enum TmdbProviderRegionPresence
{
    Present,
    Missing
}

public sealed record TmdbTvProviderDataDto(
    string Region,
    TmdbProviderRegionPresence RegionPresence,
    DateTimeOffset FetchedAt,
    string? Link,
    IReadOnlyList<TmdbTvProviderOfferDto> Offers);

public sealed record TmdbWatchProviderCatalogEntryDto(
    int ProviderId,
    string ProviderName,
    string? LogoPath,
    int DisplayPriority);

public sealed record TmdbWatchProviderCatalogDto(
    DateTimeOffset FetchedAt,
    IReadOnlyList<TmdbWatchProviderCatalogEntryDto> Providers);

public sealed record TmdbWatchProviderRegionsDto(
    DateTimeOffset FetchedAt,
    IReadOnlyList<string> RegionCodes);

public sealed record TmdbProviderCatalogSnapshot(
    DateTimeOffset CatalogFetchedAt,
    DateTimeOffset RegionsFetchedAt,
    bool Stale,
    string? LastErrorCode,
    DateTimeOffset? LastErrorAt,
    IReadOnlyList<TmdbWatchProviderCatalogEntryDto> Providers,
    IReadOnlyList<string> RegionCodes);
```

The client returns all PL offers; the application service filters configured
IDs and maps categories. A successful payload without a PL result returns an
explicit `RegionPresence=Missing` result with `Region="PL"`, null link, and no
offers; it maps to `Unknown`, not an empty confirmed result. A present PL key
uses `RegionPresence=Present`; zero configured offers after a successful present
result maps to `ConfirmedUnavailable`. Reject impossible DTO combinations and
test missing PL separately from a present-but-empty PL object.

Persist the provider catalog and region list with fetch time. The hosted
service refreshes both once per day; a failed refresh retains the prior catalog
with stale health rather than deleting it. Tests prove provider IDs, region
membership, daily cadence, and redacted failures.

`MongoTmdbProviderCatalogDocument` stores one singleton snapshot with the exact
catalog/region fetch times, provider entries, region codes, `Stale`, stable
`LastErrorCode`, and nullable `LastErrorAt`. Repository tests reconstruct the
snapshot, prove a failed refresh changes only stale/error health, and prove a
later successful atomic replacement clears the error without exposing a raw
response body.

- [ ] **Step 5: Implement provider-state and cache semantics**

`TmdbTvEnrichmentService` receives the current source show, the previous
published row, and `now`. It refreshes metadata when older than the configured
daily interval and provider data when older than 24 hours. On provider failure:

```csharp
TvProviderAvailability availability = previous?.Availability switch
{
    { FetchedAt: DateTimeOffset fetchedAt } prior
        when now - fetchedAt <= options.Value.ProviderCacheLifetime => prior,
    { Offers.Count: > 0 } prior => prior with { State = TvProviderState.Stale },
    _ => TvProviderAvailability.Unknown(options.Value.ProviderRegion)
};
```

Record a stable redacted enrichment error containing Trakt ID, TMDB ID, stage,
and error code. Do not include URL query credentials, headers, or response body.

- [ ] **Step 6: Preserve unknown versus unavailable semantics**

Map a successful empty configured-ID match to `ConfirmedUnavailable`. Map all
missing, failed, or malformed provider fetches to `Unknown` or `Stale` as
defined above. Never derive `Not released` from provider absence. Keep movie
provider output and eligibility semantics unchanged while replacing its
internal provider-name allowlist with the same configured stable IDs.

- [ ] **Step 7: Verify all TMDB TV tests and commit**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TmdbTvMetadataClientTests|FullyQualifiedName~TmdbTvEnrichmentServiceTests|FullyQualifiedName~TmdbMovieEnrichmentServiceTests"
git add backend/src backend/tests/Watchlist.Application.Tests
git commit -m "feat: enrich TV shows for Poland"
```

Expected: TV tests pass and existing movie provider tests remain green.

### Task 7: Implement MongoDB Publish-Last TV Generations

**Files:**
- Create: `backend/src/Watchlist.Application/ITvGenerationRepository.cs`
- Create: `backend/src/Watchlist.Application/ITvShowReadRepository.cs`
- Create: `backend/src/Watchlist.Application/PublishedTvGeneration.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvEpisodeProgressDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvSpecialEpisodeIdentityDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvSeasonProgressDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvProviderOfferDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvProviderAvailabilityDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvShowDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvLifecycleEventDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvSyncManifestDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvPublishedPointerDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvGenerationRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvShowReadRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvIndexHostedService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvGenerationRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvShowReadRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvIndexHostedServiceTests.cs`

- [ ] **Step 1: Write failing publish-last integration tests**

Using the local MongoDB test database, prove this sequence:

```text
stage generation-1 rows and events
stage immutable generation-1 manifest
publish pointer to generation-1
read returns only generation-1
stage generation-2 rows and events
read still returns only generation-1
publish generation-2 pointer
read returns only generation-2
```

Also prove:

- publishing without its immutable manifest fails;
- duplicate `(generationId, traktId)` is rejected;
- repeated staging of the identical stable lifecycle event is idempotent;
- one conflicting stable event ID is rejected;
- failed/staged rows never appear in browse or export;
- a reader obtains the pointer once and never mixes rows after a concurrent
  pointer change; and
- every manifest is immutable after insertion; and
- season-0 identity rows round-trip only through
  `SpecialEpisodeIdentities`, remain generation-scoped, and never appear as a
  season/progress row.

- [ ] **Step 2: Run repository tests and verify missing repositories**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~MongoTvGenerationRepositoryTests|FullyQualifiedName~MongoTvShowReadRepositoryTests"
```

Expected: build failure for `ITvGenerationRepository` or
`MongoTvGenerationRepository`.

- [ ] **Step 3: Add repository interfaces**

```csharp
namespace Watchlist.Application;

public interface ITvGenerationRepository
{
    Task StageAsync(TvGenerationDraft draft, CancellationToken cancellationToken);
    Task PublishAsync(TvGenerationManifest manifest, CancellationToken cancellationToken);
    Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken);
}

public interface ITvShowReadRepository
{
    Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken);
    Task<TvShow?> GetPublishedShowAsync(string id, CancellationToken cancellationToken);
}

public sealed record PublishedTvGeneration(
    TvGenerationManifest Manifest,
    IReadOnlyList<TvShow> Shows);
```

- [ ] **Step 4: Persist generation-scoped rows**

`MongoTvShowDocument` stores `DocumentKind` with values `generation` or
`legacy`, nullable `GenerationId`/`TraktId` for legacy records, every field in
`TvShow`, including the separate canonical special-identity list, and migration
provenance. Use document IDs:

```text
generation:{generationId}:{traktId}
legacy:{legacyWatchlistItemId}
```

Use manifest ID `generation:{generationId}` and pointer ID `published-tv`.
`PublishAsync` first confirms the immutable manifest exists and matches the
supplied hashes/counts, then performs one atomic replace-upsert of the pointer.
It must never update a TV show row during pointer publication.

- [ ] **Step 5: Add required indexes during bootstrap**

Create these indexes idempotently:

```text
tv_shows: unique (documentKind, generationId, traktId)
tv_shows: (documentKind, tvdbId)
tv_shows: (documentKind, tmdbId)
tv_sync_manifests: unique generationId for manifest documents
tv_lifecycle_events: unique _id
tv_lifecycle_events: (generationId, traktId, lifecycleVersion)
```

`MongoTvIndexHostedService` owns only the new TV indexes and completes before
the migration/sync hosted services; do not overload the movie bootstrapper.
Do not create or seed a published TV pointer. Before the first successful Trakt
generation, TV browse returns an empty list rather than sample desired state.

- [ ] **Step 6: Implement coherent reads**

`MongoTvShowReadRepository.GetPublishedAsync` reads `published-tv` once, reads its
immutable manifest once, then filters `tv_shows` by `DocumentKind=generation`
and that exact generation ID. If pointer, manifest, or row counts disagree,
throw `TvPublishedGenerationInvalidException`; never fall back to all TV rows.

- [ ] **Step 7: Run repository and bootstrap regression tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~MongoTvGenerationRepositoryTests|FullyQualifiedName~MongoTvShowReadRepositoryTests|FullyQualifiedName~MongoTvIndexHostedServiceTests|FullyQualifiedName~SeedDataTests"
```

Expected: all selected tests pass and no TV seed creates a published pointer.

- [ ] **Step 8: Commit publish-last persistence**

```powershell
git add backend/src backend/tests/Watchlist.Application.Tests
git commit -m "feat: publish coherent TV generations"
```

### Task 8: Migrate Legacy TV Rows Without Granting Source Authority

**Files:**
- Create: `backend/src/Watchlist.Application/ILegacyTvMigrationService.cs`
- Create: `backend/src/Watchlist.Application/LegacyTvMigrationResult.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoLegacyTvMigrationService.cs`
- Create: `backend/src/Watchlist.Infrastructure/LegacyTvMigrationHostedService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistReadRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/SeedData.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Application/CombinedSyncService.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoLegacyTvMigrationServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/SeedDataTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoWatchlistReadRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/CombinedSyncServiceTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/TvSyncApiTests.cs`

- [ ] **Step 1: Write failing legacy-write cutover tests**

Before running any migration, prove protected `POST /api/sync/tmdb/tv` returns
`410` with `code=legacy_tv_sync_disabled` and never invokes
`ITmdbTvWatchlistSyncService`. Prove `POST /api/sync/all` does not call the old
TMDB-TV service and temporarily reports its existing TV stage as disabled;
Task 11 replaces that compatibility result with the Trakt TV result. No call
may create, update, or hard-delete a legacy TV row.

- [ ] **Step 2: Write failing idempotent migration tests**

Insert legacy `watchlist_items` TV rows covering:

```text
valid positive TMDB and TVDB IDs
TMDB-only identity
conflicting source ID and stored TMDB ID
non-numeric source ID
duplicate exact TVDB identity
movie row that must remain untouched
```

Assert one migration run creates `DocumentKind=legacy` rows in `tv_shows`, a
second run creates no duplicates, and no migration run creates a manifest,
published pointer, lifecycle event, disappearance counter, or cleanup field.
Valid TVDB IDs must survive migration; conflicting rows get
`IdentityStatus=LegacyUnresolved` with a stable redacted reason.

- [ ] **Step 3: Write failing read-isolation and seed tests**

Assert `MongoWatchlistReadRepository` returns only movies after the migration
boundary, and remove the two inconsistent TV samples from `SeedData.WatchlistItems`.
Movie seed IDs and expected movie counts remain unchanged.

- [ ] **Step 4: Run focused migration tests and verify failure**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~MongoLegacyTvMigrationServiceTests|FullyQualifiedName~SeedDataTests|FullyQualifiedName~MongoWatchlistReadRepositoryTests|FullyQualifiedName~CombinedSyncServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvSyncApiTests"
```

Expected: compile/assertion failure because the legacy migration does not
exist and the old read repository still returns TV rows.

- [ ] **Step 5: Disable every legacy TV write before migrating**

Map the protected route to the fixed `410` response and make the combined
orchestrator construct its compatibility disabled result without resolving or
calling `ITmdbTvWatchlistSyncService`. Keep the old types registered only as
needed for migration compatibility; they have no reachable mutation route.

- [ ] **Step 6: Implement one-way inert migration**

Copy legacy title, year, artwork, overview, exact IDs, original source ID,
added/updated timestamps, genres, language, and vote data into one
`legacy:{oldId}` TV document. Set no Trakt ID and no generation ID. A later
Trakt generation may adopt presentation/provenance only when an exact TVDB,
TMDB, or IMDb mapping resolves unambiguously; legacy membership never adds a
show to the tracked catalog.

The hosted migration runs after Mongo index bootstrap and before the scheduler
can publish its first generation. It logs only migrated/quarantined counts.

- [ ] **Step 7: Make `watchlist_items` movie-only for active reads**

Change `MongoWatchlistReadRepository` to add `MediaType == Movie` to its
existing Letterboxd lifecycle filter. The new `ITvShowReadRepository` is the only
TV read path. Do not delete old documents in Phase 1.

- [ ] **Step 8: Verify migration and movie regressions**

Run the focused command from Step 4, then:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~Letterboxd|FullyQualifiedName~MovieSync|FullyQualifiedName~MongoWatchlist"
```

Expected: migration tests and all selected movie lifecycle/read tests pass.

- [ ] **Step 9: Commit the legacy migration boundary**

```powershell
git add backend/src backend/tests/Watchlist.Application.Tests
git commit -m "feat: migrate legacy TV rows safely"
```

### Task 9: Orchestrate Complete TV Refreshes And Scheduled Publication

**Files:**
- Create: `backend/src/Watchlist.Application/ITvSyncService.cs`
- Create: `backend/src/Watchlist.Application/TvSyncResultDto.cs`
- Create: `backend/src/Watchlist.Application/ITraktOperationCoordinator.cs`
- Create: `backend/src/Watchlist.Application/TraktOperationCoordinator.cs`
- Create: `backend/src/Watchlist.Application/TvSyncService.cs`
- Create: `backend/src/Watchlist.Application/TvSyncSchedule.cs`
- Create: `backend/src/Watchlist.Infrastructure/TvSyncHostedService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvSyncServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvSyncScheduleTests.cs`

- [ ] **Step 1: Write failing source-union and publication tests**

Use fakes for Trakt, connection, enrichment, repository, and time. Cover:

```text
watchlist-only unstarted show is active
unfinished progress-only show remains active after Trakt auto-removal
completed progress-only show is retained caught_up
explicitly re-added completed show is active
previous row absent once remains visible
previous row absent in two scheduled full generations becomes source_removed
activity generation cannot advance absence confirmation
new aired episode reactivates caught_up show
full source failure performs zero stage/publish calls
TMDB provider failure publishes with unknown/stale provider state
missing scheduled row for detailed progress episode rejects the draft
duplicate schedule key for one detailed episode rejects the draft
season/episode join with conflicting schedule identity rejects the draft
complete season-0 schedule persists exact identity-only rows
malformed or partial season-0 schedule rejects the draft
empty complete season-0 schedule persists an empty identity list
activity cursor changed between pre/post reads performs zero stage/publish calls
pointer advances only after stage succeeds
```

Assert every successful result contains `MutationCapable=false`, both locked
health reasons, and no cleanup authorization.

- [ ] **Step 2: Write failing scheduling tests**

Test deterministic decisions:

```text
no published generation + connected account -> scheduled_full
last scheduled full at least one hour old     -> scheduled_full
relevant activity cursor changed              -> activity_full
no activity and full not due                  -> no refresh
disconnected/refresh_required/revoked          -> no refresh
```

When both activity and hourly time are due, choose `scheduled_full` so it can
advance source-removal confirmation.

- [ ] **Step 3: Run focused service tests and observe failure**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvSyncServiceTests|FullyQualifiedName~TvSyncScheduleTests"
```

Expected: build failure for `TvSyncService` and `TvSyncSchedule`.

- [ ] **Step 4: Implement the sync result and serialized coordinator**

```csharp
namespace Watchlist.Application;

public interface ITvSyncService
{
    Task<TvSyncResultDto> SyncAsync(
        TvGenerationKind kind,
        CancellationToken cancellationToken);
}

public sealed record TvSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string GenerationId,
    string Kind,
    int WatchlistItemsFetched,
    int ProgressItemsFetched,
    int ShowsPublished,
    int ProviderFailures,
    bool MutationCapable,
    IReadOnlyList<string> HealthReasons);
```

`ITraktOperationCoordinator` exposes an async exclusive lease.
`TraktOperationCoordinator` implements it with one singleton
`SemaphoreSlim(1, 1)`. Source refresh holds the lease from the first activity
read through pointer publication. Phase 2 history writes use the same
interface and singleton.

- [ ] **Step 5: Implement complete tracked-catalog assembly**

Within the coordinator lease:

1. obtain a valid access token;
2. read activity cursor before collection;
3. fetch every watchlist and watched-progress page;
4. union their Trakt IDs with every previously published row;
5. fetch detailed progress for current watchlist/progress rows;
6. refresh Trakt metadata when absent or older than one day;
7. fetch schedules for every numbered detailed-progress season and the next
   episode's season, and fetch
   `GET /shows/{traktId}/seasons/0?extended=full` once for every current
   source-union show;
8. index numbered schedule rows by exact `(seasonNumber, episodeNumber)`, require one
   positive-ID schedule row for every detailed-progress episode, reject
   missing/duplicate/conflicting joins, and only then construct
   `TvEpisodeProgress`; separately validate and canonicalize season-0 rows into
   `TvSpecialEpisodeIdentity` without adding them to progress;
9. enrich exact identity, artwork, and providers through TMDB;
10. reduce lifecycle for current and absent previous rows;
11. read the activity cursor again;
12. validate and hash the complete draft;
13. stage rows, events, and immutable manifest; and
14. publish the pointer last.

Generate IDs as `tv-{yyyyMMddHHmmssfff}-{32-lowercase-hex}` using a random
128-bit suffix. Sort all source rows before hashing and persistence.

Include the canonical season-0 identity list in the generation membership
hash and immutable manifest. A missing/partial/malformed season-0 response is
publication-critical; an explicit complete empty response is not. Add a
handoff test that publishes an S00E03 identity with exact Trakt/TVDB episode
IDs and proves the Phase 2 resolver can select it, while browse/detail/worker
progress projections remain unchanged and contain no season 0.

- [ ] **Step 6: Implement the five-minute scheduler**

`TvSyncHostedService` uses `PeriodicTimer(TraktOptions.ActivityPollInterval)`.
Each cycle reads connection status and the published manifest, then calls
`TvSyncSchedule.Decide`. Catch and log stable exception type/error code so the
hosted service survives upstream failure; never publish a synthetic failure
generation and never advance the activity cursor outside publication.

- [ ] **Step 7: Verify orchestration, lifecycle, and repository integration**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvSyncServiceTests|FullyQualifiedName~TvSyncScheduleTests|FullyQualifiedName~TvLifecycleEvaluatorTests|FullyQualifiedName~MongoTvGenerationRepositoryTests"
```

Expected: all selected tests pass, including zero publication on activity race
or source failure.

- [ ] **Step 8: Commit complete scheduled TV publication**

```powershell
git add backend/src backend/tests/Watchlist.Application.Tests
git commit -m "feat: publish scheduled TV generations"
```

### Task 10: Project Published TV Rows Into Browse And Detail DTOs

**Files:**
- Create: `backend/src/Watchlist.Application/TvBrowseState.cs`
- Create: `backend/src/Watchlist.Application/TvEpisodeProgressDto.cs`
- Create: `backend/src/Watchlist.Application/TvProviderOfferDto.cs`
- Create: `backend/src/Watchlist.Application/TvProviderAvailabilityDto.cs`
- Create: `backend/src/Watchlist.Application/TvBrowseDto.cs`
- Create: `backend/src/Watchlist.Application/TvSeasonProgressDto.cs`
- Create: `backend/src/Watchlist.Application/TvDestinationStatusDto.cs`
- Create: `backend/src/Watchlist.Application/TvDetailsDto.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistItemDto.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistItemDetailsDto.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistQuery.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistQueryService.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`
- Test: `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/TvBrowseApiTests.cs`

- [ ] **Step 1: Write failing query-state and projection tests**

Add service and API tests for:

```text
collection=tv with no state              -> active only
collection=tv&state=active               -> active only
collection=tv&state=caught_up            -> caught_up only
collection=tv&state=retired              -> retired_terminal only
collection=all                           -> movies plus active TV
state with collection=movie or all       -> 400 Invalid TV state.
state=source_removed or another value     -> 400 Invalid TV state.
TV detail ID not in published generation -> 404
legacy TV ID                              -> 404
```

Assert an active TV browse item has `source=trakt`,
`id=tv-trakt-12345`, `availabilityStatus=unknown_match`, and a non-null `tv`
object. Existing movie JSON must not gain a `tv` object.

- [ ] **Step 2: Run the focused query/API tests and verify failure**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~WatchlistQueryServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvBrowseApiTests"
```

Expected: compile failure because `WatchlistQuery` and the DTOs have no TV
state/read-model contract.

- [ ] **Step 3: Add the exact versioned TV DTO shape**

Use these records and API strings:

```csharp
namespace Watchlist.Application;

public enum TvBrowseState
{
    Active = 0,
    CaughtUp = 1,
    Retired = 2
}

public sealed record TvEpisodeProgressDto(
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    DateTimeOffset? AiredAt,
    bool Watched,
    DateTimeOffset? WatchedAt);

public sealed record TvProviderOfferDto(
    int ProviderId,
    string ProviderName,
    string Category,
    string? LogoUrl);

public sealed record TvProviderAvailabilityDto(
    string State,
    string Region,
    DateTimeOffset? FetchedAt,
    string? Link,
    IReadOnlyList<TvProviderOfferDto> Offers);
```

```csharp
namespace Watchlist.Application;

public sealed record TvBrowseDto(
    int ContractVersion,
    string LifecycleState,
    string? LastLifecycleEvent,
    string TraktStatus,
    bool InWatchlist,
    string IdentityStatus,
    int AiredEpisodes,
    int CompletedEpisodes,
    TvEpisodeProgressDto? NextEpisode,
    bool SeasonCleanupPending,
    string PlexAvailability,
    TvProviderAvailabilityDto Availability,
    int? RelevantSeasonNumber,
    TvProviderAvailabilityDto? RelevantSeasonAvailability);
```

```csharp
namespace Watchlist.Application;

public sealed record TvSeasonProgressDto(
    int SeasonNumber,
    int AiredEpisodes,
    int CompletedEpisodes,
    bool HasKnownFutureEpisode,
    string CleanupState,
    TvProviderAvailabilityDto Availability,
    IReadOnlyList<TvEpisodeProgressDto> Episodes);

public sealed record TvDestinationStatusDto(
    string SonarrState,
    string PlexWatchlistState,
    DateTimeOffset? ObservedAt);

public sealed record TvDetailsDto(
    int ContractVersion,
    string LifecycleState,
    string? LastLifecycleEvent,
    string TraktStatus,
    bool InWatchlist,
    string IdentityStatus,
    int AiredEpisodes,
    int CompletedEpisodes,
    TvEpisodeProgressDto? LastWatchedEpisode,
    TvEpisodeProgressDto? NextEpisode,
    TvProviderAvailabilityDto Availability,
    TvDestinationStatusDto Destinations,
    IReadOnlyList<TvSeasonProgressDto> Seasons);
```

Add a nullable init property to the existing positional records so all current
movie constructors remain source-compatible and omit it from movie JSON:

```csharp
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public TvBrowseDto? Tv { get; init; }
```

and:

```csharp
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public TvDetailsDto? Tv { get; init; }
```

- [ ] **Step 4: Add TV state to the query without changing movie semantics**

Change `WatchlistQuery` to include nullable `TvBrowseState`. Inject
`ITvShowReadRepository` into `WatchlistQueryService`. The service must:

- request legacy `IWatchlistReadRepository` rows only for movies;
- request one published TV generation for TV/all;
- apply active TV as the implicit all-collection state;
- map `retired` to `RetiredTerminal`;
- use `unknown_match` and `PlexAvailability=unknown` in Phase 1;
- use `LibraryMembership=watchlist` for the common compatibility field;
- expose configured flatrate provider names in
  `OwnedServiceAvailability` without changing movie provider matching; and
- choose relevant-season availability from the next episode's season when
  known, otherwise the latest aired numbered season, while keeping series and
  season offers separate; and
- map `Destinations` to `unknown/unknown/null` until worker-run summaries exist.

TV `ReleaseStatus` is `unreleased` only when aired is zero and the next episode
has a future air date; otherwise it is `released` when aired is positive and
`unknown` when neither fact is known.

- [ ] **Step 5: Parse and enforce the state query in the API**

Add `string? state` to `GET /api/watchlist`. Return `400` with
`{"error":"Invalid TV state."}` for every invalid combination. Preserve the
existing availability/sort error messages and order of validation.

Extend the TMDB-image URL rewrite helpers so artwork inside the common TV item
still uses backend-relative image URLs. Provider logo URLs are also rewritten
through `/api/images/tmdb/w500/{fileName}`; the TMDB provider link remains an
absolute URL supplied by TMDB.

- [ ] **Step 6: Verify browse/detail and all movie regressions**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~WatchlistQueryServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvBrowseApiTests|FullyQualifiedName~WatchlistApiTests"
```

Expected: TV state and detail tests pass; all existing movie browse/detail
tests still pass.

- [ ] **Step 7: Commit TV browse and detail contracts**

```powershell
git add backend/src backend/tests
git commit -m "feat: expose TV browse and detail state"
```

### Task 11: Expose Manual Sync, Read-Only Worker Export, Status, And Typed Failures

**Files:**
- Create: `backend/src/Watchlist.Application/WorkerTvPlexHistoryDto.cs`
- Create: `backend/src/Watchlist.Application/WorkerTvEpisodeDto.cs`
- Create: `backend/src/Watchlist.Application/WorkerTvSeasonDto.cs`
- Create: `backend/src/Watchlist.Application/WorkerTvShowDto.cs`
- Create: `backend/src/Watchlist.Application/WorkerTvCleanupAuthorizationDto.cs`
- Create: `backend/src/Watchlist.Application/WorkerTvSnapshotDto.cs`
- Create: `backend/src/Watchlist.Application/TvBlockerCodes.cs`
- Create: `backend/src/Watchlist.Application/ITvExportService.cs`
- Create: `backend/src/Watchlist.Application/TvExportService.cs`
- Create: `backend/src/Watchlist.Application/TvSyncStatusDto.cs`
- Create: `backend/src/Watchlist.Application/ITvStatusService.cs`
- Create: `backend/src/Watchlist.Application/TvStatusService.cs`
- Modify: `backend/src/Watchlist.Application/SyncStatusDto.cs`
- Modify: `backend/src/Watchlist.Application/CombinedSyncResultDto.cs`
- Modify: `backend/src/Watchlist.Application/CombinedSyncService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/MongoUnavailableExceptionHandler.cs`
- Modify: `backend/src/Watchlist.Api/TvEndpointRouteBuilderExtensions.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvExportServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvStatusServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/CombinedSyncServiceTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/TvSyncApiTests.cs`

- [ ] **Step 1: Write failing export and status tests**

Assert `GET /api/export/tv/sync-state` returns exactly one published generation
with:

```text
schemaVersion="1"
generationId and publishedAt
kind=scheduled_full or activity_full
mutationCapable=false
both locked healthReasons
plexHistory with capable=false, bootstrapComplete=false, and null configured
identity/collection/watermark fields
every tracked show, including source_removed
exact identities and identityStatus
inTraktWatchlist, lifecycleVersion, status, aired/completed, last/next episode
sonarrDesired, sonarrMonitoredDesired, and plexWatchlistDesired advisory fields
per-season monitoredDesired and exact aired-unwatched episode numbers
polandAvailability state/offers/freshness
show-specific blockers
cleanupAuthorizations=[]
```

Assert no export field contains credentials, Mongo IDs, protected tokens, or
media paths. `GET /api/sync/status` must retain current movie fields and add a
nullable `tv` object with connection state, last activity poll, last complete
generation, generation age, active/caught-up/source-removed counts, provider
error count, `mutationCapable=false`, and the locked health reasons.

- [ ] **Step 2: Write failing API and combined-sync tests**

Cover:

```text
POST /api/sync/tv without configured sync key -> existing local compatibility
POST /api/sync/tv with wrong/missing key       -> 401
POST /api/sync/tv with correct key             -> 200 and generation ID
Trakt not connected                            -> 409 code=trakt_not_connected
source snapshot rejected                       -> 502 code=tv_snapshot_rejected
Trakt unavailable                              -> 503 code=trakt_unavailable
TMDB malformed provider response               -> source publishes with provider unknown
POST /api/sync/tmdb/tv                         -> 410 code=legacy_tv_sync_disabled
POST /api/sync/all with TV failure             -> 200 status=partial with separate TV failure
GET /api/export/sonarr/tv                      -> empty compatibility array
```

- [ ] **Step 3: Run the focused tests and verify failure**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvExportServiceTests|FullyQualifiedName~TvStatusServiceTests|FullyQualifiedName~CombinedSyncServiceTests"
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvSyncApiTests"
```

Expected: compile/assertion failure for the missing TV export/status services.

- [ ] **Step 4: Add the exact read-only worker snapshot records**

```csharp
namespace Watchlist.Application;

public sealed record WorkerTvPlexHistoryDto(
    bool Capable,
    bool BootstrapComplete,
    string? MachineIdentifier,
    long? AccountId,
    string? LibrarySectionId,
    string? LibrarySectionTitle,
    DateTimeOffset? CollectedAt,
    DateTimeOffset? Watermark);

public sealed record WorkerTvEpisodeDto(
    long TraktEpisodeId,
    int SeasonNumber,
    int EpisodeNumber,
    int? TvdbId,
    string? Title,
    DateTimeOffset? FirstAired,
    bool Aired,
    bool Watched,
    DateTimeOffset? LastWatchedAt,
    string? PlexRatingKey,
    bool? WatchedByConfiguredPlexAccount,
    DateTimeOffset? PlexLastViewedAt);

public sealed record WorkerTvSeasonDto(
    int SeasonNumber,
    int Aired,
    int Completed,
    bool MonitoredDesired,
    IReadOnlyList<int> SearchAiredUnwatchedEpisodes,
    string CleanupState,
    IReadOnlyList<WorkerTvEpisodeDto> Episodes);

public sealed record WorkerTvShowDto(
    long TraktId,
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    string Title,
    int? Year,
    string IdentityStatus,
    bool InTraktWatchlist,
    string LifecycleState,
    long LifecycleVersion,
    string TraktStatus,
    int Aired,
    int Completed,
    WorkerTvEpisodeDto? LastWatchedEpisode,
    WorkerTvEpisodeDto? NextEpisode,
    bool SonarrDesired,
    bool SonarrMonitoredDesired,
    bool PlexWatchlistDesired,
    IReadOnlyList<WorkerTvSeasonDto> Seasons,
    TvProviderAvailabilityDto PolandAvailability,
    IReadOnlyList<string> Blockers);

public sealed record WorkerTvCleanupAuthorizationDto(
    string EventId,
    string ActionType,
    long TraktId,
    int TvdbId,
    int? SeasonNumber,
    long LifecycleVersion,
    string PredicateHash,
    string ManifestId,
    DateTimeOffset AuthorizedAt,
    DateTimeOffset ExpiresAt,
    int ExpectedAired,
    int ExpectedCompleted,
    DateTimeOffset PlexEvidenceCollectedAt,
    DateTimeOffset? PlexHistoryWatermark);

public sealed record WorkerTvSnapshotDto(
    string SchemaVersion,
    string GenerationId,
    DateTimeOffset PublishedAt,
    DateTimeOffset GeneratedAt,
    string Kind,
    bool MutationCapable,
    IReadOnlyList<string> HealthReasons,
    WorkerTvPlexHistoryDto PlexHistory,
    IReadOnlyList<WorkerTvShowDto> Shows,
    IReadOnlyList<WorkerTvCleanupAuthorizationDto> CleanupAuthorizations);
```

`TvExportService` rejects publication if any episode object lacks a positive
`TraktEpisodeId`; the ID is copied from the Trakt source snapshot through the
domain/Mongo projection and is never reconstructed from season/episode numbers.

Set `PlexWatchlistDesired` when the show is explicitly watchlisted or
`Completed < Aired`; set both Sonarr desired fields for active or caught-up
rows. Phase 1 emits an empty authorization list and the locked incapable Plex
history object, but it fixes the full future DTO now so Phase 2 and Phase 3 do
not rename fields. Add `phase_1_read_only` to every show blocker and add
`identity_missing_tvdb` or `identity_conflict` where applicable. Centralize
published strings in `TvBlockerCodes`; include the future v1 codes
`trakt_outbox_unresolved`, `plex_event_quarantined`,
`plex_history_unavailable`, `plex_backfill_incomplete`,
`plex_evidence_stale`, `explicit_trakt_watchlist`, and
`trakt_next_episode_known` so later phases extend behavior without renaming the
wire contract.

- [ ] **Step 5: Replace combined TMDB-TV sync with separately reported Trakt TV sync**

Change `CombinedSyncResultDto.TmdbTv` to `Tv` of type `TvSyncResultDto`. Preserve
the current movie stage ordering, then run TV as its own final stage. Catch
typed TV/Trakt connection failures inside `CombinedSyncService`, return a TV
result with `Status=failed`, empty generation ID, zero counts, and a stable
health reason, and set overall status `partial`. Do not mask or relabel movie
stage failures.

- [ ] **Step 6: Map typed source failures without leaking response bodies**

Add exception-handler mappings in this order:

```text
TraktNotConnectedException          -> 409 trakt_not_connected
TvSourceSnapshotRejectedException  -> 502 tv_snapshot_rejected
TraktParseException                -> 502 trakt_malformed_response
TmdbParseException                 -> 502 tmdb_malformed_response
TraktUnavailableException          -> 503 trakt_unavailable
TraktConnectionUnreadableException -> 503 trakt_connection_unreadable
```

Return `{ "code": "...", "error": "..." }` with fixed text. Never include
the upstream exception message for parse, auth, or transport failures.

- [ ] **Step 7: Map routes and preserve compatibility boundaries**

Map protected `POST /api/sync/tv`, update protected `POST /api/sync/all`, map
read-only `GET /api/export/tv/sync-state`, and extend `GET /api/sync/status`.
Retain Task 8's `410 Gone` legacy route and prove it still cannot call the old
service. Replace the combined sync's temporary disabled TV result with the
separately reported Trakt `TvSyncResultDto`; a TV failure makes the combined
result partial and cannot mask movie results. Keep `GET /api/export/sonarr/tv`
empty and add response header `X-Watchlist-Contract: compatibility-only`.

- [ ] **Step 8: Verify all export, status, API, and movie regressions**

Run the commands from Step 3, then:

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj
```

Expected: all API tests pass; TV failures are typed and redacted; the existing
movie API contract remains unchanged except for the additive nullable TV status.

- [ ] **Step 9: Commit Phase 1 backend HTTP contracts**

```powershell
git add backend/src backend/tests
git commit -m "feat: expose read-only TV API contracts"
```

### Task 12: Lock Backend And Android To Shared Versioned Fixtures

**Files:**
- Create: `contracts/tv/watchlist-browse-v1.json`
- Create: `contracts/tv/watchlist-detail-v1.json`
- Create: `contracts/tv/worker-sync-state-v1.json`
- Create: `contracts/tv/enums-v1.json`
- Create: `backend/tests/Watchlist.Api.Tests/TvAndroidContractTests.cs`
- Create: `backend/tests/Watchlist.Api.Tests/TvWorkerContractTests.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj`
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`

- [ ] **Step 1: Add the canonical browse fixture**

Commit this semantic shape with real JSON values and no comments:

```json
[
  {
    "id": "tv-trakt-12345",
    "mediaType": "tv",
    "source": "trakt",
    "sourceId": "12345",
    "title": "Example Show",
    "year": 2024,
    "overview": "An example TV show used by the shared contract test.",
    "posterUrl": "/api/images/tmdb/w500/example-poster.jpg",
    "backdropUrl": "/api/images/tmdb/w1280/example-backdrop.jpg",
    "releaseStatus": "released",
    "availabilityStatus": "unknown_match",
    "libraryMembership": "watchlist",
    "vodReleaseKnown": true,
    "releasedOnVod": true,
    "vodRegions": ["PL"],
    "ownedServiceAvailability": ["Prime Video"],
    "addedAt": "2026-07-01T12:00:00+00:00",
    "updatedAt": "2026-07-13T12:00:00+00:00",
    "tv": {
      "contractVersion": 1,
      "lifecycleState": "active",
      "lastLifecycleEvent": "reactivated",
      "traktStatus": "returning series",
      "inWatchlist": false,
      "identityStatus": "verified",
      "airedEpisodes": 12,
      "completedEpisodes": 11,
      "nextEpisode": {
        "seasonNumber": 2,
        "episodeNumber": 4,
        "title": "The Next Episode",
        "airedAt": "2026-07-16T19:00:00+00:00",
        "watched": false,
        "watchedAt": null
      },
      "seasonCleanupPending": false,
      "plexAvailability": "unknown",
      "availability": {
        "state": "available",
        "region": "PL",
        "fetchedAt": "2026-07-13T11:00:00+00:00",
        "link": "https://www.themoviedb.org/tv/999/watch",
        "offers": [
          {
            "providerId": 119,
            "providerName": "Prime Video",
            "category": "flatrate",
            "logoUrl": "/api/images/tmdb/w500/prime.jpg"
          }
        ]
      },
      "relevantSeasonNumber": 2,
      "relevantSeasonAvailability": {
        "state": "available",
        "region": "PL",
        "fetchedAt": "2026-07-13T11:00:00+00:00",
        "link": "https://www.themoviedb.org/tv/999/season/2/watch",
        "offers": []
      }
    }
  }
]
```

- [ ] **Step 2: Add detail, worker, and enum fixtures**

`watchlist-detail-v1.json` uses the same common fields and TV summary, then adds
`genres`, nullable movie metadata, the disabled primary action, and this `tv`
detail object:

```json
{
  "contractVersion": 1,
  "lifecycleState": "active",
  "lastLifecycleEvent": "reactivated",
  "traktStatus": "returning series",
  "inWatchlist": false,
  "identityStatus": "verified",
  "airedEpisodes": 12,
  "completedEpisodes": 11,
  "lastWatchedEpisode": {
    "seasonNumber": 2,
    "episodeNumber": 3,
    "title": "Previously",
    "airedAt": "2026-07-09T19:00:00+00:00",
    "watched": true,
    "watchedAt": "2026-07-10T20:30:00+00:00"
  },
  "nextEpisode": {
    "seasonNumber": 2,
    "episodeNumber": 4,
    "title": "The Next Episode",
    "airedAt": "2026-07-16T19:00:00+00:00",
    "watched": false,
    "watchedAt": null
  },
  "availability": {
    "state": "available",
    "region": "PL",
    "fetchedAt": "2026-07-13T11:00:00+00:00",
    "link": "https://www.themoviedb.org/tv/999/watch",
    "offers": []
  },
  "destinations": {
    "sonarrState": "unknown",
    "plexWatchlistState": "unknown",
    "observedAt": null
  },
  "seasons": [
    {
      "seasonNumber": 2,
      "airedEpisodes": 4,
      "completedEpisodes": 3,
      "hasKnownFutureEpisode": true,
      "cleanupState": "none",
      "availability": {
        "state": "unknown",
        "region": "PL",
        "fetchedAt": null,
        "link": null,
        "offers": []
      },
      "episodes": []
    }
  ]
}
```

`enums-v1.json` contains exactly:

```json
{
  "contractVersion": 1,
  "lifecycleStates": ["active", "caught_up", "source_removed", "terminal_cleanup_pending", "retired_terminal"],
  "lifecycleEvents": ["added", "caught_up", "reactivated", "source_removed", "season_candidate_started", "season_cleanup_authorized", "season_cleanup_completed", "terminal_candidate_started", "terminal_cleanup_authorized", "terminal_cleanup_completed", "cleanup_canceled", "destination_drift"],
  "identityStates": ["verified", "missing", "conflict", "legacy_unresolved"],
  "providerStates": ["available", "confirmed_unavailable", "unknown", "stale"],
  "providerCategories": ["flatrate", "free", "ads", "rent", "buy"],
  "destinationStates": ["unknown", "present", "absent", "monitored", "unmonitored"]
}
```

`worker-sync-state-v1.json` is a complete capable example serialized from the
Task 11 records. It uses `schemaVersion: "1"`, a string
`plexHistory.librarySectionId`, `librarySectionTitle`, timestamp-valued
`collectedAt` and `watermark`, full episode objects for last/next/season rows,
positive `traktEpisodeId` values on every episode object, the exact
`inTraktWatchlist`/`aired`/`completed`/desired-state vocabulary, one
active and one caught-up show, Poland availability, and both cleanup action
shapes. Runtime Phase 1 still emits `mutationCapable=false` and no
authorizations; constructing the capable DTO in a serialization test locks the
future wire shape without claiming Phase 2 capability.

- [ ] **Step 3: Write failing backend semantic fixture tests**

Configure `Watchlist.Api.Tests.csproj` to copy
`../../../contracts/tv/*.json` to `Contracts/Tv` in test output.
`TvAndroidContractTests` calls the seeded browse/detail endpoints.
`TvWorkerContractTests` constructs the exact capable `WorkerTvSnapshotDto`.
Both parse expected and actual with `JsonDocument` and compare semantic JSON
rather than whitespace.

It also recursively rejects property names matching this case-insensitive set:

```text
clientSecret accessToken refreshToken plexToken sonarrApiKey syncKey
connectionString protectedAccessToken protectedRefreshToken
```

- [ ] **Step 4: Run the backend contract test and make the seeded API match**

Run:

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvAndroidContractTests|FullyQualifiedName~TvWorkerContractTests"
```

Expected before seeded DTO updates: semantic JSON mismatch. Update only the
seeded TV repository/service values needed to produce the committed browse and
detail fixtures and the constructed worker DTO values, then rerun and expect
all contract tests to pass.

- [ ] **Step 5: Commit the shared contract authority**

```powershell
git add contracts/tv backend/tests/Watchlist.Api.Tests
git commit -m "test: lock TV client contract fixtures"
```

### Task 13: Parse The Shared TV Contract In Android And Remove Client Writes

**Files:**

- Create: `android/app/src/main/java/com/watchlist/tv/TvEpisodeProgress.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvProviderOffer.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvProviderAvailability.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvBrowseSummary.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvSeasonProgress.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvDestinationSummary.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvDetails.java`
- Create: `android/app/src/test/java/com/watchlist/tv/TvContractFixtureTest.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistItemDetails.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/BrowsingState.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`
- Modify: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`
- Modify: `android/app/src/test/java/com/watchlist/tv/WatchlistItemDetailsTest.java`
- Modify: `android/app/src/test/java/com/watchlist/tv/BrowsingStateTest.java`
- Modify: `android/app/build.gradle`
- Delete: `android/app/src/main/java/com/watchlist/tv/AvailabilityRefreshResult.java`

- [ ] **Step 1: Make the backend fixture directory a unit-test resource**

Add this source set inside the existing `android {}` block:

```groovy
sourceSets {
    test.resources.srcDir rootProject.file("../contracts")
}
```

`TvContractFixtureTest` must load `/tv/watchlist-browse-v1.json`,
`/tv/watchlist-detail-v1.json`, and `/tv/enums-v1.json` with the test class
loader. This makes the backend fixtures the sole payload authority; do not copy
their JSON into an Android-only fixture.

- [ ] **Step 2: Write failing parser, immutability, and URL tests**

Cover all of these assertions before changing production Java:

```text
the browse fixture parses tv.contractVersion, tv.lifecycleState,
tv.inWatchlist, aired/completed, next episode, seasonCleanupPending,
plexAvailability, and series/relevant-season provider state/offers/freshness
the detail fixture parses tv.contractVersion plus ordered seasons and episodes
without dropping nulls
all returned lists are unmodifiable defensive copies
unknown enum strings remain displayable raw values and do not crash parsing
TV active uses /api/watchlist?collection=tv&state=active&availability=plex,not_on_plex,unreleased,unknown_match&sort=added_desc
TV caught-up uses state=caught_up and alphabetical uses sort=title_asc
TV retired uses state=retired and the same availability set
movie and all requests do not send a state parameter
no Android model exposes accessToken, refreshToken, clientSecret, Plex token,
Sonarr key, cleanup authorizations, or mutationCapable as an editable property
```

Model lifecycle filters as constants on `BrowsingState`:

```java
public static final String TV_STATE_ACTIVE = "active";
public static final String TV_STATE_CAUGHT_UP = "caught_up";
public static final String TV_STATE_RETIRED = "retired";
```

`BrowsingState.defaults()` retains `TV_STATE_ACTIVE` even while media type is
`all`; `withMediaType`, `withSortMode`, and every existing copy method must
preserve it, and `withTvState` must preserve the other fields.

- [ ] **Step 3: Run the focused Android tests and observe contract failures**

Run from `android`:

```powershell
./gradlew.bat testDebugUnitTest --tests "com.watchlist.tv.TvContractFixtureTest" --tests "com.watchlist.tv.WatchlistApiClientTest" --tests "com.watchlist.tv.BrowsingStateTest"
```

Expected: compilation fails because the TV value objects and `withTvState` do
not exist, followed by parser/URL failures once those signatures are added.

- [ ] **Step 4: Add immutable TV value objects and attach them additively**

Use final Java classes implementing `Serializable`, with constructor
validation, final fields, accessors, and `List.copyOf`. Every nested type
attached to `WatchlistItem` or `WatchlistItemDetails` must also implement
`Serializable` because the existing activities pass items through an Intent.
Keep the JSON names and nullable behavior identical to the shared fixtures.
The browse and detail aggregates remain distinct:

```java
public final class TvBrowseSummary implements Serializable {
    private final int contractVersion;
    private final String lifecycleState;
    private final String lastLifecycleEvent;
    private final String traktStatus;
    private final boolean inWatchlist;
    private final String identityStatus;
    private final int airedEpisodes;
    private final int completedEpisodes;
    private final TvEpisodeProgress nextEpisode;
    private final boolean seasonCleanupPending;
    private final String plexAvailability;
    private final TvProviderAvailability availability;
    private final Integer relevantSeasonNumber;
    private final TvProviderAvailability relevantSeasonAvailability;
}

public final class TvDetails implements Serializable {
    private final int contractVersion;
    private final String lifecycleState;
    private final String lastLifecycleEvent;
    private final String traktStatus;
    private final boolean inWatchlist;
    private final String identityStatus;
    private final int airedEpisodes;
    private final int completedEpisodes;
    private final TvEpisodeProgress lastWatchedEpisode;
    private final TvEpisodeProgress nextEpisode;
    private final TvProviderAvailability availability;
    private final TvDestinationSummary destinations;
    private final List<TvSeasonProgress> seasons;
}
```

Add nullable `TvBrowseSummary tv()` to `WatchlistItem` and nullable
`TvDetails tv()` to `WatchlistItemDetails`. Existing movie constructors may
delegate to overloads that pass `null`, so all movie tests and payloads remain
source-compatible. Parse provider offers in server order and seasons/episodes
in numeric order as delivered; the client must not infer watched status or
provider availability.

- [ ] **Step 5: Make watchlist reads lifecycle-aware and remove every POST path**

Change the client signatures to:

```java
public List<WatchlistItem> getWatchlist(
        String collection,
        String tvState,
        String sortMode,
        boolean includeUnavailable) throws IOException, JSONException;

public static String buildWatchlistPath(
        String mediaType,
        String tvState,
        String sortMode,
        boolean includeUnavailable);
```

Append `state=` only for `collection=tv`, encode every query value with
`URLEncoder.encode(value, StandardCharsets.UTF_8)`, and retain the current
availability semantics. Delete `refreshAvailability`,
`buildAvailabilityRefreshPath`, `parseAvailabilityRefreshResult`, the private
`post` transport, and `AvailabilityRefreshResult.java`. Remove the startup
availability-refresh branch and its status text from `MainActivity`; startup
performs only the existing GET status/watchlist calls.

- [ ] **Step 6: Prove Android is read-only at the transport boundary**

Add a reflection/source assertion in `WatchlistApiClientTest` that the client
declares only GET-backed public network methods and that its compiled source
contains neither `setRequestMethod("POST")` nor `/api/sync/`. Rerun:

```powershell
./gradlew.bat testDebugUnitTest --tests "com.watchlist.tv.*"
```

Expected: all Android unit tests pass, including the unchanged movie parser
tests and the three shared-fixture tests.

- [ ] **Step 7: Commit the read-only Android contract**

```powershell
git add android contracts/tv
git commit -m "feat: parse TV read models on Android"
```

### Task 14: Present TV Lifecycle, Progress, And Polish Providers On Android TV

**Files:**

- Create: `android/app/src/main/java/com/watchlist/tv/TvPresentation.java`
- Create: `android/app/src/test/java/com/watchlist/tv/TvPresentationTest.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/DetailsActivity.java`
- Modify: `android/app/src/main/res/values/strings.xml`
- Modify: `android/app/src/test/java/com/watchlist/tv/BrowsingStateTest.java`
- Modify: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`

- [ ] **Step 1: Write failing presentation tests for every backend state**

Keep string construction outside activities in `TvPresentation`. Lock these
English labels exactly so null and unknown states stay honest:

```text
active -> Watching
caught_up -> Caught up
source_removed -> Removed from Trakt
terminal_cleanup_pending -> Cleanup pending
retired_terminal -> Finished
12 / 18 aired episodes watched -> 12/18 aired watched
next episode with air date -> Next: S02E05 · 20 Jul 2026
next episode without air date -> Next: S02E05 · Air date unknown
no next aired/future episode -> No announced next episode
available with flatrate offer -> Stream in Poland: {provider}
confirmed_unavailable -> Not available on configured Polish services
unknown -> Streaming availability unknown
stale -> Streaming availability may be outdated
plexWatchlistState unknown -> Plex watchlist: not checked in Phase 1
sonarrState unknown -> Sonarr: not checked in Phase 1
```

Also test that `rent` and `buy` are labeled separately from streaming, that a
provider link is only rendered when present, and that the UI never turns
`unknown` or `stale` into “not available.”

- [ ] **Step 2: Run the presentation tests and confirm the missing class**

```powershell
Set-Location android
./gradlew.bat testDebugUnitTest --tests "com.watchlist.tv.TvPresentationTest"
Set-Location ..
```

Expected: compilation fails because `TvPresentation` has not been created.

- [ ] **Step 3: Implement pure presentation helpers**

Give `TvPresentation` only static, deterministic methods that accept the parsed
models and `Locale`; it must not perform HTTP, parse JSON, read credentials, or
compute lifecycle transitions. Format calendar dates in `Europe/Warsaw` and
include the fixture's provider freshness time beneath the provider section.
Return a neutral “Data unavailable” line when the entire nullable TV block is
absent.

- [ ] **Step 4: Add lifecycle filters only to the TV browsing mode**

In the existing filter overlay, show a three-choice lifecycle row only when
`BrowsingState.MEDIA_TV` is selected:

```text
Watching | Caught up | Retired
```

Map the choices to `active`, `caught_up`, and `retired`. Selecting movies or
all hides the row but preserves the selected TV state for the next TV visit.
Changing the choice issues a new GET and restores focus using the existing
focused-item behavior. No lifecycle controls may call a sync endpoint.

- [ ] **Step 5: Add compact TV progress to browse cards**

For TV cards, render title/year followed by lifecycle label, aired progress,
next episode, and the provider summary. Keep movie cards unchanged. Missing
posters, absent progress, and long provider names must use the existing focus
and ellipsize behavior rather than changing grid width.

- [ ] **Step 6: Build a scrollable TV details section**

When `details.tv()` is non-null, `DetailsActivity` renders these sections in
order:

```text
Progress
Next episode
Seasons (season number, aired/watched counts, episode rows, air status)
Where to watch in Poland (stream/free/ads/rent/buy groups and freshness)
Destinations (explicit Phase 1 unknown labels)
Data source: Trakt; provider data: TMDB / JustWatch
```

Keep the existing movie details route and action behavior unchanged. Show the
required TMDB and JustWatch attribution beside the backend-provided TMDB
provider link; do not construct a service deep link. Phase 1 adds no browser
launch or write action. `cleanupState` is informational and must never become
a delete button.

- [ ] **Step 7: Run unit tests and compile the debug APK**

```powershell
Set-Location android
./gradlew.bat testDebugUnitTest
./gradlew.bat assembleDebug
Set-Location ..
```

Expected: both commands exit zero and produce
`android/app/build/outputs/apk/debug/app-debug.apk`.

- [ ] **Step 8: Perform the D-pad/read-only acceptance check**

Against a seeded backend, verify on an Android TV emulator or device:

1. Focus travels into and out of the lifecycle filter without a trap.
2. TV state changes refresh the grid and restore focus to a surviving item.
3. A TV card opens the scrollable detail page and Back returns to the same
   card.
4. `unknown`, `stale`, and `confirmed_unavailable` appear as three distinct
   messages.
5. No screen offers “sync,” “mark watched,” “delete,” or destination controls.
6. Backend access logs contain only Android `GET /api/watchlist`,
   `GET /api/watchlist/{id}`, and `GET /api/sync/status` requests.

Expected: all six checks pass with no focus trap, clipped status text, crash,
or non-GET Android request.

- [ ] **Step 9: Commit the Android TV presentation**

```powershell
git add android
git commit -m "feat: present TV progress on Android"
```

### Task 15: Mount The Keyring, Lock Mutation Gates, And Extend CI

**Files:**

- Create: `tests/deployment/test_tv_phase1_deployment.py`
- Modify: `backend/src/Watchlist.Api/appsettings.json`
- Modify: `backend/src/Watchlist.Api/appsettings.Development.Local.example.json`
- Modify: `backend/src/Watchlist.Api/Dockerfile`
- Modify: `deploy/backend/compose.yaml`
- Modify: `deploy/backend/watchlist-backend.env.example`
- Modify: `deploy/production/compose.yaml`
- Modify: `deploy/production/backend.env.example`
- Modify: `deploy/production/worker.env.example`
- Modify: `deploy/local-cd/watchlist-deploy.env.example`
- Modify: `scripts/deploy-movie-sync.sh`
- Modify: `tests/deployment/test_deploy_script.py`
- Modify: `.github/workflows/android-ci.yml`
- Modify: `.github/workflows/movie-ci.yml`

- [ ] **Step 1: Write failing deployment invariants before editing Compose**

`tests/deployment/test_tv_phase1_deployment.py` parses both backend/production
Compose files, example environments, Dockerfile, production deploy script, and CI
YAML. Assert:

```text
production backend has a persistent bind-mounted /var/lib/watchlist/keyring
standalone backend has a Docker-managed named keyring volume
DataProtection__KeyRingPath is /var/lib/watchlist/keyring in deployed backends
backend runs with WATCHLIST_RUNTIME_UID:WATCHLIST_RUNTIME_GID in production
Trakt client id is configurable; Trakt client secret has no committed value
TMDB provider region defaults to PL and provider cache is 24 hours
Mongo TV collection names are explicit and distinct
TRAKT_HISTORY_SYNC_APPLY=false
TV_SYNC_APPLY=false
TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false
TV_SYNC_ALLOW_SEASON_FILE_DELETION=false
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false
worker Compose receives the same six false values
deploy script creates the host keyring directory before docker compose up
Android CI includes contracts/tv and backend DTO path triggers
movie CI runs backend, worker, deployment, and Android contract tests
no example file contains a token, API key, sync key, or usable client secret
```

- [ ] **Step 2: Run the deployment test and observe missing keyring/gates**

```powershell
python -m pytest tests/deployment/test_tv_phase1_deployment.py -q
```

Expected: assertions fail for the absent keyring mount, TV environment keys,
and CI contract jobs.

- [ ] **Step 3: Add non-secret application defaults**

Add these sections to `appsettings.json`; local examples may override only
values needed for development:

```json
{
  "Trakt": {
    "BaseUrl": "https://api.trakt.tv",
    "ClientId": "",
    "ClientSecret": "",
    "RedirectUri": "urn:ietf:wg:oauth:2.0:oob",
    "ActivityPollInterval": "00:05:00",
    "FullSyncInterval": "01:00:00",
    "TokenRefreshSkew": "00:05:00"
  },
  "DataProtection": {
    "KeyRingPath": "/var/lib/watchlist/keyring",
    "ApplicationName": "watchlist-api"
  },
  "Tmdb": {
    "ProviderRegion": "PL",
    "OwnedProviderIds": [119, 1899, 1773],
    "ProviderCacheLifetime": "1.00:00:00"
  }
}
```

Add explicit `MongoDb` names for `TraktConnectionsCollectionName`,
`TvShowsCollectionName`, `TvLifecycleEventsCollectionName`, and
`TvSyncManifestsCollectionName`. Keep the existing movie collections
unchanged; manifests and the singleton published pointer share the manifest
collection using distinct document IDs. The
local-development keyring path is
`../../../../.artifacts/data-protection-keys`; it remains gitignored.

- [ ] **Step 4: Persist the keyring and make permissions deterministic**

In production mount the deployer-managed host directory:

```yaml
- ${WATCHLIST_DATA_DIR:-./data}/backend/data-protection-keys:/var/lib/watchlist/keyring
```

In `deploy/backend/compose.yaml`, use a Docker-managed volume so direct local
Compose startup does not depend on a host chown helper:

```yaml
services:
  watchlist-api:
    volumes:
      - watchlist_backend_keyring:/var/lib/watchlist/keyring

volumes:
  watchlist_backend_keyring:
```

Parameterize the standalone backend Compose env file as
`${WATCHLIST_BACKEND_ENV_FILE:-./watchlist-backend.env}` so validation and local
operators can provide an existing file without writing into the repository.

Production uses:

```yaml
user: "${WATCHLIST_RUNTIME_UID:-10001}:${WATCHLIST_RUNTIME_GID:-10001}"
```

The Dockerfile runs
`install -d -o app -g app -m 0700 /var/lib/watchlist/keyring` in the final
image before switching to `USER app`; this ownership seeds a newly created
standalone named volume. `scripts/deploy-movie-sync.sh` creates
`$DATA_DIR/backend/data-protection-keys`, assigns the configured runtime
UID/GID, applies mode `0700`, preserves it through cutover/rollback, and then
runs Compose. It must not print Trakt credentials or protected tokens.

- [ ] **Step 5: Declare secrets and all six false gates in examples/Compose**

Use these exact environment names:

```dotenv
Trakt__ClientId=
Trakt__ClientSecret=
DataProtection__KeyRingPath=/var/lib/watchlist/keyring
Tmdb__ProviderRegion=PL
Tmdb__OwnedProviderIds__0=119
Tmdb__OwnedProviderIds__1=1899
Tmdb__OwnedProviderIds__2=1773
Tmdb__ProviderCacheLifetime=1.00:00:00
TRAKT_HISTORY_SYNC_APPLY=false
TV_SYNC_APPLY=false
TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false
TV_SYNC_ALLOW_SEASON_FILE_DELETION=false
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false
```

The backend option values are passed only to the backend. The six gates
are present and false in both backend and worker environments so an image from
a later phase cannot mutate TV destinations when deployed with Phase 1
configuration. Do not introduce Plex or Sonarr credentials into Android or
the TV export response.

- [ ] **Step 6: Extend CI around the shared contract and gate invariants**

In `android-ci.yml`, include `contracts/tv/**`, the additive backend TV DTOs,
and Android sources in path filters, then run `testDebugUnitTest` and
`assembleDebug`. In `movie-ci.yml`, add named steps for:

```powershell
dotnet test backend/Watchlist.sln --configuration Release --no-restore
cd workers/vod-filter && python -m pytest -q
python -m pytest tests/deployment/test_tv_phase1_deployment.py -q
cd android && ./gradlew testDebugUnitTest
```

Use the workflow's native Bash equivalents where appropriate. Preserve the
existing secret scan and Docker build jobs.

- [ ] **Step 7: Validate configuration without production credentials**

```powershell
python -m pytest tests/deployment/test_tv_phase1_deployment.py -q
$validationRoot = Join-Path $env:TEMP "watchlist-tv-phase1-compose"
$configDir = Join-Path $validationRoot "config"
$dataDir = Join-Path $validationRoot "data"
New-Item -ItemType Directory -Force $configDir, "$dataDir/backend/data-protection-keys", "$dataDir/worker" | Out-Null
Copy-Item deploy/production/backend.env.example "$configDir/backend.env"
Copy-Item deploy/production/worker.env.example "$configDir/worker.env"
$env:WATCHLIST_BACKEND_ENV_FILE = (Resolve-Path deploy/backend/watchlist-backend.env.example).Path
$env:WATCHLIST_CONFIG_DIR = $configDir
$env:WATCHLIST_DATA_DIR = $dataDir
$env:WATCHLIST_RUNTIME_UID = "10001"
$env:WATCHLIST_RUNTIME_GID = "10001"
$env:WATCHLIST_RELEASE = "tv-phase1-validation"
docker compose -f compose.yaml config --quiet
docker compose -f deploy/backend/compose.yaml config --quiet
docker compose -f deploy/production/compose.yaml config --quiet
Remove-Item Env:WATCHLIST_BACKEND_ENV_FILE, Env:WATCHLIST_CONFIG_DIR, Env:WATCHLIST_DATA_DIR, Env:WATCHLIST_RUNTIME_UID, Env:WATCHLIST_RUNTIME_GID, Env:WATCHLIST_RELEASE
Remove-Item -Recurse -Force $validationRoot
```

Expected: the deployment test passes and all three Compose commands exit zero
without starting containers. Empty Trakt credentials are permitted at config
render time; the integration status remains disconnected until configured.

- [ ] **Step 8: Commit the secret-safe deployment boundary**

```powershell
git add backend/src/Watchlist.Api/appsettings.json backend/src/Watchlist.Api/appsettings.Development.Local.example.json backend/src/Watchlist.Api/Dockerfile deploy scripts/deploy-movie-sync.sh tests/deployment .github/workflows
git commit -m "ops: deploy TV read model safely"
```

### Task 16: Make The Phase 1 Operational Contract Discoverable In OKF

**Files:**

- Create: `docs/architecture/tv_sync_read_model.md`
- Create: `docs/integrations/trakt.md`
- Create: `docs/data_models/tv_show.md`
- Create: `docs/runbooks/tv_sync_operations.md`
- Create: `docs/reports/tv_integration_rollout.md`
- Modify: `docs/superpowers/specs/2026-07-13-tv-show-integration-design.md`
- Modify: `docs/index.md`
- Modify: `docs/projects/watchlist_app.md`
- Modify: `docs/architecture/index.md`
- Modify: `docs/architecture/system_boundaries.md`
- Modify: `docs/architecture/sync_pipeline.md`
- Modify: `docs/apis/index.md`
- Modify: `docs/apis/backend_api.md`
- Modify: `docs/apis/export_endpoints.md`
- Modify: `docs/integrations/index.md`
- Modify: `docs/integrations/mongodb.md`
- Modify: `docs/integrations/tmdb.md`
- Modify: `docs/integrations/plex.md`
- Modify: `docs/data_models/index.md`
- Modify: `docs/data_models/availability_states.md`
- Modify: `docs/data_models/sync_run.md`
- Modify: `docs/systems/index.md`
- Modify: `docs/systems/backend_service.md`
- Modify: `docs/systems/android_tv_client.md`
- Modify: `docs/systems/deployment_tooling.md`
- Modify: `docs/systems/vod_filter_worker.md`
- Modify: `docs/decisions/index.md`
- Modify: `docs/decisions/android_tv_read_only_v1.md`
- Modify: `docs/decisions/backend_owns_integrations.md`
- Modify: `docs/decisions/sync_correctness_priority.md`
- Modify: `docs/runbooks/index.md`
- Modify: `docs/runbooks/agent_onboarding.md`
- Modify: `docs/runbooks/local_development.md`
- Modify: `docs/runbooks/validation.md`
- Modify: `docs/runbooks/homelab_cd.md`
- Modify: `docs/runbooks/vod_filter_operations.md`
- Modify: `docs/backlog/index.md`
- Modify: `docs/backlog/roadmap.md`
- Modify: `docs/reports/index.md`
- Modify: `docs/log.md`

- [ ] **Step 1: Write the five canonical concept documents**

Every new document gets valid OKF frontmatter, a stable title, and links to
the approved design rather than duplicating its full rationale. Record these
exact ownership boundaries:

```text
Trakt: TV watchlist, watched progress, episode/season/show status
TMDB: exact-ID metadata and PL watch-provider observations
MongoDB: encrypted connection plus immutable generation/read lifecycle state
backend: OAuth, synchronization, lifecycle reduction, publishing, read API
Android: unauthenticated-integration read client only; no third-party secrets
worker: movie behavior only in Phase 1; every TV apply/adoption/delete gate false
Plex/Sonarr: no Phase 1 observations or mutations
```

`tv_show.md` documents identity fields, lifecycle/event enums, watched/aired
progress, next-episode semantics, provider states/categories, generation
membership, and the difference between `source_removed` and terminal status.
`trakt.md` documents device OAuth, protected storage, refresh, pagination,
`last_activities`, full-sync cursor race protection, and rate-limit behavior.
`tv_sync_operations.md` gives commands to inspect connection state, trigger a
read sync, inspect the published generation, distinguish unknown/stale data,
rotate the keyring safely, and recover from an unreadable connection without
deleting the old keyring.

- [ ] **Step 2: Document the actual API and rollout evidence**

Update the API docs with exact Phase 1 query values, DTO fields, response
codes, auth requirement for integration/manual-sync routes, read-only export,
and `410 Gone` for the replaced TMDB-TV sync route. State explicitly that
`collection=all` contains active TV only and lifecycle filtering applies only
to `collection=tv`.

`tv_integration_rollout.md` is the sole cumulative integration evidence ledger.
Phase 1 creates it with a Phase 1 section containing concrete rows for:

```text
device connection established without logging tokens
first complete generation published
cursor-race rejection left the old pointer unchanged
source failure left the old pointer unchanged
provider failure published unknown rather than unavailable
legacy row quarantined or migrated deterministically
Android shared fixtures and read-only transport passed
all six TV mutation gates observed false
```

Each row names the command/API call and artifact to capture; it does not claim
production rollout has occurred during implementation.

- [ ] **Step 3: Connect indexes, boundaries, decisions, and backlog**

Add links from every listed index and update the project/architecture/system
pages so agents reach the TV read-model docs from `docs/index.md`. Preserve
the existing movie authority. The three decision pages receive additive Phase
1 consequences: Trakt credentials remain server-side, publish-last generations
are required for correctness, and Android never owns TV sync.

Move only Phase 1 read-model items to completed in the roadmap after their
tests pass. Keep Plex history ingestion, Trakt history writes, Sonarr/Plex
adoption, season deletion, and terminal show deletion in the later-phase
backlog with links to their separate plans. Do not describe those mutations as
available, partially enabled, or manually runnable in Phase 1.

- [ ] **Step 4: Run OKF and link validation**

```powershell
python tests/validate_okf.py
rg -n "Phase 1|Trakt|tv_sync_read_model|tv_show|tv_sync_operations" docs/index.md docs/projects docs/architecture docs/apis docs/integrations docs/data_models docs/systems docs/decisions docs/runbooks docs/backlog docs/reports
```

Expected: the OKF validator exits zero; the search shows the new concepts from
their section indexes and the root knowledge index. Resolve every missing
frontmatter key, broken relative link, orphaned concept, and contradictory
Phase 1 mutation claim before committing.

- [ ] **Step 5: Commit the knowledge layer**

```powershell
git add docs
git commit -m "docs: document TV Phase 1 operations"
```

### Task 17: Run The Phase 1 Release Gate And Record Evidence

**Files:**

- Modify: `docs/reports/tv_integration_rollout.md`
- Modify: `docs/log.md`

- [ ] **Step 1: Start from a clean disposable Mongo database**

Use a database name dedicated to this validation and a persistent disposable
keyring under `.artifacts`:

```powershell
New-Item -ItemType Directory -Force .artifacts/tv-phase1-keyring | Out-Null
$env:MongoDb__DatabaseName = "watchlist_tv_phase1_validation"
$env:DataProtection__KeyRingPath = (Resolve-Path .artifacts/tv-phase1-keyring).Path
$env:TRAKT_HISTORY_SYNC_APPLY = "false"
$env:TV_SYNC_APPLY = "false"
$env:TV_SYNC_ADOPT_EXISTING_DESTINATIONS = "false"
$env:TV_SYNC_ALLOW_SEASON_FILE_DELETION = "false"
$env:TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION = "false"
$env:TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE = "false"
```

Drop only `watchlist_tv_phase1_validation` through the Mongo test fixture or
Mongo shell. Never point this validation at the configured production
database and never delete the keyring used by a deployed backend.

- [ ] **Step 2: Run backend restore, build, and all tests**

```powershell
dotnet restore backend/Watchlist.sln
dotnet build backend/Watchlist.sln --configuration Release --no-restore
dotnet test backend/Watchlist.sln --configuration Release --no-build
```

Expected: all three commands exit zero. The test output includes Trakt OAuth,
pagination, lifecycle evaluator, cursor race, provider semantics, generation
publication, legacy migration, API contract, and existing movie suites.

- [ ] **Step 3: Prove the unchanged worker remains green and mutation-disabled**

```powershell
Push-Location workers/vod-filter
python -m pytest -q
python -m compileall -q src continuous_sync.py sync_movies.py reconcile_sync.py healthcheck.py
Pop-Location
rg -n "TRAKT_HISTORY_SYNC_APPLY|TV_SYNC_APPLY|TV_SYNC_ADOPT_EXISTING_DESTINATIONS|TV_SYNC_ALLOW_SEASON_FILE_DELETION|TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION|TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE" compose.yaml deploy workers/vod-filter
```

Expected: pytest and compileall exit zero. Every environment assignment shown
by the search is `false`; there is no Phase 1 Python code that calls Sonarr,
Plex watchlist, or Trakt history for TV.

- [ ] **Step 4: Run Android contract, unit, and APK gates**

```powershell
Set-Location android
./gradlew.bat clean testDebugUnitTest assembleDebug
Set-Location ..
```

Expected: the command exits zero, shared backend fixtures are parsed, and the
debug APK exists at `android/app/build/outputs/apk/debug/app-debug.apk`.

- [ ] **Step 5: Run deployment, Compose, image, and shell checks**

```powershell
python -m pytest tests/deployment/test_tv_phase1_deployment.py -q
$validationRoot = Join-Path $env:TEMP "watchlist-tv-phase1-release"
$configDir = Join-Path $validationRoot "config"
$dataDir = Join-Path $validationRoot "data"
New-Item -ItemType Directory -Force $configDir, "$dataDir/backend/data-protection-keys", "$dataDir/worker" | Out-Null
Copy-Item deploy/production/backend.env.example "$configDir/backend.env"
Copy-Item deploy/production/worker.env.example "$configDir/worker.env"
$env:WATCHLIST_BACKEND_ENV_FILE = (Resolve-Path deploy/backend/watchlist-backend.env.example).Path
$env:WATCHLIST_CONFIG_DIR = $configDir
$env:WATCHLIST_DATA_DIR = $dataDir
$env:WATCHLIST_RUNTIME_UID = "10001"
$env:WATCHLIST_RUNTIME_GID = "10001"
$env:WATCHLIST_RELEASE = "tv-phase1-release"
docker compose -f compose.yaml config --quiet
docker compose -f deploy/backend/compose.yaml config --quiet
docker compose -f deploy/production/compose.yaml config --quiet
docker build -f backend/src/Watchlist.Api/Dockerfile -t watchlist-api:tv-phase1 .
docker build -f workers/vod-filter/Dockerfile -t watchlist-worker:tv-phase1 workers/vod-filter
bash -n scripts/deploy-movie-sync.sh
Remove-Item Env:WATCHLIST_BACKEND_ENV_FILE, Env:WATCHLIST_CONFIG_DIR, Env:WATCHLIST_DATA_DIR, Env:WATCHLIST_RUNTIME_UID, Env:WATCHLIST_RUNTIME_GID, Env:WATCHLIST_RELEASE
Remove-Item -Recurse -Force $validationRoot
```

Expected: every command exits zero; image builds do not require real Trakt
credentials and no container is started by the config checks.

- [ ] **Step 6: Run secret, write-surface, and scope scans**

```powershell
gitleaks detect --source . --no-banner --redact
rg -n -i "clientSecret|accessToken|refreshToken|protectedAccessToken|protectedRefreshToken|plexToken|sonarrApiKey" contracts android docs/reports
rg -n "setRequestMethod\(\"POST\"\)|/api/sync/" android/app/src/main
rg -n "mutationCapable\s*[:=]\s*true|TV_.*_APPLY\s*=\s*true|ADOPT_EXISTING_DESTINATIONS\s*=\s*true" backend android compose.yaml deploy workers
```

Expected: gitleaks exits zero. The sensitive-name scan finds only explicitly
redacted documentation/test assertions and no values; the Android POST/sync
scan and true-gate scan return no matches.

- [ ] **Step 7: Exercise the publish-last failure matrix**

Run the integration test fixture with its Trakt/TMDB handlers configured for
these four sequences and record the published generation ID before and after:

```text
complete Trakt + complete TMDB -> new generation published
complete Trakt + TMDB provider failure -> new generation published with unknown providers
Trakt page failure -> no generation published
pre/post last_activities cursor change -> no generation published
```

Then rerun the successful sequence with one source item absent twice on two
separate hourly full-sync timestamps. Expected: first absence keeps the prior
lifecycle; second absence emits one stable `source_removed` event and publishes
the new generation. Activity-poll retries do not increment the absence count.

- [ ] **Step 8: Exercise connection/keyring recovery without exposing tokens**

Use the fake Trakt server from the API integration suite to complete device
authorization, restart the backend against the same keyring, and confirm the
connection remains usable. Point a second backend instance at a new empty
keyring and confirm status reports `token_unreadable`, sync is rejected, the
encrypted Mongo fields remain unchanged, and logs contain neither token.

Expected: restoring the original keyring restores the connection with no
re-authorization and no Mongo document edit.

- [ ] **Step 9: Update rollout evidence and final knowledge validation**

Record command timestamps, commit SHA, generation IDs, redacted log/artifact
paths, and pass/fail outcomes in the Phase 1 section of
`tv_integration_rollout.md`. Update `docs/log.md`
with the Phase 1 completion entry only after every preceding gate passes.

```powershell
python tests/validate_okf.py
git diff --check
git status --short
```

Expected: OKF validation and whitespace checks exit zero. Status contains only
intentional Phase 1 changes and no `.artifacts`, keyring files, tokens, local
environment files, database dumps, or generated APKs.

- [ ] **Step 10: Commit the verified rollout record**

```powershell
git add docs/reports/tv_integration_rollout.md docs/log.md
git commit -m "docs: record TV Phase 1 validation"
```

Phase 1 is complete only when the backend publishes coherent Trakt-derived TV
read generations, provider failures remain explicitly unknown, Android is
read-only, secrets survive restart through the mounted keyring, and all six TV
mutation gates are false. Plex history ingestion, Trakt watched writes,
Sonarr/Plex adoption, season deletion, and terminal show deletion remain for
their approved later-phase plans.
