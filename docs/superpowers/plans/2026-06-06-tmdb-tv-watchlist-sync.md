# TMDB TV Watchlist Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import the user's TMDB account TV watchlist into MongoDB so existing Android TV `TV Shows` and `All` collections show real TV records.

**Architecture:** Add a backend-only TMDB TV sync path that mirrors existing layering: application interfaces and orchestration, infrastructure TMDB/Mongo adapters, minimal API endpoint, and deterministic tests. TV watchlist and TV metadata both come from TMDB, so the sync imports and enriches TV shows in one operation while preserving future Plex TV availability fields.

**Tech Stack:** .NET minimal API, C# records, HttpClient, MongoDB.Driver, xUnit, FluentAssertions, ASP.NET Core WebApplicationFactory.

---

## File Structure

- Create `backend/src/Watchlist.Application/ITmdbTvWatchlistClient.cs`: source client boundary for paginated TMDB account TV watchlist reads.
- Create `backend/src/Watchlist.Application/ITmdbTvMetadataClient.cs`: metadata client boundary for TV details and external IDs.
- Create `backend/src/Watchlist.Application/ITmdbTvWatchlistSyncService.cs`: application service boundary.
- Create `backend/src/Watchlist.Application/TmdbTvWatchlistItemDto.cs`: raw normalized TV watchlist source DTO.
- Create `backend/src/Watchlist.Application/TmdbTvMetadataDto.cs`: enriched TV metadata DTO.
- Create `backend/src/Watchlist.Application/TmdbTvExternalIdsDto.cs`: external IDs DTO for later Plex TV matching.
- Create `backend/src/Watchlist.Application/TmdbTvSyncResultDto.cs`: endpoint result DTO.
- Modify `backend/src/Watchlist.Application/TmdbExceptions.cs`: add TV-specific not-found exception.
- Modify `backend/src/Watchlist.Application/IWatchlistWriteRepository.cs`: add TMDB TV sync persistence method.
- Create `backend/src/Watchlist.Application/TmdbTvWatchlistSyncService.cs`: import/enrichment orchestration.
- Modify `backend/src/Watchlist.Application/CombinedSyncResultDto.cs`: add TV sync result.
- Modify `backend/src/Watchlist.Application/CombinedSyncService.cs`: run TV sync before Plex movie sync.
- Modify `backend/src/Watchlist.Infrastructure/TmdbOptions.cs`: add `AccountId`, `SessionId`, and `Language`.
- Create `backend/src/Watchlist.Infrastructure/TmdbTvWatchlistClient.cs`: fetch all account watchlist pages.
- Create `backend/src/Watchlist.Infrastructure/TmdbTvMetadataClient.cs`: fetch TV details and external IDs.
- Modify `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`: add `TvdbId` field and factory support.
- Modify `backend/src/Watchlist.Infrastructure/MongoWatchlistWriteRepository.cs`: add TMDB TV upsert/delete method.
- Modify `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`: register TV clients and sync service.
- Modify `backend/src/Watchlist.Api/Program.cs`: map `POST /api/sync/tmdb/tv`.
- Modify API test factories and tests under `backend/tests/Watchlist.Api.Tests`.
- Add focused application/infrastructure tests under `backend/tests/Watchlist.Application.Tests`.
- Modify `docs/api.md`, `docs/integrations.md`, and `docs/architecture.md`.

Before implementation, run:

```powershell
git status --short --branch
```

Preserve unrelated local changes. Do not edit ignored local credential files except for manual smoke testing.

### Task 1: Application Contracts And Result DTOs

**Files:**
- Create: `backend/src/Watchlist.Application/ITmdbTvWatchlistClient.cs`
- Create: `backend/src/Watchlist.Application/ITmdbTvMetadataClient.cs`
- Create: `backend/src/Watchlist.Application/ITmdbTvWatchlistSyncService.cs`
- Create: `backend/src/Watchlist.Application/TmdbTvWatchlistItemDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbTvMetadataDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbTvExternalIdsDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbTvSyncResultDto.cs`
- Modify: `backend/src/Watchlist.Application/TmdbExceptions.cs`

- [ ] **Step 1: Add TV application DTOs and interfaces**

Create `backend/src/Watchlist.Application/TmdbTvWatchlistItemDto.cs`:

```csharp
namespace Watchlist.Application;

public sealed record TmdbTvWatchlistItemDto(
    int TmdbId,
    string Name,
    string OriginalName,
    string? Overview,
    string? FirstAirDate,
    string? PosterPath,
    string? BackdropPath,
    string? OriginalLanguage,
    double? TmdbVoteAverage,
    int? TmdbVoteCount);
```

Create `backend/src/Watchlist.Application/TmdbTvExternalIdsDto.cs`:

```csharp
namespace Watchlist.Application;

public sealed record TmdbTvExternalIdsDto(
    string? ImdbId,
    int? TvdbId);
```

Create `backend/src/Watchlist.Application/TmdbTvMetadataDto.cs`:

```csharp
namespace Watchlist.Application;

public sealed record TmdbTvMetadataDto(
    int TmdbId,
    string Name,
    string OriginalName,
    string? Overview,
    string? FirstAirDate,
    string? Status,
    string? PosterPath,
    string? BackdropPath,
    string? PosterUrl,
    string? BackdropUrl,
    IReadOnlyList<string> Genres,
    string? OriginalLanguage,
    double? TmdbVoteAverage,
    int? TmdbVoteCount,
    TmdbTvExternalIdsDto ExternalIds);
```

Create `backend/src/Watchlist.Application/TmdbTvSyncResultDto.cs`:

```csharp
namespace Watchlist.Application;

public sealed record TmdbTvSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ItemsFetched,
    int ItemsUpserted,
    int ItemsDeleted,
    int ItemsEnriched,
    int ItemsNotFound,
    int ItemsFailed);
```

Create `backend/src/Watchlist.Application/ITmdbTvWatchlistClient.cs`:

```csharp
namespace Watchlist.Application;

public interface ITmdbTvWatchlistClient
{
    Task<IReadOnlyList<TmdbTvWatchlistItemDto>> GetWatchlistAsync(CancellationToken cancellationToken);
}
```

Create `backend/src/Watchlist.Application/ITmdbTvMetadataClient.cs`:

```csharp
namespace Watchlist.Application;

public interface ITmdbTvMetadataClient
{
    Task<TmdbTvMetadataDto> GetTvMetadataAsync(int tmdbId, CancellationToken cancellationToken);
}
```

Create `backend/src/Watchlist.Application/ITmdbTvWatchlistSyncService.cs`:

```csharp
namespace Watchlist.Application;

public interface ITmdbTvWatchlistSyncService
{
    Task<TmdbTvSyncResultDto> SyncAsync(CancellationToken cancellationToken);
}
```

Modify `backend/src/Watchlist.Application/TmdbExceptions.cs` and add this class after `TmdbMovieNotFoundException`:

```csharp
public sealed class TmdbTvNotFoundException : Exception
{
    public TmdbTvNotFoundException(string message)
        : base(message)
    {
    }
}
```

- [ ] **Step 2: Build the application project**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter DomainEnumTests
```

Expected: project compiles and the filtered test passes.

- [ ] **Step 3: Commit contracts**

```powershell
git add backend\src\Watchlist.Application\ITmdbTvWatchlistClient.cs `
  backend\src\Watchlist.Application\ITmdbTvMetadataClient.cs `
  backend\src\Watchlist.Application\ITmdbTvWatchlistSyncService.cs `
  backend\src\Watchlist.Application\TmdbTvWatchlistItemDto.cs `
  backend\src\Watchlist.Application\TmdbTvMetadataDto.cs `
  backend\src\Watchlist.Application\TmdbTvExternalIdsDto.cs `
  backend\src\Watchlist.Application\TmdbTvSyncResultDto.cs `
  backend\src\Watchlist.Application\TmdbExceptions.cs
git commit -m "feat: add tmdb tv sync contracts"
```

### Task 2: TMDB TV HTTP Clients

**Files:**
- Modify: `backend/src/Watchlist.Infrastructure/TmdbOptions.cs`
- Create: `backend/src/Watchlist.Infrastructure/TmdbTvWatchlistClient.cs`
- Create: `backend/src/Watchlist.Infrastructure/TmdbTvMetadataClient.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TmdbTvWatchlistClientTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TmdbTvMetadataClientTests.cs`

- [ ] **Step 1: Write failing watchlist client tests**

Create `backend/tests/Watchlist.Application.Tests/TmdbTvWatchlistClientTests.cs` with tests that assert:

```csharp
[Fact]
public async Task GetWatchlistAsync_FetchesAllPagesWithSessionAndSort()
{
    Dictionary<string, string> responses = new()
    {
        ["/3/account/123/watchlist/tv?language=en-US&page=1&session_id=session&sort_by=created_at.desc"] = """
        {
          "page": 1,
          "total_pages": 2,
          "results": [
            {
              "id": 1399,
              "name": "Game of Thrones",
              "original_name": "Game of Thrones",
              "overview": "Nine noble families fight for control.",
              "first_air_date": "2011-04-17",
              "poster_path": "/poster.jpg",
              "backdrop_path": "/backdrop.jpg",
              "original_language": "en",
              "vote_average": 8.5,
              "vote_count": 25000
            }
          ]
        }
        """,
        ["/3/account/123/watchlist/tv?language=en-US&page=2&session_id=session&sort_by=created_at.desc"] = """
        {
          "page": 2,
          "total_pages": 2,
          "results": [
            {
              "id": 1436,
              "name": "Justified",
              "original_name": "Justified",
              "overview": "",
              "first_air_date": "2010-03-16",
              "poster_path": null,
              "backdrop_path": null,
              "original_language": "en",
              "vote_average": 7.9,
              "vote_count": 558
            }
          ]
        }
        """
    };
    StaticTmdbHandler handler = new(responses);
    TmdbTvWatchlistClient client = CreateClient(handler);

    IReadOnlyList<TmdbTvWatchlistItemDto> result = await client.GetWatchlistAsync(CancellationToken.None);

    result.Select(item => item.TmdbId).Should().Equal(1399, 1436);
    handler.RequestedPathAndQueries.Should().Equal(responses.Keys);
}
```

Also add tests for missing `AccountId` or `SessionId` throwing `TmdbUnavailableException`, malformed JSON throwing `TmdbParseException`, and HTTP 401/403/429/500 throwing `TmdbUnavailableException`.

- [ ] **Step 2: Write failing metadata client tests**

Create `backend/tests/Watchlist.Application.Tests/TmdbTvMetadataClientTests.cs` with tests that assert:

```csharp
[Fact]
public async Task GetTvMetadataAsync_ParsesDetailsAndExternalIds()
{
    Dictionary<string, string> responses = new()
    {
        ["/3/tv/1399?language=en-US"] = """
        {
          "id": 1399,
          "name": "Game of Thrones",
          "original_name": "Game of Thrones",
          "overview": "Nine noble families fight for control.",
          "first_air_date": "2011-04-17",
          "status": "Ended",
          "poster_path": "/poster.jpg",
          "backdrop_path": "/backdrop.jpg",
          "original_language": "en",
          "vote_average": 8.5,
          "vote_count": 25000,
          "genres": [{ "id": 18, "name": "Drama" }]
        }
        """,
        ["/3/tv/1399/external_ids"] = """
        {
          "id": 1399,
          "imdb_id": "tt0944947",
          "tvdb_id": 121361
        }
        """
    };
    TmdbTvMetadataClient client = CreateClient(responses);

    TmdbTvMetadataDto result = await client.GetTvMetadataAsync(1399, CancellationToken.None);

    result.Should().BeEquivalentTo(new TmdbTvMetadataDto(
        1399,
        "Game of Thrones",
        "Game of Thrones",
        "Nine noble families fight for control.",
        "2011-04-17",
        "Ended",
        "/poster.jpg",
        "/backdrop.jpg",
        "https://image.tmdb.org/t/p/w500/poster.jpg",
        "https://image.tmdb.org/t/p/w1280/backdrop.jpg",
        ["Drama"],
        "en",
        8.5,
        25000,
        new TmdbTvExternalIdsDto("tt0944947", 121361)));
}
```

Also add `TmdbTvNotFoundException`, malformed JSON, missing access token, and HTTP dependency failure tests.

- [ ] **Step 3: Run tests to verify RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "TmdbTvWatchlistClientTests|TmdbTvMetadataClientTests"
```

Expected: compile failure because the infrastructure clients and new options are missing.

- [ ] **Step 4: Add options**

Modify `backend/src/Watchlist.Infrastructure/TmdbOptions.cs`:

```csharp
public int? AccountId { get; init; }

public string SessionId { get; init; } = string.Empty;

