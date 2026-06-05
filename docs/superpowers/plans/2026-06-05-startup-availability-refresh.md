# Startup Availability Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add cached-first Android TV startup with stale-aware backend Plex availability refresh.

**Architecture:** Backend gets a dedicated availability refresh application service and `POST /api/sync/availability/refresh` endpoint. The service checks the latest successful Plex movie sync run and runs Plex sync only when missing or older than 15 minutes. Android renders cached `GET /api/watchlist` data first, then triggers the refresh endpoint in the background and reloads the current query once only when Plex sync actually ran.

**Tech Stack:** .NET 10 minimal API, MongoDB.Driver, xUnit, FluentAssertions, Java Android Activity, `HttpURLConnection`, JUnit.

---

## Preflight Notes

The worktree may already contain an unrelated Plex config bugfix:

- `backend/src/Watchlist.Api/appsettings.json`
- `backend/src/Watchlist.Infrastructure/MongoDbOptions.cs`
- `backend/tests/Watchlist.Application.Tests/MongoDbOptionsTests.cs`

Do not revert those files. If they are still dirty when this plan is executed, either commit them separately first or leave them unstaged while committing this feature. Also leave untracked `opencode.json` alone unless the user explicitly says it belongs in the repository.

Recommended before starting:

```powershell
git status --short --branch
```

Expected: review current dirty state and decide commit boundaries before editing.

## File Structure

Backend application:

- Create `backend/src/Watchlist.Application/SyncRunStatuses.cs`: shared status constants for persisted sync runs.
- Modify `backend/src/Watchlist.Application/PlexMovieSyncService.cs`: use shared Plex completed status constant.
- Modify `backend/src/Watchlist.Application/ISyncStatusReadRepository.cs`: add status-specific latest sync query.
- Create `backend/src/Watchlist.Application/AvailabilityRefreshResultDto.cs`: API response contract.
- Create `backend/src/Watchlist.Application/IAvailabilityRefreshService.cs`: application boundary used by API.
- Create `backend/src/Watchlist.Application/AvailabilityRefreshService.cs`: stale-aware orchestration.

Backend infrastructure/API:

- Modify `backend/src/Watchlist.Infrastructure/MongoSyncStatusReadRepository.cs`: implement status-specific sync lookup.
- Modify `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`: register `IAvailabilityRefreshService`.
- Modify `backend/src/Watchlist.Api/Program.cs`: map `POST /api/sync/availability/refresh`.

Backend tests:

- Create `backend/tests/Watchlist.Application.Tests/AvailabilityRefreshServiceTests.cs`.
- Create `backend/tests/Watchlist.Application.Tests/MongoSyncStatusReadRepositoryTests.cs`.
- Modify `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`.
- Modify `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`.

Android:

- Create `android/app/src/main/java/com/watchlist/tv/AvailabilityRefreshResult.java`.
- Modify `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`.
- Modify `android/app/src/main/java/com/watchlist/tv/MainActivity.java`.
- Modify `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`.

Docs:

- Modify `docs/api.md`.
- Modify `docs/architecture.md`.
- Modify `docs/android-tv.md`.

---

### Task 1: Backend Refresh Service Contract

**Files:**

- Create: `backend/src/Watchlist.Application/SyncRunStatuses.cs`
- Modify: `backend/src/Watchlist.Application/PlexMovieSyncService.cs`
- Modify: `backend/src/Watchlist.Application/ISyncStatusReadRepository.cs`
- Create: `backend/src/Watchlist.Application/AvailabilityRefreshResultDto.cs`
- Create: `backend/src/Watchlist.Application/IAvailabilityRefreshService.cs`
- Create: `backend/src/Watchlist.Application/AvailabilityRefreshService.cs`
- Test: `backend/tests/Watchlist.Application.Tests/AvailabilityRefreshServiceTests.cs`

- [ ] **Step 1: Write failing service tests**

Create `backend/tests/Watchlist.Application.Tests/AvailabilityRefreshServiceTests.cs`:

```csharp
using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class AvailabilityRefreshServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-05T12:00:00Z");

    [Fact]
    public async Task RefreshAsync_WhenLatestPlexSyncIsFresh_SkipsPlexSync()
    {
        FakeSyncStatusReadRepository statusRepository = new(
            new SyncStatusDto(SyncRunStatuses.PlexMoviesCompleted, Now.AddMinutes(-10)));
        FakePlexMovieSyncService plexSyncService = new();
        AvailabilityRefreshService service = new(
            statusRepository,
            plexSyncService,
            new FakeTimeProvider(Now));

        AvailabilityRefreshResultDto result = await service.RefreshAsync(CancellationToken.None);

        result.Status.Should().Be("skipped");
        result.RanPlexSync.Should().BeFalse();
        result.Reason.Should().Be("fresh");
        result.StartedAt.Should().Be(Now);
        result.FinishedAt.Should().Be(Now);
        result.Plex.Should().BeNull();
        plexSyncService.CallCount.Should().Be(0);
        statusRepository.RequestedStatus.Should().Be(SyncRunStatuses.PlexMoviesCompleted);
    }

    [Fact]
    public async Task RefreshAsync_WhenLatestPlexSyncIsStale_RunsPlexSync()
    {
        FakeSyncStatusReadRepository statusRepository = new(
            new SyncStatusDto(SyncRunStatuses.PlexMoviesCompleted, Now.AddMinutes(-16)));
        FakePlexMovieSyncService plexSyncService = new();
        AvailabilityRefreshService service = new(
            statusRepository,
            plexSyncService,
            new FakeTimeProvider(Now));

        AvailabilityRefreshResultDto result = await service.RefreshAsync(CancellationToken.None);

        result.Status.Should().Be("completed");
        result.RanPlexSync.Should().BeTrue();
        result.Reason.Should().Be("stale");
        result.Plex.Should().NotBeNull();
        result.Plex!.WatchlistItemsMatched.Should().Be(40);
        plexSyncService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAsync_WhenLatestPlexSyncIsMissing_RunsPlexSync()
    {
        FakeSyncStatusReadRepository statusRepository = new(null);
        FakePlexMovieSyncService plexSyncService = new();
        AvailabilityRefreshService service = new(
            statusRepository,
            plexSyncService,
            new FakeTimeProvider(Now));

        AvailabilityRefreshResultDto result = await service.RefreshAsync(CancellationToken.None);

        result.Status.Should().Be("completed");
        result.RanPlexSync.Should().BeTrue();
        result.Reason.Should().Be("missing");
        plexSyncService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAsync_WhenPlexSyncFails_PropagatesException()
    {
        FakeSyncStatusReadRepository statusRepository = new(null);
        FakePlexMovieSyncService plexSyncService = new()
        {
            Exception = new PlexUnavailableException("Plex is unavailable.")
        };
        AvailabilityRefreshService service = new(
            statusRepository,
            plexSyncService,
            new FakeTimeProvider(Now));

        Func<Task> act = () => service.RefreshAsync(CancellationToken.None);

        await act.Should().ThrowAsync<PlexUnavailableException>();
    }

    private sealed class FakeSyncStatusReadRepository(SyncStatusDto? latest) : ISyncStatusReadRepository
    {
        public string? RequestedStatus { get; private set; }

        public Task<SyncStatusDto?> GetLatestAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(latest);
        }

        public Task<SyncStatusDto?> GetLatestByStatusAsync(
            string status,
            CancellationToken cancellationToken)
        {
            RequestedStatus = status;
            return Task.FromResult(latest);
        }
    }

    private sealed class FakePlexMovieSyncService : IPlexMovieSyncService
    {
        public int CallCount { get; private set; }

        public Exception? Exception { get; init; }

        public Task<PlexMovieSyncResultDto> SyncMoviesAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            if (Exception is not null)
            {
                return Task.FromException<PlexMovieSyncResultDto>(Exception);
            }

            PlexMovieSyncResultDto result = new(
                "completed",
                Now,
                Now.AddSeconds(5),
                1,
                500,
                500,
                2,
                40,
                220,
                3);

            return Task.FromResult(result);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter AvailabilityRefreshServiceTests --artifacts-path .artifacts\startup-refresh-task1-red
```

Expected: FAIL because `SyncRunStatuses`, `AvailabilityRefreshService`, `AvailabilityRefreshResultDto`, `IAvailabilityRefreshService`, and `GetLatestByStatusAsync` do not exist.

- [ ] **Step 3: Add shared sync run status constants**

Create `backend/src/Watchlist.Application/SyncRunStatuses.cs`:

```csharp
namespace Watchlist.Application;

public static class SyncRunStatuses
{
    public const string PlexMoviesCompleted = "plex_movies_completed";
}
```

Modify `backend/src/Watchlist.Application/PlexMovieSyncService.cs`:

```csharp
using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class PlexMovieSyncService(
    IPlexLibraryClient client,
    IPlexMovieInventoryRepository repository,
    TimeProvider timeProvider) : IPlexMovieSyncService
{
    private const string CompletedResultStatus = "completed";

    public async Task<PlexMovieSyncResultDto> SyncMoviesAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        IReadOnlyList<PlexLibrarySectionDto> sections = await client.GetSectionsAsync(cancellationToken);
        List<PlexLibrarySectionDto> movieSections = sections
            .Where(section => string.Equals(section.Type, "movie", StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<PlexMovieDto> sourceMovies = [];
        foreach (PlexLibrarySectionDto section in movieSections)
        {
            sourceMovies.AddRange(await client.GetMoviesAsync(section, cancellationToken));
        }

        DateTimeOffset syncTime = timeProvider.GetUtcNow();
        HashSet<string> scannedSectionKeys = movieSections
            .Select(section => section.Key)
            .ToHashSet(StringComparer.Ordinal);

        PlexInventoryApplyResult inventoryResult = await repository.ApplyMovieInventoryAsync(
            sourceMovies,
            scannedSectionKeys,
            syncTime,
            cancellationToken);

        IReadOnlyList<PlexMovieDto> plexMovies = await repository.GetMoviesAsync(cancellationToken);
        IReadOnlyList<WatchlistItemWriteModel> watchlistMovies = await repository.GetWatchlistMoviesAsync(cancellationToken);

        List<PlexMovieMatchUpdate> updates = watchlistMovies
            .Select(movie => ToUpdate(movie, PlexMovieMatcher.Match(movie, plexMovies), syncTime))
            .ToList();

        await repository.ApplyMatchUpdatesAsync(
            updates,
            SyncRunStatuses.PlexMoviesCompleted,
            syncTime,
            cancellationToken);

        DateTimeOffset finishedAt = timeProvider.GetUtcNow();

        return new PlexMovieSyncResultDto(
            CompletedResultStatus,
            startedAt,
            finishedAt,
            movieSections.Count,
            sourceMovies.Count,
            inventoryResult.ItemsUpserted,
            inventoryResult.ItemsDeleted,
            updates.Count(update => update.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex),
            updates.Count(update => update.AvailabilityStatus == AvailabilityStatus.NotOnPlex
                || update.AvailabilityStatus == AvailabilityStatus.Unreleased),
            updates.Count(update => update.AvailabilityStatus == AvailabilityStatus.UnknownMatch));
    }

    private static PlexMovieMatchUpdate ToUpdate(
        WatchlistItemWriteModel item,
        PlexMatchResult match,
        DateTimeOffset syncTime)
    {
        DateTimeOffset? matchedAt = match.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex
            ? syncTime
            : null;

        return new PlexMovieMatchUpdate(
            item.Item.Id,
            match.AvailabilityStatus,
            match.PlexRatingKey,
            matchedAt,
            match.MatchReason,
            match.MatchConfidence);
    }
}
```

- [ ] **Step 4: Extend sync status repository interface**

Modify `backend/src/Watchlist.Application/ISyncStatusReadRepository.cs`:

```csharp
namespace Watchlist.Application;

/// <summary>
/// Reads the latest normalized sync state.
/// </summary>
public interface ISyncStatusReadRepository
{
    /// <summary>
    /// Gets the latest sync status, if one has been persisted.
    /// </summary>
    Task<SyncStatusDto?> GetLatestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the latest sync status for one persisted sync run status.
    /// </summary>
    Task<SyncStatusDto?> GetLatestByStatusAsync(
        string status,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Add refresh DTO and service boundary**

Create `backend/src/Watchlist.Application/AvailabilityRefreshResultDto.cs`:

```csharp
namespace Watchlist.Application;

public sealed record AvailabilityRefreshResultDto(
    string Status,
    bool RanPlexSync,
    string Reason,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    PlexMovieSyncResultDto? Plex);
```

Create `backend/src/Watchlist.Application/IAvailabilityRefreshService.cs`:

```csharp
namespace Watchlist.Application;

public interface IAvailabilityRefreshService
{
    Task<AvailabilityRefreshResultDto> RefreshAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 6: Implement availability refresh service**

Create `backend/src/Watchlist.Application/AvailabilityRefreshService.cs`:

```csharp
namespace Watchlist.Application;

public sealed class AvailabilityRefreshService(
    ISyncStatusReadRepository syncStatusRepository,
    IPlexMovieSyncService plexMovieSyncService,
    TimeProvider timeProvider) : IAvailabilityRefreshService
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(15);

    public async Task<AvailabilityRefreshResultDto> RefreshAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        SyncStatusDto? latestPlexSync = await syncStatusRepository.GetLatestByStatusAsync(
            SyncRunStatuses.PlexMoviesCompleted,
            cancellationToken);

        if (latestPlexSync?.LastSuccessfulSyncAt is DateTimeOffset lastSuccessfulSyncAt
            && startedAt - lastSuccessfulSyncAt <= FreshnessWindow)
        {
            return new AvailabilityRefreshResultDto(
                "skipped",
                false,
                "fresh",
                startedAt,
                timeProvider.GetUtcNow(),
                null);
        }

        string reason = latestPlexSync?.LastSuccessfulSyncAt is null ? "missing" : "stale";
        PlexMovieSyncResultDto plex = await plexMovieSyncService.SyncMoviesAsync(cancellationToken);

        return new AvailabilityRefreshResultDto(
            "completed",
            true,
            reason,
            startedAt,
            timeProvider.GetUtcNow(),
            plex);
    }
}
```

- [ ] **Step 7: Run focused service tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter AvailabilityRefreshServiceTests --artifacts-path .artifacts\startup-refresh-task1
```

Expected: PASS.

- [ ] **Step 8: Commit backend contract/service**

Run:

```powershell
git add backend/src/Watchlist.Application/SyncRunStatuses.cs backend/src/Watchlist.Application/PlexMovieSyncService.cs backend/src/Watchlist.Application/ISyncStatusReadRepository.cs backend/src/Watchlist.Application/AvailabilityRefreshResultDto.cs backend/src/Watchlist.Application/IAvailabilityRefreshService.cs backend/src/Watchlist.Application/AvailabilityRefreshService.cs backend/tests/Watchlist.Application.Tests/AvailabilityRefreshServiceTests.cs
git commit -m "feat: add stale-aware availability refresh service"
```

Expected: commit succeeds. If the pre-existing Plex config fix is still dirty, do not include it in this commit.

---

### Task 2: Backend Persistence And API Endpoint

**Files:**

- Modify: `backend/src/Watchlist.Infrastructure/MongoSyncStatusReadRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoSyncStatusReadRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`
- Test: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Write failing Mongo status repository tests**

Create `backend/tests/Watchlist.Application.Tests/MongoSyncStatusReadRepositoryTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoSyncStatusReadRepositoryTests : IAsyncLifetime
{
    private readonly MongoClient client;
    private readonly IMongoDatabase database;
    private readonly MongoDbOptions options;

    public MongoSyncStatusReadRepositoryTests()
    {
        client = new MongoClient("mongodb://localhost:27017");
        string databaseName = $"watchlist_sync_status_tests_{Guid.NewGuid():N}";
        database = client.GetDatabase(databaseName);
        options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName,
            WatchlistItemsCollectionName = "watchlist_items",
            SyncRunsCollectionName = "sync_runs",
            PlexLibraryItemsCollectionName = "plex_library_items"
        };
    }

    [Fact]
    public async Task GetLatestByStatusAsync_ReturnsLatestMatchingStatus()
    {
        IMongoCollection<MongoSyncRunDocument> collection =
            database.GetCollection<MongoSyncRunDocument>(options.SyncRunsCollectionName);
        await collection.InsertManyAsync([
            new MongoSyncRunDocument
            {
                Id = "letterboxd-newer",
                Status = "letterboxd_completed",
                LastSuccessfulSyncAt = DateTimeOffset.Parse("2026-06-05T12:20:00Z")
            },
            new MongoSyncRunDocument
            {
                Id = "plex-older",
                Status = SyncRunStatuses.PlexMoviesCompleted,
                LastSuccessfulSyncAt = DateTimeOffset.Parse("2026-06-05T12:00:00Z")
            },
            new MongoSyncRunDocument
            {
                Id = "plex-newer",
                Status = SyncRunStatuses.PlexMoviesCompleted,
                LastSuccessfulSyncAt = DateTimeOffset.Parse("2026-06-05T12:10:00Z")
            }
        ]);
        MongoSyncStatusReadRepository repository = new(database, Options.Create(options));

        SyncStatusDto? status = await repository.GetLatestByStatusAsync(
            SyncRunStatuses.PlexMoviesCompleted,
            CancellationToken.None);

        status.Should().NotBeNull();
        status!.Status.Should().Be(SyncRunStatuses.PlexMoviesCompleted);
        status.LastSuccessfulSyncAt.Should().Be(DateTimeOffset.Parse("2026-06-05T12:10:00Z"));
    }

    [Fact]
    public async Task GetLatestByStatusAsync_WhenNoMatchingStatus_ReturnsNull()
    {
        MongoSyncStatusReadRepository repository = new(database, Options.Create(options));

        SyncStatusDto? status = await repository.GetLatestByStatusAsync(
            SyncRunStatuses.PlexMoviesCompleted,
            CancellationToken.None);

        status.Should().BeNull();
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(options.DatabaseName);
    }
}
```

- [ ] **Step 2: Run Mongo repository tests to verify they fail**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter MongoSyncStatusReadRepositoryTests --artifacts-path .artifacts\startup-refresh-task2-mongo-red
```

Expected: FAIL because `MongoSyncStatusReadRepository.GetLatestByStatusAsync` is not implemented.

- [ ] **Step 3: Implement status-specific Mongo query**

Modify `backend/src/Watchlist.Infrastructure/MongoSyncStatusReadRepository.cs`:

```csharp
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoSyncStatusReadRepository : ISyncStatusReadRepository
{
    private readonly IMongoCollection<MongoSyncRunDocument> collection;

    public MongoSyncStatusReadRepository(IMongoDatabase database, IOptions<MongoDbOptions> options)
    {
        collection = database.GetCollection<MongoSyncRunDocument>(
            options.Value.SyncRunsCollectionName);
    }

    public async Task<SyncStatusDto?> GetLatestAsync(CancellationToken cancellationToken)
    {
        MongoSyncRunDocument? document = await collection
            .Find(FilterDefinition<MongoSyncRunDocument>.Empty)
            .SortByDescending(syncRun => syncRun.LastSuccessfulSyncAt)
            .FirstOrDefaultAsync(cancellationToken);

        return document?.ToDto();
    }

    public async Task<SyncStatusDto?> GetLatestByStatusAsync(
        string status,
        CancellationToken cancellationToken)
    {
        MongoSyncRunDocument? document = await collection
            .Find(syncRun => syncRun.Status == status)
            .SortByDescending(syncRun => syncRun.LastSuccessfulSyncAt)
            .FirstOrDefaultAsync(cancellationToken);

        return document?.ToDto();
    }
}
```

- [ ] **Step 4: Run Mongo repository tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter MongoSyncStatusReadRepositoryTests --artifacts-path .artifacts\startup-refresh-task2-mongo
```

Expected: PASS.

- [ ] **Step 5: Write failing API endpoint tests**

Modify `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`.

Add constructor parameter:

```csharp
Exception? availabilityRefreshException = null
```

Remove and replace `IAvailabilityRefreshService` inside `ConfigureServices`:

```csharp
services.RemoveAll<IAvailabilityRefreshService>();
services.AddSingleton<IAvailabilityRefreshService>(
    _ => new SeededAvailabilityRefreshService(availabilityRefreshException));
```

Add this nested fake service before the final closing brace:

```csharp
private sealed class SeededAvailabilityRefreshService(Exception? syncException) : IAvailabilityRefreshService
{
    public Task<AvailabilityRefreshResultDto> RefreshAsync(CancellationToken cancellationToken)
    {
        if (syncException is not null)
        {
            return Task.FromException<AvailabilityRefreshResultDto>(syncException);
        }

        AvailabilityRefreshResultDto result = new(
            "completed",
            true,
            "stale",
            DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-05T12:00:05Z"),
            new PlexMovieSyncResultDto(
                "completed",
                DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
                DateTimeOffset.Parse("2026-06-05T12:00:05Z"),
                1,
                500,
                500,
                2,
                40,
                220,
                3));

        return Task.FromResult(result);
    }
}
```

Modify `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs` and add tests after `PostPlexMovieSync_WhenPlexXmlMalformed_ReturnsBadGateway`:

```csharp
[Fact]
public async Task PostAvailabilityRefresh_ReturnsRefreshResult()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.PostAsync("/api/sync/availability/refresh", null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    document.RootElement.GetProperty("status").GetString().Should().Be("completed");
    document.RootElement.GetProperty("ranPlexSync").GetBoolean().Should().BeTrue();
    document.RootElement.GetProperty("reason").GetString().Should().Be("stale");
    document.RootElement.GetProperty("plex").GetProperty("watchlistItemsMatched").GetInt32().Should().Be(40);
}

[Fact]
public async Task PostAvailabilityRefresh_WhenPlexUnavailable_ReturnsServiceUnavailable()
{
    using SeededApiFactory factory = new(
        availabilityRefreshException: new PlexUnavailableException("Plex token is not configured."));
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.PostAsync("/api/sync/availability/refresh", null);

    response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    document.RootElement.GetProperty("error").GetString().Should().Be("Plex is unavailable.");
}
```

- [ ] **Step 6: Run API tests to verify endpoint test fails**

Run:

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "PostAvailabilityRefresh" --artifacts-path .artifacts\startup-refresh-task2-api-red
```

Expected: FAIL because the endpoint is not mapped or the service is not registered.

- [ ] **Step 7: Register refresh service**

Modify `backend/src/Watchlist.Infrastructure/DependencyInjection.cs` and add this registration next to the Plex sync registration:

```csharp
services.AddScoped<IPlexMovieSyncService, PlexMovieSyncService>();
services.AddScoped<IAvailabilityRefreshService, AvailabilityRefreshService>();
services.AddScoped<ICombinedSyncService, CombinedSyncService>();
```

- [ ] **Step 8: Map refresh endpoint**

Modify `backend/src/Watchlist.Api/Program.cs` and add this endpoint after `/api/sync/plex/movies`:

```csharp
app.MapPost("/api/sync/availability/refresh", async (
    IAvailabilityRefreshService refreshService,
    CancellationToken cancellationToken) =>
{
    AvailabilityRefreshResultDto result = await refreshService.RefreshAsync(cancellationToken);

    return Results.Ok(result);
});
```