public string Language { get; init; } = "en-US";
```

- [ ] **Step 5: Implement `TmdbTvWatchlistClient`**

Create `backend/src/Watchlist.Infrastructure/TmdbTvWatchlistClient.cs`. Follow `TmdbMovieClient` patterns: bearer token header, `BuildRequestUri`, `EnsureSuccess`, and exception mapping. The client must:

- Validate non-empty `AccessToken`.
- Validate `AccountId` exists.
- Validate non-empty `SessionId`.
- Request pages until `page >= total_pages`.
- Use path `/account/{accountId}/watchlist/tv?language={language}&page={page}&session_id={sessionId}&sort_by=created_at.desc`.
- Map blank optional strings to `null`.

- [ ] **Step 6: Implement `TmdbTvMetadataClient`**

Create `backend/src/Watchlist.Infrastructure/TmdbTvMetadataClient.cs`. Follow `TmdbMovieClient` patterns. The client must:

- Request `/tv/{tmdbId}?language={language}`.
- Request `/tv/{tmdbId}/external_ids`.
- Build image URLs with `w500` and `w1280`.
- Require positive ID, non-empty `name`, non-empty `original_name`, and non-null `genres`.
- Map external `imdb_id` and positive `tvdb_id`.

- [ ] **Step 7: Run client tests to verify GREEN**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "TmdbTvWatchlistClientTests|TmdbTvMetadataClientTests"
```

Expected: all TV client tests pass.

- [ ] **Step 8: Commit clients**

```powershell
git add backend\src\Watchlist.Infrastructure\TmdbOptions.cs `
  backend\src\Watchlist.Infrastructure\TmdbTvWatchlistClient.cs `
  backend\src\Watchlist.Infrastructure\TmdbTvMetadataClient.cs `
  backend\tests\Watchlist.Application.Tests\TmdbTvWatchlistClientTests.cs `
  backend\tests\Watchlist.Application.Tests\TmdbTvMetadataClientTests.cs
git commit -m "feat: add tmdb tv clients"
```

### Task 3: TV Sync Service

**Files:**
- Modify: `backend/src/Watchlist.Application/IWatchlistWriteRepository.cs`
- Create: `backend/src/Watchlist.Application/TmdbTvWatchlistSyncService.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TmdbTvWatchlistSyncServiceTests.cs`

- [ ] **Step 1: Extend repository interface**

Modify `IWatchlistWriteRepository` to add:

```csharp
Task<TmdbTvWatchlistApplyResult> ApplyTmdbTvWatchlistSyncAsync(
    IReadOnlyList<WatchlistItemWriteModel> items,
    IReadOnlySet<string> sourceIds,
    string completedStatus,
    DateTimeOffset completedAt,
    CancellationToken cancellationToken);
```

Create `backend/src/Watchlist.Application/TmdbTvWatchlistApplyResult.cs`:

```csharp
namespace Watchlist.Application;

public sealed record TmdbTvWatchlistApplyResult(
    int ItemsUpserted,
    int ItemsDeleted);
```

- [ ] **Step 2: Write failing sync service tests**

Create `backend/tests/Watchlist.Application.Tests/TmdbTvWatchlistSyncServiceTests.cs` covering:

- Enriched TV items become `MediaType.TvShow`, `WatchlistSource.Tmdb`, ID `tv-tmdb-{id}`, source ID as string, detail fields, IMDb ID, and TVDB ID.
- Future `first_air_date` creates `ReleaseStatus.Unreleased` and `AvailabilityStatus.Unreleased`.
- Missing/unrecognized release state creates `ReleaseStatus.Unknown` and `AvailabilityStatus.UnknownMatch`.
- One metadata not-found increments `ItemsNotFound` and does not stop other items.
- One metadata dependency failure increments `ItemsFailed`, returns `partial`, and does not stop other items.

Use fakes:

```csharp
private sealed class FakeTvWatchlistClient(IReadOnlyList<TmdbTvWatchlistItemDto> items) : ITmdbTvWatchlistClient
{
    public Task<IReadOnlyList<TmdbTvWatchlistItemDto>> GetWatchlistAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(items);
    }
}

private sealed class FakeTvMetadataClient : ITmdbTvMetadataClient
{
    public Dictionary<int, TmdbTvMetadataDto> MetadataById { get; } = [];
    public Dictionary<int, Exception> ExceptionsById { get; } = [];

    public Task<TmdbTvMetadataDto> GetTvMetadataAsync(int tmdbId, CancellationToken cancellationToken)
    {
        if (ExceptionsById.TryGetValue(tmdbId, out Exception? exception))
        {
            throw exception;
        }

        return Task.FromResult(MetadataById[tmdbId]);
    }
}
```