- [ ] **Step 9: Run focused backend tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "AvailabilityRefreshServiceTests|MongoSyncStatusReadRepositoryTests" --artifacts-path .artifacts\startup-refresh-task2-app
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "PostAvailabilityRefresh|PostPlexMovieSync" --artifacts-path .artifacts\startup-refresh-task2-api
```

Expected: PASS.

- [ ] **Step 10: Commit backend endpoint**

Run:

```powershell
git add backend/src/Watchlist.Infrastructure/MongoSyncStatusReadRepository.cs backend/src/Watchlist.Infrastructure/DependencyInjection.cs backend/src/Watchlist.Api/Program.cs backend/tests/Watchlist.Application.Tests/MongoSyncStatusReadRepositoryTests.cs backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs
git commit -m "feat: expose startup availability refresh endpoint"
```

Expected: commit succeeds.

---

### Task 3: Android API Client Refresh Support

**Files:**

- Create: `android/app/src/main/java/com/watchlist/tv/AvailabilityRefreshResult.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`
- Test: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`

- [ ] **Step 1: Write failing Android API client tests**

Modify `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java` and add:

```java
@Test
public void buildAvailabilityRefreshPath_usesStartupRefreshEndpoint() {
    assertEquals("/api/sync/availability/refresh", WatchlistApiClient.buildAvailabilityRefreshPath());
}

@Test
public void parseAvailabilityRefreshResult_parsesCompletedResponse() throws Exception {
    String json = "{"
            + "\"status\":\"completed\","
            + "\"ranPlexSync\":true,"
            + "\"reason\":\"stale\","
            + "\"startedAt\":\"2026-06-05T12:00:00Z\","
            + "\"finishedAt\":\"2026-06-05T12:00:05Z\","
            + "\"plex\":{\"watchlistItemsMatched\":40}"
            + "}";

    AvailabilityRefreshResult result = WatchlistApiClient.parseAvailabilityRefreshResult(json);

    assertEquals("completed", result.status());
    assertEquals(true, result.ranPlexSync());
    assertEquals("stale", result.reason());
}

@Test
public void parseAvailabilityRefreshResult_parsesSkippedResponse() throws Exception {
    String json = "{"
            + "\"status\":\"skipped\","
            + "\"ranPlexSync\":false,"
            + "\"reason\":\"fresh\","
            + "\"startedAt\":\"2026-06-05T12:00:00Z\","
            + "\"finishedAt\":\"2026-06-05T12:00:00Z\","
            + "\"plex\":null"
            + "}";

    AvailabilityRefreshResult result = WatchlistApiClient.parseAvailabilityRefreshResult(json);

    assertEquals("skipped", result.status());
    assertEquals(false, result.ranPlexSync());
    assertEquals("fresh", result.reason());
}
```

- [ ] **Step 2: Run Android client tests to verify failure**

Run:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.WatchlistApiClientTest --no-daemon
```

Expected: FAIL because `AvailabilityRefreshResult`, `buildAvailabilityRefreshPath`, and `parseAvailabilityRefreshResult` do not exist.

- [ ] **Step 3: Add Android refresh result model**

Create `android/app/src/main/java/com/watchlist/tv/AvailabilityRefreshResult.java`:

```java
package com.watchlist.tv;

public record AvailabilityRefreshResult(
        String status,
        boolean ranPlexSync,
        String reason) {
}
```

- [ ] **Step 4: Add refresh path, parser, and POST helper**

Modify `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`.

Add this public method after `getSyncStatus`:

```java
public AvailabilityRefreshResult refreshAvailability() throws IOException, JSONException {
    return parseAvailabilityRefreshResult(post(buildAvailabilityRefreshPath()));
}
```

Add these static methods after `buildWatchlistPath`:

```java
public static String buildAvailabilityRefreshPath() {
    return "/api/sync/availability/refresh";
}
```

Add this parser after `parseSyncStatus`:

```java
public static AvailabilityRefreshResult parseAvailabilityRefreshResult(String json) throws JSONException {
    JSONObject object = new JSONObject(json);
    return new AvailabilityRefreshResult(
            object.getString("status"),
            object.getBoolean("ranPlexSync"),
            object.getString("reason"));
}
```

Add this private helper after `get`:

```java
private String post(String path) throws IOException {
    URL url = new URL(baseUrl + path);
    HttpURLConnection connection = (HttpURLConnection) url.openConnection();
    connection.setConnectTimeout(5000);
    connection.setReadTimeout(30000);
    connection.setRequestMethod("POST");
    connection.setDoOutput(true);
    connection.setFixedLengthStreamingMode(0);

    int statusCode = connection.getResponseCode();
    InputStream stream = statusCode >= 200 && statusCode < 300
            ? connection.getInputStream()
            : connection.getErrorStream();
    String body = readAll(stream);
    connection.disconnect();

    if (statusCode < 200 || statusCode >= 300) {
        throw new IOException("Backend returned HTTP " + statusCode + ": " + body);
    }

    return body;
}
```

- [ ] **Step 5: Run Android client tests**

Run:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.WatchlistApiClientTest --no-daemon
```