- [ ] **Step 3: Run test to verify RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter TmdbTvWatchlistSyncServiceTests
```

Expected: compile failure because service and apply result are missing.

- [ ] **Step 4: Implement sync service**

Create `backend/src/Watchlist.Application/TmdbTvWatchlistSyncService.cs`:

- Use primary constructor with `ITmdbTvWatchlistClient`, `ITmdbTvMetadataClient`, `IWatchlistWriteRepository`, and `TimeProvider`.
- Fetch watchlist source items once.
- Build `sourceIds` from every fetched watchlist source item before per-item metadata calls.
- For each source item, call metadata client.
- Convert metadata to `WatchlistItemWriteModel`.
- Store successful write models.
- Count `not_found` from `TmdbTvNotFoundException`.
- Count dependency/unexpected failures as failed.
- Call `ApplyTmdbTvWatchlistSyncAsync` with successful write models and the full fetched `sourceIds` set, so a temporary metadata failure does not delete an existing TV record that remains on the TMDB watchlist.
- Return status `completed` when failed count is zero, otherwise `partial`.

Release status helper:

```csharp
private static ReleaseStatus ToReleaseStatus(string? firstAirDate, string? status, DateTimeOffset syncTime)
{
    if (DateTimeOffset.TryParse(firstAirDate, out DateTimeOffset parsed)
        && parsed.Date > syncTime.Date)
    {
        return ReleaseStatus.Unreleased;
    }

    return status switch
    {
        "Ended" or "Returning Series" or "Canceled" or "In Production" => ReleaseStatus.Released,
        _ when !string.IsNullOrWhiteSpace(firstAirDate) => ReleaseStatus.Released,
        _ => ReleaseStatus.Unknown
    };
}
```

- [ ] **Step 5: Run service tests to verify GREEN**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter TmdbTvWatchlistSyncServiceTests
```

Expected: all TV sync service tests pass.

- [ ] **Step 6: Commit sync service**

```powershell
git add backend\src\Watchlist.Application\IWatchlistWriteRepository.cs `
  backend\src\Watchlist.Application\TmdbTvWatchlistApplyResult.cs `
  backend\src\Watchlist.Application\TmdbTvWatchlistSyncService.cs `
  backend\tests\Watchlist.Application.Tests\TmdbTvWatchlistSyncServiceTests.cs
git commit -m "feat: add tmdb tv sync service"
```

### Task 4: Mongo Persistence

**Files:**
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistWriteRepository.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoWatchlistWriteRepositoryTests.cs`

- [ ] **Step 1: Add failing Mongo repository test**

Add a test to `MongoWatchlistWriteRepositoryTests`:

```csharp
[Fact]
public async Task ApplyTmdbTvWatchlistSyncAsync_UpsertsTvDeletesRemovedTvAndPreservesOtherSources()
{
    IMongoCollection<MongoWatchlistItemDocument> items =
        database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
    await items.InsertManyAsync([
        MongoWatchlistItemDocument.FromDomain(CreateTmdbTv("removed", "Removed")),
        MongoWatchlistItemDocument.FromDomain(CreateLetterboxdMovie("1297842", "GOAT")),
        MongoWatchlistItemDocument.FromDomain(CreateTmdbMovie())
    ]);
    MongoWatchlistWriteRepository repository = new(database, Options.Create(options));
    WatchlistItem syncedItem = new(
        "tv-tmdb-1399",
        MediaType.TvShow,
        WatchlistSource.Tmdb,
        "1399",
        "Game of Thrones",
        2011,
        "Nine noble families fight for control.",
        "https://image.tmdb.org/t/p/w500/poster.jpg",
        "https://image.tmdb.org/t/p/w1280/backdrop.jpg",
        ReleaseStatus.Released,
        AvailabilityStatus.NotOnPlex,
        DateTimeOffset.Parse("2026-06-06T12:00:00Z"),
        DateTimeOffset.Parse("2026-06-06T12:00:00Z"))
    {
        Genres = ["Drama"],
        OriginalLanguage = "en",
        TmdbVoteAverage = 8.5,
        TmdbVoteCount = 25000
    };
    WatchlistItemWriteModel writeModel = new(
        syncedItem,
        "tt0944947",
        null,
        1399,
        TvdbId: 121361);

    TmdbTvWatchlistApplyResult result = await repository.ApplyTmdbTvWatchlistSyncAsync(
        [writeModel],
        new HashSet<string>(["1399"], StringComparer.Ordinal),
        "tmdb_tv_completed",
        DateTimeOffset.Parse("2026-06-06T12:00:01Z"),
        CancellationToken.None);

    result.ItemsUpserted.Should().Be(1);
    result.ItemsDeleted.Should().Be(1);
    MongoWatchlistItemDocument stored = await items.Find(item => item.Id == "tv-tmdb-1399").SingleAsync();
    stored.TmdbId.Should().Be(1399);
    stored.ImdbId.Should().Be("tt0944947");
    stored.TvdbId.Should().Be(121361);
    stored.TmdbMetadataStatus.Should().Be("enriched");
    stored.Genres.Should().Equal("Drama");
}
```