Expected: PASS.

- [ ] **Step 6: Commit Android API client support**

Run:

```powershell
git add android/app/src/main/java/com/watchlist/tv/AvailabilityRefreshResult.java android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java
git commit -m "feat: add Android availability refresh client"
```

Expected: commit succeeds.

---

### Task 4: Android Cached-First Startup Refresh

**Files:**

- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`

- [ ] **Step 1: Add startup refresh state**

Modify `android/app/src/main/java/com/watchlist/tv/MainActivity.java`.

Add field near `loadGeneration`:

```java
private boolean startupAvailabilityRefreshStarted;
```

- [ ] **Step 2: Start first load with startup refresh enabled**

Modify `onCreate`:

```java
allButton.requestFocus();
loadItems(true);
```

- [ ] **Step 3: Keep existing call sites as normal loads**

Replace existing `loadItems();` calls in `selectMediaType`, `selectSortMode`, and the availability checkbox listener with:

```java
loadItems(false);
```

- [ ] **Step 4: Add overloaded load method**

Replace the current `private void loadItems()` method with:

```java
private void loadItems() {
    loadItems(false);
}

private void loadItems(boolean requestStartupAvailabilityRefresh) {
    if (destroyed) {
        return;
    }
    int generation = ++loadGeneration;
    String mediaType = browsingState.mediaType();
    String sortMode = browsingState.sortMode();
    boolean includeUnavailable = browsingState.includeUnavailable();
    showLoading();
    try {
        apiExecutor.execute(() -> {
            try {
                List<WatchlistItem> items = apiClient.getWatchlist(
                        mediaType,
                        sortMode,
                        includeUnavailable);
                if (!destroyed) {
                    mainHandler.post(() -> {
                        if (!destroyed && generation == loadGeneration) {
                            loadedItems = items;
                            renderItems(items, true);
                            if (requestStartupAvailabilityRefresh) {
                                refreshAvailabilityAfterInitialLoad(generation);
                            }
                        }
                    });
                }
            } catch (Exception exception) {
                if (!destroyed) {
                    mainHandler.post(() -> {
                        if (!destroyed && generation == loadGeneration) {
                            showError(exception);
                        }
                    });
                }
            }
        });
    } catch (RejectedExecutionException ignored) {
        // The Activity is tearing down.
    }
}
```

- [ ] **Step 5: Add background refresh method**

Add this method after `loadItems(boolean requestStartupAvailabilityRefresh)`:

```java
private void refreshAvailabilityAfterInitialLoad(int initialGeneration) {
    if (startupAvailabilityRefreshStarted || destroyed || initialGeneration != loadGeneration) {
        return;
    }

    startupAvailabilityRefreshStarted = true;
    try {
        apiExecutor.execute(() -> {
            try {
                AvailabilityRefreshResult result = apiClient.refreshAvailability();
                if (!destroyed && result.ranPlexSync()) {
                    mainHandler.post(() -> {
                        if (!destroyed) {
                            loadItems(false);
                        }
                    });
                }
            } catch (Exception ignored) {
                // Cached data is already visible; startup refresh must not replace it with an error.
            }
        });
    } catch (RejectedExecutionException ignored) {
        // The Activity is tearing down.
    }
}
```

- [ ] **Step 6: Run Android build/tests**

Run:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest :app:assembleDebug --no-daemon
```

Expected: PASS.

- [ ] **Step 7: Manual Android verification**

Start backend and Mongo:

```powershell
docker compose up -d mongo
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5000
```

Open Android TV emulator and launch the app.

Expected:

- The grid appears from cached API data.
- The app remains navigable while refresh runs.
- If backend returns `ranPlexSync = true`, the grid reloads once.
- If backend returns `ranPlexSync = false`, the grid does not visibly reload.
- If Plex is unavailable but `GET /api/watchlist` succeeds, cached items remain visible.

- [ ] **Step 8: Commit Android startup refresh**

Run:

```powershell
git add android/app/src/main/java/com/watchlist/tv/MainActivity.java
git commit -m "feat: refresh availability on Android startup"
```

Expected: commit succeeds.

---

### Task 5: Documentation And End-To-End Verification

**Files:**

- Modify: `docs/api.md`
- Modify: `docs/architecture.md`
- Modify: `docs/android-tv.md`

- [ ] **Step 1: Update API docs**

Modify `docs/api.md` and add this section after `POST /api/sync/plex/movies`:

````markdown
## POST /api/sync/availability/refresh

Runs the app-open availability refresh. The backend checks the latest successful Plex movie sync and only runs Plex sync when the cached availability is missing or stale.

Freshness window: 15 minutes.

Skipped response:

```json
{
  "status": "skipped",
  "ranPlexSync": false,
  "reason": "fresh",
  "startedAt": "2026-06-05T12:00:00Z",
  "finishedAt": "2026-06-05T12:00:00Z",
  "plex": null
}
```

Completed response:

```json
{
  "status": "completed",
  "ranPlexSync": true,
  "reason": "stale",
  "startedAt": "2026-06-05T12:00:00Z",
  "finishedAt": "2026-06-05T12:00:05Z",
  "plex": {
    "status": "completed",
    "sectionsScanned": 1,
    "itemsFetched": 500,
    "itemsUpserted": 500,
    "itemsDeleted": 2,
    "watchlistItemsMatched": 40,
    "watchlistItemsNotMatched": 220,
    "watchlistItemsUnknown": 3
  }
}
```

`reason` values:

- `fresh`: latest successful Plex movie sync is inside the freshness window.
- `stale`: latest successful Plex movie sync is older than the freshness window.
- `missing`: no previous successful Plex movie sync is known.

Dependency errors match `POST /api/sync/plex/movies`.
````

- [ ] **Step 2: Update architecture docs**

Modify `docs/architecture.md`.

In `Android TV Responsibilities`, add:

```markdown
- Trigger backend-owned startup availability refresh without blocking cached watchlist rendering.
```

In `API Surface`, add:

```markdown
- `POST /api/sync/availability/refresh` — app-open stale-aware Plex availability refresh.
```

After the API surface paragraph, add:

```markdown
Android startup is cached-first. The client loads `GET /api/watchlist` immediately, then triggers `POST /api/sync/availability/refresh`. The backend runs Plex sync only when the latest successful Plex movie sync is older than 15 minutes or missing. If Plex sync runs, Android reloads the current watchlist query once.
```

- [ ] **Step 3: Update Android TV docs**

Modify `docs/android-tv.md` and add a startup refresh section:

```markdown
## Startup Availability Refresh

On app open, Android TV loads cached watchlist data first. After the first successful render it calls `POST /api/sync/availability/refresh` in the background.

- `ranPlexSync = false`: keep the current grid.
- `ranPlexSync = true`: reload the current watchlist query once.
- Refresh failure: keep cached data visible.

Manual test:

1. Start MongoDB and the backend.
2. Open the Android TV app.
3. Confirm posters render before Plex refresh needs to complete.
4. Run `Invoke-RestMethod http://localhost:5000/api/sync/status` and confirm Plex sync state after refresh when stale or missing.
5. Temporarily stop Plex or remove local Plex credentials, restart the backend, and confirm cached watchlist data still renders if `GET /api/watchlist` succeeds.
```

- [ ] **Step 4: Run backend and Android verification**

Run:

```powershell
dotnet test backend\Watchlist.sln --artifacts-path .artifacts\startup-refresh-full-backend
```

Expected: PASS.

Run:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest :app:assembleDebug --no-daemon
```

Expected: PASS.

- [ ] **Step 5: Live smoke test**

Restart backend so local config is loaded:

```powershell
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5000
```

In a second PowerShell:

```powershell
Invoke-RestMethod -Method Post http://localhost:5000/api/sync/availability/refresh
Invoke-RestMethod "http://localhost:5000/api/watchlist?collection=movie&availability=plex&sort=title_asc"
```

Expected:

- First command returns either `status = skipped` with `reason = fresh`, or `status = completed` with `ranPlexSync = true`.
- Second command returns Plex-available movies when Plex matching has found watchlist matches.

- [ ] **Step 6: Commit docs and final verification state**

Run:

```powershell
git add docs/api.md docs/architecture.md docs/android-tv.md
git commit -m "docs: document startup availability refresh"
```

Expected: commit succeeds.

---

## Final Review Checklist

- [ ] `POST /api/sync/availability/refresh` exists and returns `skipped` for fresh Plex sync state.
- [ ] `POST /api/sync/availability/refresh` runs Plex sync for stale or missing Plex sync state.
- [ ] Android first renders cached watchlist data from `GET /api/watchlist`.
- [ ] Android refresh failure does not replace cached grid with an error screen.
- [ ] Android reloads the current watchlist once when `ranPlexSync = true`.
- [ ] Full backend tests pass.
- [ ] Android unit tests and debug assemble pass.
- [ ] Existing manual `POST /api/sync/plex/movies` still works.
- [ ] Docs mention the endpoint, cached-first startup behavior, and 15-minute freshness window.