`WatchlistItemWriteModel` does not yet support `TvdbId`; add that property in this task and update existing call sites by relying on the default `null`.

- [ ] **Step 2: Run test to verify RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter ApplyTmdbTvWatchlistSyncAsync
```

Expected: compile failure because repository method and `TvdbId` support are missing.

- [ ] **Step 3: Add `TvdbId` support**

Modify `WatchlistItemWriteModel`:

```csharp
public sealed record WatchlistItemWriteModel(
    WatchlistItem Item,
    string? ImdbId,
    string? LetterboxdPath,
    int? TmdbId = null,
    int? TvdbId = null);
```

Modify `MongoWatchlistItemDocument`:

```csharp
public int? TvdbId { get; init; }
```

Modify `MongoWatchlistItemDocument.FromDomain` to accept an optional `int? tvdbId = null` parameter and set `TvdbId = tvdbId`. Existing callers keep compiling because the parameter is optional.

- [ ] **Step 4: Implement Mongo TV sync method**

In `MongoWatchlistWriteRepository`, add:

- Filter for removed TV records:

```csharp
return filter.Eq(document => document.MediaType, MediaType.TvShow)
    & filter.Eq(document => document.Source, WatchlistSource.Tmdb)
    & filter.Nin(document => document.SourceId, sourceIds);
```

- Upsert update for TV fields, preserving existing Plex fields by not setting `PlexRatingKey`, `PlexMatchedAt`, `PlexMatchReason`, or `PlexMatchConfidence`.
- Set `TmdbMetadataStatus` to `enriched`, `TmdbMetadataUpdatedAt` to completed time, and `TmdbMetadataError` to null.
- Insert a sync run with status `tmdb_tv_completed`.

- [ ] **Step 5: Run Mongo tests**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "MongoWatchlistWriteRepositoryTests|TmdbTvWatchlistSyncServiceTests"
```

Expected: all selected tests pass. Requires local MongoDB on `localhost:27017`.

- [ ] **Step 6: Commit Mongo persistence**

```powershell
git add backend\src\Watchlist.Application\WatchlistItemWriteModel.cs `
  backend\src\Watchlist.Infrastructure\MongoWatchlistItemDocument.cs `
  backend\src\Watchlist.Infrastructure\MongoWatchlistWriteRepository.cs `
  backend\tests\Watchlist.Application.Tests\MongoWatchlistWriteRepositoryTests.cs
git commit -m "feat: persist tmdb tv watchlist records"
```

### Task 5: Dependency Injection And API Endpoint

**Files:**
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Add failing API tests**

Add tests to `WatchlistApiTests`:

```csharp
[Fact]
public async Task SyncTmdbTv_ReturnsTvSyncResult()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.PostAsync("/api/sync/tmdb/tv", null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    document.RootElement.GetProperty("status").GetString().Should().Be("completed");
    document.RootElement.GetProperty("itemsFetched").GetInt32().Should().Be(2);
    document.RootElement.GetProperty("itemsUpserted").GetInt32().Should().Be(2);
}
```

Register a fake `ITmdbTvWatchlistSyncService` in `SeededApiFactory`.

- [ ] **Step 2: Run API test to verify RED**

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter SyncTmdbTv_ReturnsTvSyncResult
```

Expected: `404 Not Found` until endpoint is mapped.

- [ ] **Step 3: Register services**

In `DependencyInjection.cs`, register:

```csharp
services.AddHttpClient<ITmdbTvWatchlistClient, TmdbTvWatchlistClient>((serviceProvider, httpClient) =>
{
    TmdbOptions options = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.BaseUrl);
});
services.AddHttpClient<ITmdbTvMetadataClient, TmdbTvMetadataClient>((serviceProvider, httpClient) =>
{
    TmdbOptions options = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.BaseUrl);
});
services.AddScoped<ITmdbTvWatchlistSyncService, TmdbTvWatchlistSyncService>();
```

- [ ] **Step 4: Map endpoint**

In `Program.cs`, after TMDB movie endpoints:

```csharp
app.MapPost("/api/sync/tmdb/tv", async (
    ITmdbTvWatchlistSyncService syncService,
    CancellationToken cancellationToken) =>
{
    TmdbTvSyncResultDto result = await syncService.SyncAsync(cancellationToken);

    return Results.Ok(result);
});
```

- [ ] **Step 5: Run API test to verify GREEN**

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter SyncTmdbTv_ReturnsTvSyncResult
```

Expected: test passes.

- [ ] **Step 6: Commit API endpoint**

```powershell
git add backend\src\Watchlist.Infrastructure\DependencyInjection.cs `
  backend\src\Watchlist.Api\Program.cs `
  backend\tests\Watchlist.Api.Tests\SeededApiFactory.cs `
  backend\tests\Watchlist.Api.Tests\WatchlistApiTests.cs
git commit -m "feat: expose tmdb tv sync endpoint"
```

### Task 6: Combined Sync

**Files:**
- Modify: `backend/src/Watchlist.Application/CombinedSyncResultDto.cs`
- Modify: `backend/src/Watchlist.Application/CombinedSyncService.cs`
- Modify: `backend/tests/Watchlist.Application.Tests/CombinedSyncServiceTests.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Add failing combined sync tests**

Update `CombinedSyncServiceTests` so the expected call order is:

```text
letterboxd
tmdb_movies
tmdb_tv
plex_movies
```

Add a fake `ITmdbTvWatchlistSyncService` returning a `TmdbTvSyncResultDto`.

- [ ] **Step 2: Run test to verify RED**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter CombinedSyncServiceTests
```

Expected: compile failure or assertion failure until combined sync is updated.

- [ ] **Step 3: Update result DTO and service**

Modify `CombinedSyncResultDto` to include:

```csharp
TmdbTvSyncResultDto TmdbTv,
```

Modify `CombinedSyncService` primary constructor to accept `ITmdbTvWatchlistSyncService`. Run:

```csharp
TmdbTvSyncResultDto tmdbTv = await tmdbTvWatchlistSyncService.SyncAsync(cancellationToken);
```

between movie enrichment and Plex movie sync.

- [ ] **Step 4: Update API seeded factory and API assertions**

Update seeded combined sync fakes so `/api/sync/all` JSON includes `tmdbTv`.

- [ ] **Step 5: Run combined tests**

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter CombinedSyncServiceTests
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter SyncAll
```

Expected: selected tests pass.

- [ ] **Step 6: Commit combined sync**

```powershell
git add backend\src\Watchlist.Application\CombinedSyncResultDto.cs `
  backend\src\Watchlist.Application\CombinedSyncService.cs `
  backend\tests\Watchlist.Application.Tests\CombinedSyncServiceTests.cs `
  backend\tests\Watchlist.Api.Tests\SeededApiFactory.cs `
  backend\tests\Watchlist.Api.Tests\WatchlistApiTests.cs
git commit -m "feat: include tmdb tv in combined sync"
```

### Task 7: TV Collection API Verification

**Files:**
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Add seeded TV read model test**

Add a seeded TV item with `MediaType.TvShow`, `Source.Tmdb`, source ID `1399`, title `Game of Thrones`, poster/backdrop URLs, and `AvailabilityStatus.NotOnPlex`.

Add test:

```csharp
[Fact]
public async Task GetWatchlist_WhenCollectionTv_ReturnsTmdbTvShows()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.GetAsync(
        "/api/watchlist?collection=tv&availability=plex,not_on_plex,unreleased,unknown_match&sort=title_asc");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    JsonElement item = document.RootElement.EnumerateArray()
        .Single(element => element.GetProperty("id").GetString() == "tv-tmdb-1399");
    item.GetProperty("mediaType").GetString().Should().Be("tv");
    item.GetProperty("source").GetString().Should().Be("tmdb");
    item.GetProperty("title").GetString().Should().Be("Game of Thrones");
}
```

- [ ] **Step 2: Run API TV collection test**

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter GetWatchlist_WhenCollectionTv_ReturnsTmdbTvShows
```

Expected: test passes if existing query service supports TV records. If it fails, fix only the query/read DTO path needed to expose existing `MediaType.TvShow` records.

- [ ] **Step 3: Commit API read verification**

```powershell
git add backend\tests\Watchlist.Api.Tests\SeededApiFactory.cs backend\tests\Watchlist.Api.Tests\WatchlistApiTests.cs
git commit -m "test: cover synced tv collection api"
```

### Task 8: Documentation

**Files:**
- Modify: `docs/api.md`
- Modify: `docs/integrations.md`
- Modify: `docs/architecture.md`
- Modify: `docs/todo.md` if present locally

- [ ] **Step 1: Update API docs**

In `docs/api.md`, add `POST /api/sync/tmdb/tv` after movie TMDB sync docs. Include the response shape from the design spec.

Update `POST /api/sync/all` response to include:

```json
"tmdbTv": {
  "status": "completed",
  "itemsFetched": 14,
  "itemsUpserted": 14,
  "itemsDeleted": 2,
  "itemsEnriched": 14,
  "itemsNotFound": 0,
  "itemsFailed": 0
}
```

- [ ] **Step 2: Update integration docs**

In `docs/integrations.md`, replace the "Still needed for TV watchlist" list with implemented TV watchlist behavior:

- account TV watchlist source
- required local account/session configuration
- per-show details and external IDs
- Mongo fields stored
- remaining TV Plex matching gap

- [ ] **Step 3: Update architecture docs**

In `docs/architecture.md`, update API surface and sync pipeline to include TMDB TV sync before Plex movie sync.

- [ ] **Step 4: Update local backlog if present**

If `docs/todo.md` exists, mark TMDB TV watchlist sync complete and keep Plex TV matching as open. This file is ignored locally, so do not force-add it unless the user asks.

- [ ] **Step 5: Commit docs**

```powershell
git add docs\api.md docs\integrations.md docs\architecture.md
git commit -m "docs: document tmdb tv watchlist sync"
```

### Task 9: Full Verification And Manual Smoke Test

**Files:**
- No code changes expected.

- [ ] **Step 1: Run full backend tests**

```powershell
dotnet test backend\Watchlist.sln --artifacts-path .artifacts\tmdb-tv-watchlist-sync
```

Expected: all backend tests pass. Record total passed count.

- [ ] **Step 2: Inspect final status**

```powershell
git status --short --branch
```

Expected: clean working tree, except ignored local files such as `docs/todo.md` or local credential files.

- [ ] **Step 3: Manual local configuration**

Create or update ignored local config:

```json
{
  "Tmdb": {
    "AccessToken": "put-local-token-here",
    "AccountId": 123,
    "SessionId": "put-local-session-id-here",
    "Language": "en-US"
  }
}
```

Path:

```text
backend/src/Watchlist.Api/appsettings.Development.Local.json
```

- [ ] **Step 4: Manual smoke**

Start Mongo:

```powershell
docker compose up -d mongo
```

Start backend:

```powershell
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5000
```

In another terminal:

```powershell
Invoke-RestMethod -Method Post http://localhost:5000/api/sync/tmdb/tv
Invoke-RestMethod "http://localhost:5000/api/watchlist?collection=tv&availability=plex,not_on_plex,unreleased,unknown_match&sort=title_asc" | Select-Object -First 5
```

Expected:

- Sync endpoint returns `completed` or `partial` with non-negative counts.
- TV collection returns TMDB TV records with `mediaType` `tv`, titles, and backend-relative TMDB image URLs when artwork exists.

- [ ] **Step 5: Final commit check**

```powershell
git log --oneline -8
```

Expected recent commits include:

- `feat: add tmdb tv sync contracts`
- `feat: add tmdb tv clients`
- `feat: add tmdb tv sync service`
- `feat: persist tmdb tv watchlist records`
- `feat: expose tmdb tv sync endpoint`
- `feat: include tmdb tv in combined sync`
- `test: cover synced tv collection api`
- `docs: document tmdb tv watchlist sync`
