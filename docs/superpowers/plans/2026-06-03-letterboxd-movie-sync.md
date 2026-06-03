# Letterboxd Movie Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a manual backend sync that imports the user's Letterboxd movie watchlist JSON into MongoDB and keeps Letterboxd movie records in sync with source-of-truth deletions.

**Architecture:** Add a focused Letterboxd client/parser and a sync orchestration service in the application/infrastructure boundary. Add a Mongo write repository for upsert/delete operations while preserving the existing read repository. Expose a manual `POST /api/sync/letterboxd` endpoint and keep all tests deterministic with fake clients/repositories.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, MongoDB.Driver, System.Text.Json, HttpClientFactory, xUnit, FluentAssertions.

---

## File Structure

- `backend/src/Watchlist.Application/LetterboxdMovieDto.cs`: DTO representing one parsed source movie from the Letterboxd proxy.
- `backend/src/Watchlist.Application/LetterboxdSyncResultDto.cs`: API response for a manual Letterboxd sync run.
- `backend/src/Watchlist.Application/ILetterboxdWatchlistClient.cs`: application boundary for fetching source movies.
- `backend/src/Watchlist.Application/IWatchlistWriteRepository.cs`: write-side repository boundary for the sync service.
- `backend/src/Watchlist.Application/ILetterboxdSyncRunRepository.cs`: write-side sync status boundary.
- `backend/src/Watchlist.Application/ILetterboxdMovieSyncService.cs`: API-facing sync service boundary.
- `backend/src/Watchlist.Application/WatchlistItemWriteModel.cs`: write-side model that carries a clean domain item plus source trace fields for persistence.
- `backend/src/Watchlist.Application/LetterboxdMovieSyncService.cs`: orchestrates fetch, mapping, upserts, deletions, and sync status.
- `backend/src/Watchlist.Infrastructure/LetterboxdOptions.cs`: configuration for the watchlist URL.
- `backend/src/Watchlist.Infrastructure/LetterboxdWatchlistClient.cs`: HTTP client and JSON parser for the Letterboxd proxy.
- `backend/src/Watchlist.Infrastructure/MongoWatchlistWriteRepository.cs`: MongoDB upsert/delete implementation.
- `backend/src/Watchlist.Infrastructure/MongoLetterboxdSyncRunRepository.cs`: inserts successful sync run status.
- `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`: add `ImdbId` and `LetterboxdPath`.
- `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`: register options, HttpClient, sync service dependencies.
- `backend/src/Watchlist.Api/Program.cs`: add `POST /api/sync/letterboxd`.
- `backend/src/Watchlist.Api/appsettings.json`: add default Letterboxd URL.
- `backend/tests/Watchlist.Application.Tests/LetterboxdMovieSyncServiceTests.cs`: application-level sync behavior tests.
- `backend/tests/Watchlist.Application.Tests/LetterboxdWatchlistClientTests.cs`: parser/client tests using a fake HTTP handler.
- `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`: trace-field mapping tests.
- `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`: manual sync endpoint test.
- `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`: override sync service for API tests.
- `docs/api.md`, `docs/integrations.md`, `docs/todo.md`: document the manual sync and keep follow-ups accurate.

---

### Task 1: Parse Letterboxd Watchlist JSON

**Files:**
- Create: `backend/src/Watchlist.Application/LetterboxdMovieDto.cs`
- Create: `backend/src/Watchlist.Application/ILetterboxdWatchlistClient.cs`
- Create: `backend/src/Watchlist.Infrastructure/LetterboxdOptions.cs`
- Create: `backend/src/Watchlist.Infrastructure/LetterboxdWatchlistClient.cs`
- Create: `backend/tests/Watchlist.Application.Tests/LetterboxdWatchlistClientTests.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/appsettings.json`

- [ ] Write failing parser/client tests in `backend/tests/Watchlist.Application.Tests/LetterboxdWatchlistClientTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class LetterboxdWatchlistClientTests
{
    [Fact]
    public async Task GetMoviesAsync_WhenJsonValid_ParsesMovies()
    {
        const string json = """
        [
          {
            "id": 1418998,
            "imdb_id": "tt35450621",
            "title": "Karma",
            "release_year": "2026",
            "clean_title": "/film/karma-2026/",
            "adult": false
          },
          {
            "id": 1635594,
            "imdb_id": "tt39883390",
            "title": "Ti Amo!",
            "release_year": "",
            "clean_title": "/film/ti-amo-1/",
            "adult": false
          }
        ]
        """;
        LetterboxdWatchlistClient client = CreateClient(HttpStatusCode.OK, json);

        IReadOnlyList<LetterboxdMovieDto> movies = await client.GetMoviesAsync(CancellationToken.None);

        movies.Should().Equal(
            new LetterboxdMovieDto("1418998", "tt35450621", "Karma", 2026, "/film/karma-2026/"),
            new LetterboxdMovieDto("1635594", "tt39883390", "Ti Amo!", null, "/film/ti-amo-1/"));
    }

    [Fact]
    public async Task GetMoviesAsync_WhenProxyUnavailable_ThrowsLetterboxdUnavailableException()
    {
        LetterboxdWatchlistClient client = CreateClient(HttpStatusCode.ServiceUnavailable, "unavailable");

        Func<Task> action = () => client.GetMoviesAsync(CancellationToken.None);

        await action.Should().ThrowAsync<LetterboxdUnavailableException>();
    }

    [Fact]
    public async Task GetMoviesAsync_WhenJsonMalformed_ThrowsLetterboxdParseException()
    {
        LetterboxdWatchlistClient client = CreateClient(HttpStatusCode.OK, "[");

        Func<Task> action = () => client.GetMoviesAsync(CancellationToken.None);

        await action.Should().ThrowAsync<LetterboxdParseException>();
    }

    private static LetterboxdWatchlistClient CreateClient(HttpStatusCode statusCode, string content)
    {
        HttpClient httpClient = new(new StaticHttpMessageHandler(statusCode, content));
        LetterboxdOptions options = new()
        {
            WatchlistUrl = "https://example.test/example-user/watchlist"
        };

        return new LetterboxdWatchlistClient(httpClient, Options.Create(options));
    }

    private sealed class StaticHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(content)
            };

            return Task.FromResult(response);
        }
    }
}
```

- [ ] Run focused tests and verify failure because the new types do not exist:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter LetterboxdWatchlistClientTests
```

Expected: compile failure mentioning `LetterboxdWatchlistClient` or `LetterboxdMovieDto`.

- [ ] Add `backend/src/Watchlist.Application/LetterboxdMovieDto.cs`:

```csharp
namespace Watchlist.Application;

public sealed record LetterboxdMovieDto(
    string SourceId,
    string? ImdbId,
    string Title,
    int? ReleaseYear,
    string? LetterboxdPath);
```

- [ ] Add `backend/src/Watchlist.Application/ILetterboxdWatchlistClient.cs`:

```csharp
namespace Watchlist.Application;

public interface ILetterboxdWatchlistClient
{
    Task<IReadOnlyList<LetterboxdMovieDto>> GetMoviesAsync(CancellationToken cancellationToken);
}
```

- [ ] Add `backend/src/Watchlist.Infrastructure/LetterboxdOptions.cs`:

```csharp
namespace Watchlist.Infrastructure;

public sealed class LetterboxdOptions
{
    public const string SectionName = "Letterboxd";

    public string WatchlistUrl { get; init; } = string.Empty;
}
```

- [ ] Add `backend/src/Watchlist.Infrastructure/LetterboxdWatchlistClient.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class LetterboxdUnavailableException(string message) : Exception(message);

public sealed class LetterboxdParseException(string message, Exception innerException)
    : Exception(message, innerException);

public sealed class LetterboxdWatchlistClient(
    HttpClient httpClient,
    IOptions<LetterboxdOptions> options) : ILetterboxdWatchlistClient
{
    public async Task<IReadOnlyList<LetterboxdMovieDto>> GetMoviesAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            options.Value.WatchlistUrl,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable
            || !response.IsSuccessStatusCode)
        {
            throw new LetterboxdUnavailableException(
                $"Letterboxd watchlist proxy returned HTTP {(int)response.StatusCode}.");
        }

        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            SourceMovie[]? movies = JsonSerializer.Deserialize<SourceMovie[]>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (movies is null)
            {
                return [];
            }

            return movies.Select(ToDto).ToList();
        }
        catch (JsonException exception)
        {
            throw new LetterboxdParseException("Letterboxd watchlist proxy returned malformed JSON.", exception);
        }
    }

    private static LetterboxdMovieDto ToDto(SourceMovie movie)
    {
        return new LetterboxdMovieDto(
            movie.Id.ToString(),
            string.IsNullOrWhiteSpace(movie.ImdbId) ? null : movie.ImdbId,
            movie.Title,
            int.TryParse(movie.ReleaseYear, out int releaseYear) ? releaseYear : null,
            string.IsNullOrWhiteSpace(movie.CleanTitle) ? null : movie.CleanTitle);
    }

    private sealed record SourceMovie(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("imdb_id")] string? ImdbId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("release_year")] string? ReleaseYear,
        [property: JsonPropertyName("clean_title")] string? CleanTitle);
}
```

- [ ] Register configuration and client in `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`:

```csharp
services.Configure<LetterboxdOptions>(configuration.GetSection(LetterboxdOptions.SectionName));
services.AddHttpClient<ILetterboxdWatchlistClient, LetterboxdWatchlistClient>();
```

- [ ] Add default configuration to `backend/src/Watchlist.Api/appsettings.json`:

```json
"Letterboxd": {
  "WatchlistUrl": "https://letterboxd-list-radarr.onrender.com/example-user/watchlist"
}
```

- [ ] Run focused tests and verify pass:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter LetterboxdWatchlistClientTests
```

Expected: all `LetterboxdWatchlistClientTests` pass.

- [ ] Commit:

```powershell
git add backend/src/Watchlist.Application/LetterboxdMovieDto.cs backend/src/Watchlist.Application/ILetterboxdWatchlistClient.cs backend/src/Watchlist.Infrastructure/LetterboxdOptions.cs backend/src/Watchlist.Infrastructure/LetterboxdWatchlistClient.cs backend/src/Watchlist.Infrastructure/DependencyInjection.cs backend/src/Watchlist.Api/appsettings.json backend/tests/Watchlist.Application.Tests/LetterboxdWatchlistClientTests.cs
git commit -m "feat: add letterboxd watchlist client"
```

### Task 2: Add Letterboxd Sync Domain Mapping

**Files:**
- Create: `backend/src/Watchlist.Application/LetterboxdSyncResultDto.cs`
- Create: `backend/src/Watchlist.Application/ILetterboxdMovieSyncService.cs`
- Create: `backend/src/Watchlist.Application/IWatchlistWriteRepository.cs`
- Create: `backend/src/Watchlist.Application/ILetterboxdSyncRunRepository.cs`
- Create: `backend/src/Watchlist.Application/LetterboxdMovieSyncService.cs`
- Create: `backend/src/Watchlist.Application/WatchlistItemWriteModel.cs`
- Create: `backend/tests/Watchlist.Application.Tests/LetterboxdMovieSyncServiceTests.cs`

- [ ] Write failing sync service tests in `backend/tests/Watchlist.Application.Tests/LetterboxdMovieSyncServiceTests.cs`:

```csharp
using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class LetterboxdMovieSyncServiceTests
{
    private static readonly DateTimeOffset SyncTime = DateTimeOffset.Parse("2026-06-03T12:00:00Z");

    [Fact]
    public async Task SyncAsync_WhenMoviesFetched_UpsertsMappedMoviesAndDeletesRemovedLetterboxdMovies()
    {
        FakeLetterboxdWatchlistClient client = new([
            new LetterboxdMovieDto("1418998", "tt35450621", "Karma", 2026, "/film/karma-2026/"),
            new LetterboxdMovieDto("4951", "tt0147800", "10 Things I Hate About You", 1999, "/film/10-things-i-hate-about-you/")
        ]);
        FakeWatchlistWriteRepository repository = new([
            CreateExistingMovie("4951"),
            CreateRemovedLetterboxdMovie("old"),
            CreateTvShow()
        ]);
        FakeSyncRunRepository syncRuns = new();
        LetterboxdMovieSyncService service = CreateService(client, repository, syncRuns);

        LetterboxdSyncResultDto result = await service.SyncAsync(CancellationToken.None);

        result.ItemsFetched.Should().Be(2);
        result.ItemsUpserted.Should().Be(2);
        result.ItemsDeleted.Should().Be(1);
        repository.Upserted.Select(item => item.Item.Id).Should().Equal(
            "movie-letterboxd-1418998",
            "movie-letterboxd-4951");
        repository.Upserted.Select(item => item.ImdbId).Should().Equal("tt35450621", "tt0147800");
        repository.Upserted.Select(item => item.LetterboxdPath).Should().Equal(
            "/film/karma-2026/",
            "/film/10-things-i-hate-about-you/");
        repository.DeletedSourceIds.Should().Equal("old");
        repository.Items.Should().Contain(item => item.MediaType == MediaType.TvShow);
        syncRuns.Statuses.Should().Equal("letterboxd_completed");
    }

    [Fact]
    public async Task SyncAsync_WhenExistingRecordUpdated_PreservesAvailabilityAddedAtAndMetadata()
    {
        WatchlistItem existing = CreateExistingMovie("4951");
        FakeLetterboxdWatchlistClient client = new([
            new LetterboxdMovieDto("4951", "tt0147800", "10 Things I Hate About You", 1999, "/film/10-things-i-hate-about-you/")
        ]);
        FakeWatchlistWriteRepository repository = new([existing]);
        LetterboxdMovieSyncService service = CreateService(client, repository, new FakeSyncRunRepository());

        await service.SyncAsync(CancellationToken.None);

        WatchlistItem updated = repository.Upserted.Single().Item;
        updated.AvailabilityStatus.Should().Be(AvailabilityStatus.AvailableOnPlex);
        updated.AddedAt.Should().Be(existing.AddedAt);
        updated.Overview.Should().Be(existing.Overview);
        updated.PosterUrl.Should().Be(existing.PosterUrl);
        updated.BackdropUrl.Should().Be(existing.BackdropUrl);
        updated.UpdatedAt.Should().Be(SyncTime);
    }

    [Theory]
    [InlineData(2027, ReleaseStatus.Unreleased, AvailabilityStatus.Unreleased)]
    [InlineData(1999, ReleaseStatus.Released, AvailabilityStatus.NotOnPlex)]
    public async Task SyncAsync_WhenNewRecordImported_MapsReleaseAndAvailability(
        int releaseYear,
        ReleaseStatus releaseStatus,
        AvailabilityStatus availabilityStatus)
    {
        FakeLetterboxdWatchlistClient client = new([
            new LetterboxdMovieDto("source", "tt0000001", "Movie", releaseYear, "/film/movie/")
        ]);
        FakeWatchlistWriteRepository repository = new([]);
        LetterboxdMovieSyncService service = CreateService(client, repository, new FakeSyncRunRepository());

        await service.SyncAsync(CancellationToken.None);

        WatchlistItem item = repository.Upserted.Single().Item;
        item.ReleaseStatus.Should().Be(releaseStatus);
        item.AvailabilityStatus.Should().Be(availabilityStatus);
        item.AddedAt.Should().Be(SyncTime);
        item.UpdatedAt.Should().Be(SyncTime);
    }

    [Fact]
    public async Task SyncAsync_WhenReleaseYearUnknown_MapsUnknownReleaseAndUnknownMatch()
    {
        FakeLetterboxdWatchlistClient client = new([
            new LetterboxdMovieDto("source", null, "Movie", null, "/film/movie/")
        ]);
        FakeWatchlistWriteRepository repository = new([]);
        LetterboxdMovieSyncService service = CreateService(client, repository, new FakeSyncRunRepository());

        await service.SyncAsync(CancellationToken.None);

        WatchlistItem item = repository.Upserted.Single().Item;
        item.ReleaseStatus.Should().Be(ReleaseStatus.Unknown);
        item.AvailabilityStatus.Should().Be(AvailabilityStatus.UnknownMatch);
    }

    [Fact]
    public async Task SyncAsync_WhenFetchFails_DoesNotModifyRepository()
    {
        FakeLetterboxdWatchlistClient client = new(new LetterboxdUnavailableException("unavailable"));
        FakeWatchlistWriteRepository repository = new([CreateExistingMovie("4951")]);
        LetterboxdMovieSyncService service = CreateService(client, repository, new FakeSyncRunRepository());

        Func<Task> action = () => service.SyncAsync(CancellationToken.None);

        await action.Should().ThrowAsync<LetterboxdUnavailableException>();
        repository.Upserted.Should().BeEmpty();
        repository.DeletedSourceIds.Should().BeEmpty();
    }

    private static LetterboxdMovieSyncService CreateService(
        ILetterboxdWatchlistClient client,
        FakeWatchlistWriteRepository repository,
        FakeSyncRunRepository syncRuns)
    {
        return new LetterboxdMovieSyncService(
            client,
            repository,
            syncRuns,
            new FakeTimeProvider(SyncTime));
    }

    private static WatchlistItem CreateExistingMovie(string sourceId)
    {
        return new WatchlistItem(
            $"movie-letterboxd-{sourceId}",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            sourceId,
            "Old Title",
            1999,
            "Existing overview",
            "https://example.test/poster.jpg",
            "https://example.test/backdrop.jpg",
            ReleaseStatus.Released,
            AvailabilityStatus.AvailableOnPlex,
            DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T12:00:00Z"));
    }

    private static WatchlistItem CreateRemovedLetterboxdMovie(string sourceId)
    {
        return new WatchlistItem(
            $"movie-letterboxd-{sourceId}",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            sourceId,
            "Removed",
            2001,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T12:00:00Z"));
    }

    private static WatchlistItem CreateTvShow()
    {
        return new WatchlistItem(
            "tv-andor",
            MediaType.TvShow,
            WatchlistSource.Tmdb,
            "tmdb-andor",
            "Andor",
            2022,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T12:00:00Z"));
    }

    private sealed class FakeLetterboxdWatchlistClient : ILetterboxdWatchlistClient
    {
        private readonly IReadOnlyList<LetterboxdMovieDto>? movies;
        private readonly Exception? exception;

        public FakeLetterboxdWatchlistClient(IReadOnlyList<LetterboxdMovieDto> movies)
        {
            this.movies = movies;
        }

        public FakeLetterboxdWatchlistClient(Exception exception)
        {
            this.exception = exception;
        }

        public Task<IReadOnlyList<LetterboxdMovieDto>> GetMoviesAsync(CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(movies!);
        }
    }

    private sealed class FakeWatchlistWriteRepository(IReadOnlyList<WatchlistItem> initialItems)
        : IWatchlistWriteRepository
    {
        public List<WatchlistItem> Items { get; } = initialItems.ToList();
        public List<WatchlistItemWriteModel> Upserted { get; } = [];
        public List<string> DeletedSourceIds { get; } = [];

        public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<WatchlistItem>>(Items);
        }

        public Task UpsertItemsAsync(
            IReadOnlyList<WatchlistItemWriteModel> items,
            CancellationToken cancellationToken)
        {
            Upserted.AddRange(items);
            foreach (WatchlistItemWriteModel item in items)
            {
                Items.RemoveAll(existing => existing.Id == item.Item.Id);
                Items.Add(item.Item);
            }

            return Task.CompletedTask;
        }

        public Task<int> DeleteLetterboxdMoviesExceptAsync(
            IReadOnlySet<string> sourceIds,
            CancellationToken cancellationToken)
        {
            List<WatchlistItem> deleted = Items
                .Where(item => item.MediaType == MediaType.Movie
                    && item.Source == WatchlistSource.Letterboxd
                    && !sourceIds.Contains(item.SourceId))
                .ToList();

            DeletedSourceIds.AddRange(deleted.Select(item => item.SourceId));
            Items.RemoveAll(item => deleted.Any(deletedItem => deletedItem.Id == item.Id));
            return Task.FromResult(deleted.Count);
        }
    }

    private sealed class FakeSyncRunRepository : ILetterboxdSyncRunRepository
    {
        public List<string> Statuses { get; } = [];

        public Task InsertSuccessfulRunAsync(string status, DateTimeOffset finishedAt, CancellationToken cancellationToken)
        {
            Statuses.Add(status);
            return Task.CompletedTask;
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

- [ ] Run focused tests and verify failure because sync service/boundaries do not exist:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter LetterboxdMovieSyncServiceTests
```

Expected: compile failure mentioning `LetterboxdMovieSyncService`.

- [ ] Add `backend/src/Watchlist.Application/LetterboxdSyncResultDto.cs`:

```csharp
namespace Watchlist.Application;

public sealed record LetterboxdSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ItemsFetched,
    int ItemsUpserted,
    int ItemsDeleted);
```

- [ ] Add `backend/src/Watchlist.Application/ILetterboxdMovieSyncService.cs`:

```csharp
namespace Watchlist.Application;

public interface ILetterboxdMovieSyncService
{
    Task<LetterboxdSyncResultDto> SyncAsync(CancellationToken cancellationToken);
}
```

- [ ] Add `backend/src/Watchlist.Application/IWatchlistWriteRepository.cs`:

```csharp
using Watchlist.Domain;

namespace Watchlist.Application;

public interface IWatchlistWriteRepository
{
    Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken);

    Task UpsertItemsAsync(
        IReadOnlyList<WatchlistItemWriteModel> items,
        CancellationToken cancellationToken);

    Task<int> DeleteLetterboxdMoviesExceptAsync(
        IReadOnlySet<string> sourceIds,
        CancellationToken cancellationToken);
}
```

- [ ] Add `backend/src/Watchlist.Application/WatchlistItemWriteModel.cs`:

```csharp
using Watchlist.Domain;

namespace Watchlist.Application;

public sealed record WatchlistItemWriteModel(
    WatchlistItem Item,
    string? ImdbId,
    string? LetterboxdPath);
```

- [ ] Add `backend/src/Watchlist.Application/ILetterboxdSyncRunRepository.cs`:

```csharp
namespace Watchlist.Application;

public interface ILetterboxdSyncRunRepository
{
    Task InsertSuccessfulRunAsync(
        string status,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken);
}
```

- [ ] Add `backend/src/Watchlist.Application/LetterboxdMovieSyncService.cs`:

```csharp
using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class LetterboxdMovieSyncService(
    ILetterboxdWatchlistClient client,
    IWatchlistWriteRepository repository,
    ILetterboxdSyncRunRepository syncRuns,
    TimeProvider timeProvider) : ILetterboxdMovieSyncService
{
    private const string CompletedStatus = "letterboxd_completed";

    public async Task<LetterboxdSyncResultDto> SyncAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        IReadOnlyList<LetterboxdMovieDto> sourceMovies = await client.GetMoviesAsync(cancellationToken);
        IReadOnlyList<WatchlistItem> existingItems = await repository.GetItemsAsync(cancellationToken);
        Dictionary<string, WatchlistItem> existingLetterboxdMovies = existingItems
            .Where(item => item.MediaType == MediaType.Movie && item.Source == WatchlistSource.Letterboxd)
            .ToDictionary(item => item.SourceId, StringComparer.Ordinal);

        List<WatchlistItemWriteModel> upsertItems = sourceMovies
            .Select(movie => ToWriteModel(movie, existingLetterboxdMovies, startedAt))
            .ToList();
        HashSet<string> sourceIds = sourceMovies
            .Select(movie => movie.SourceId)
            .ToHashSet(StringComparer.Ordinal);

        await repository.UpsertItemsAsync(upsertItems, cancellationToken);
        int deleted = await repository.DeleteLetterboxdMoviesExceptAsync(sourceIds, cancellationToken);
        DateTimeOffset finishedAt = timeProvider.GetUtcNow();
        await syncRuns.InsertSuccessfulRunAsync(CompletedStatus, finishedAt, cancellationToken);

        return new LetterboxdSyncResultDto(
            "completed",
            startedAt,
            finishedAt,
            sourceMovies.Count,
            upsertItems.Count,
            deleted);
    }

    private static WatchlistItemWriteModel ToWriteModel(
        LetterboxdMovieDto movie,
        IReadOnlyDictionary<string, WatchlistItem> existingMovies,
        DateTimeOffset syncTime)
    {
        existingMovies.TryGetValue(movie.SourceId, out WatchlistItem? existing);
        ReleaseStatus releaseStatus = ToReleaseStatus(movie.ReleaseYear, syncTime.Year);
        AvailabilityStatus availabilityStatus = existing?.AvailabilityStatus
            ?? ToInitialAvailabilityStatus(releaseStatus);

        WatchlistItem item = new(
            $"movie-letterboxd-{movie.SourceId}",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            movie.SourceId,
            movie.Title,
            movie.ReleaseYear,
            existing?.Overview,
            existing?.PosterUrl,
            existing?.BackdropUrl,
            releaseStatus,
            availabilityStatus,
            existing?.AddedAt ?? syncTime,
            syncTime);

        return new WatchlistItemWriteModel(item, movie.ImdbId, movie.LetterboxdPath);
    }

    private static ReleaseStatus ToReleaseStatus(int? releaseYear, int currentYear)
    {
        if (releaseYear is null)
        {
            return ReleaseStatus.Unknown;
        }

        return releaseYear > currentYear ? ReleaseStatus.Unreleased : ReleaseStatus.Released;
    }

    private static AvailabilityStatus ToInitialAvailabilityStatus(ReleaseStatus releaseStatus)
    {
        return releaseStatus switch
        {
            ReleaseStatus.Unreleased => AvailabilityStatus.Unreleased,
            ReleaseStatus.Unknown => AvailabilityStatus.UnknownMatch,
            _ => AvailabilityStatus.NotOnPlex
        };
    }
}
```

- [ ] Register `TimeProvider.System` in `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddSingleton(TimeProvider.System);
services.AddScoped<ILetterboxdMovieSyncService, LetterboxdMovieSyncService>();
```

- [ ] Run focused tests and verify pass:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter LetterboxdMovieSyncServiceTests
```

Expected: all `LetterboxdMovieSyncServiceTests` pass.

- [ ] Commit:

```powershell
git add backend/src/Watchlist.Application/LetterboxdSyncResultDto.cs backend/src/Watchlist.Application/ILetterboxdMovieSyncService.cs backend/src/Watchlist.Application/IWatchlistWriteRepository.cs backend/src/Watchlist.Application/ILetterboxdSyncRunRepository.cs backend/src/Watchlist.Application/WatchlistItemWriteModel.cs backend/src/Watchlist.Application/LetterboxdMovieSyncService.cs backend/src/Watchlist.Infrastructure/DependencyInjection.cs backend/tests/Watchlist.Application.Tests/LetterboxdMovieSyncServiceTests.cs
git commit -m "feat: add letterboxd movie sync service"
```

### Task 3: Persist Letterboxd Sync Writes In MongoDB

**Files:**
- Create: `backend/src/Watchlist.Infrastructure/MongoWatchlistWriteRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoLetterboxdSyncRunRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`

- [ ] Add failing trace-field mapping test to `MongoWatchlistItemDocumentTests`:

```csharp
[Fact]
public void FromDomain_WhenDocumentHasSourceTraceFields_PreservesThemOnDocument()
{
    WatchlistItem item = new(
        "movie-letterboxd-1418998",
        MediaType.Movie,
        WatchlistSource.Letterboxd,
        "1418998",
        "Karma",
        2026,
        null,
        null,
        null,
        ReleaseStatus.Unreleased,
        AvailabilityStatus.Unreleased,
        DateTimeOffset.Parse("2026-06-03T12:00:00Z"),
        DateTimeOffset.Parse("2026-06-03T12:00:00Z"));

    MongoWatchlistItemDocument document = MongoWatchlistItemDocument.FromDomain(
        item,
        "tt35450621",
        "/film/karma-2026/");

    document.ImdbId.Should().Be("tt35450621");
    document.LetterboxdPath.Should().Be("/film/karma-2026/");
}
```

- [ ] Run the focused document tests and verify failure because `ImdbId` and `LetterboxdPath` do not exist:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter MongoWatchlistItemDocumentTests
```

- [ ] Add nullable trace fields to `MongoWatchlistItemDocument`:

```csharp
public string? ImdbId { get; init; }

public string? LetterboxdPath { get; init; }
```

- [ ] Update `MongoWatchlistItemDocument.FromDomain` to accept the trace fields:

```csharp
public static MongoWatchlistItemDocument FromDomain(
    WatchlistItem item,
    string? imdbId = null,
    string? letterboxdPath = null)
{
    return new MongoWatchlistItemDocument
    {
        Id = item.Id,
        MediaType = item.MediaType,
        Source = item.Source,
        SourceId = item.SourceId,
        Title = item.Title,
        ReleaseYear = item.ReleaseYear,
        Overview = item.Overview,
        PosterUrl = item.PosterUrl,
        BackdropUrl = item.BackdropUrl,
        ReleaseStatus = item.ReleaseStatus,
        AvailabilityStatus = item.AvailabilityStatus,
        AddedAt = item.AddedAt,
        UpdatedAt = item.UpdatedAt,
        ImdbId = imdbId,
        LetterboxdPath = letterboxdPath
    };
}
```

Keep `ToDomain()` unchanged so these trace fields stay backend-only in this slice. Do not add these fields to `WatchlistItemDto` or Android models.

- [ ] Add `backend/src/Watchlist.Infrastructure/MongoWatchlistWriteRepository.cs`:

```csharp
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoWatchlistWriteRepository : IWatchlistWriteRepository
{
    private readonly IMongoCollection<MongoWatchlistItemDocument> collection;

    public MongoWatchlistWriteRepository(IMongoDatabase database, IOptions<MongoDbOptions> options)
    {
        collection = database.GetCollection<MongoWatchlistItemDocument>(
            options.Value.WatchlistItemsCollectionName);
    }

    public async Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
    {
        List<MongoWatchlistItemDocument> documents = await collection
            .Find(FilterDefinition<MongoWatchlistItemDocument>.Empty)
            .ToListAsync(cancellationToken);

        return documents.Select(document => document.ToDomain()).ToList();
    }

    public async Task UpsertItemsAsync(
        IReadOnlyList<WatchlistItemWriteModel> items,
        CancellationToken cancellationToken)
    {
        foreach (WatchlistItemWriteModel item in items)
        {
            MongoWatchlistItemDocument document = MongoWatchlistItemDocument.FromDomain(
                item.Item,
                item.ImdbId,
                item.LetterboxdPath);
            await collection.ReplaceOneAsync(
                stored => stored.Id == document.Id,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
    }

    public async Task<int> DeleteLetterboxdMoviesExceptAsync(
        IReadOnlySet<string> sourceIds,
        CancellationToken cancellationToken)
    {
        FilterDefinition<MongoWatchlistItemDocument> filter =
            Builders<MongoWatchlistItemDocument>.Filter.Eq(document => document.MediaType, MediaType.Movie)
            & Builders<MongoWatchlistItemDocument>.Filter.Eq(document => document.Source, WatchlistSource.Letterboxd)
            & Builders<MongoWatchlistItemDocument>.Filter.Nin(document => document.SourceId, sourceIds);

        DeleteResult result = await collection.DeleteManyAsync(filter, cancellationToken);
        return (int)result.DeletedCount;
    }
}
```

- [ ] Add `backend/src/Watchlist.Infrastructure/MongoLetterboxdSyncRunRepository.cs`:

```csharp
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoLetterboxdSyncRunRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : ILetterboxdSyncRunRepository
{
    public async Task InsertSuccessfulRunAsync(
        string status,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken)
    {
        IMongoCollection<MongoSyncRunDocument> collection =
            database.GetCollection<MongoSyncRunDocument>(options.Value.SyncRunsCollectionName);

        MongoSyncRunDocument document = new()
        {
            Id = $"letterboxd-{finishedAt:yyyyMMddHHmmssfffffff}",
            Status = status,
            LastSuccessfulSyncAt = finishedAt
        };

        await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
    }
}
```

- [ ] Register write repositories in `DependencyInjection.cs`:

```csharp
services.AddSingleton<IWatchlistWriteRepository, MongoWatchlistWriteRepository>();
services.AddSingleton<ILetterboxdSyncRunRepository, MongoLetterboxdSyncRunRepository>();
```

- [ ] Run backend application tests:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj
```

Expected: all application tests pass.

- [ ] Commit:

```powershell
git add backend/src/Watchlist.Infrastructure/MongoWatchlistWriteRepository.cs backend/src/Watchlist.Infrastructure/MongoLetterboxdSyncRunRepository.cs backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs backend/src/Watchlist.Infrastructure/DependencyInjection.cs backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs
git commit -m "feat: persist letterboxd movie sync"
```

### Task 4: Expose Manual Sync API

**Files:**
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Modify: `backend/src/Watchlist.Api/MongoUnavailableExceptionHandler.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`

- [ ] Add failing API test to `WatchlistApiTests.cs`:

```csharp
[Fact]
public async Task PostLetterboxdSync_ReturnsSyncResult()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.PostAsync("/api/sync/letterboxd", null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    document.RootElement.GetProperty("status").GetString().Should().Be("completed");
    document.RootElement.GetProperty("itemsFetched").GetInt32().Should().Be(2);
    document.RootElement.GetProperty("itemsUpserted").GetInt32().Should().Be(2);
}
```

- [ ] Run focused API test and verify failure because endpoint does not exist:

```powershell
dotnet test backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj --filter PostLetterboxdSync
```

Expected: test fails with `NotFound`.

- [ ] Update `SeededApiFactory.cs` to override `ILetterboxdMovieSyncService` with a fake:

```csharp
services.RemoveAll<ILetterboxdMovieSyncService>();
services.AddSingleton<ILetterboxdMovieSyncService, SeededLetterboxdMovieSyncService>();
```

Add the fake service inside `SeededApiFactory`:

```csharp
private sealed class SeededLetterboxdMovieSyncService : ILetterboxdMovieSyncService
{
    public Task<LetterboxdSyncResultDto> SyncAsync(CancellationToken cancellationToken)
    {
        LetterboxdSyncResultDto result = new(
            "completed",
            DateTimeOffset.Parse("2026-06-03T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-03T12:00:01Z"),
            2,
            2,
            0);

        return Task.FromResult(result);
    }
}
```

- [ ] Add endpoint to `Program.cs`:

```csharp
app.MapPost("/api/sync/letterboxd", async (
    ILetterboxdMovieSyncService syncService,
    CancellationToken cancellationToken) =>
{
    LetterboxdSyncResultDto result = await syncService.SyncAsync(cancellationToken);

    return Results.Ok(result);
});
```

- [ ] Update `MongoUnavailableExceptionHandler.cs` so it also handles Letterboxd dependency failures:
  - `LetterboxdUnavailableException` maps to `503` and `{ error = "Letterboxd watchlist is unavailable." }`.
  - `LetterboxdParseException` maps to `502` and `{ error = "Letterboxd watchlist returned malformed JSON." }`.

Use this structure:

```csharp
if (exception is LetterboxdUnavailableException)
{
    httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    await httpContext.Response.WriteAsJsonAsync(
        new { error = "Letterboxd watchlist is unavailable." },
        cancellationToken);
    return true;
}

if (exception is LetterboxdParseException)
{
    httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
    await httpContext.Response.WriteAsJsonAsync(
        new { error = "Letterboxd watchlist returned malformed JSON." },
        cancellationToken);
    return true;
}
```

- [ ] Run focused API tests:

```powershell
dotnet test backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj --filter "PostLetterboxdSync|GetSyncStatus"
```

Expected: tests pass.

- [ ] Commit:

```powershell
git add backend/src/Watchlist.Api/Program.cs backend/src/Watchlist.Api/MongoUnavailableExceptionHandler.cs backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs
git commit -m "feat: expose letterboxd sync endpoint"
```

### Task 5: Documentation And Local Smoke

**Files:**
- Modify: `docs/api.md`
- Modify: `docs/integrations.md`
- Modify: `docs/todo.md`

- [ ] Append this section to `docs/api.md` before `## Dependency Errors`:

````markdown
## POST /api/sync/letterboxd

Runs a manual import of the configured Letterboxd movie watchlist.

Response:

```json
{
  "status": "completed",
  "startedAt": "2026-06-03T12:00:00Z",
  "finishedAt": "2026-06-03T12:00:01Z",
  "itemsFetched": 27,
  "itemsUpserted": 27,
  "itemsDeleted": 3
}
```

Dependency errors:

- `503 Service Unavailable` when the Letterboxd proxy is unavailable.
- `502 Bad Gateway` when the Letterboxd proxy returns malformed JSON.
````

- [ ] Replace the current `## Letterboxd` section in `docs/integrations.md` with:

```markdown
## Letterboxd

Purpose: source of truth for movies the user wants to watch.

The backend imports movies from:

`https://letterboxd-list-radarr.onrender.com/example-user/watchlist`

The URL is configured with `Letterboxd:WatchlistUrl` and can be overridden with `Letterboxd__WatchlistUrl`.

The proxy returns Radarr-style JSON with `id`, `imdb_id`, `title`, `release_year`, `clean_title`, and `adult`.

Imported source trace fields:

- `id` maps to the backend `sourceId`.
- `imdb_id` is stored on the MongoDB document for later TMDB/Plex matching.
- `clean_title` is stored on the MongoDB document as the Letterboxd path.
```

- [ ] Add these bullets under `## Backend API Follow-ups` in `docs/todo.md`:

```markdown
- [x] Add manual Letterboxd movie watchlist sync into MongoDB.
- [ ] Add TMDB metadata enrichment for imported Letterboxd movies.
- [ ] Add Plex availability matching for imported movies.
```

- [ ] Run full backend tests:

```powershell
dotnet test backend\Watchlist.sln
```

Expected: all backend tests pass.

- [ ] Run the Android smoke build because DTO/API shape is shared:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest :app:assembleDebug --no-daemon
```

Expected: `BUILD SUCCESSFUL`.

- [ ] Start MongoDB:

```powershell
docker start watchlist-mongo
```

If the container does not exist:

```powershell
docker compose up -d mongo
```

- [ ] Start backend on port `5010`:

```powershell
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5010
```

- [ ] Smoke-test the manual sync:

```powershell
Invoke-RestMethod -Method Post 'http://localhost:5010/api/sync/letterboxd'
Invoke-RestMethod 'http://localhost:5010/api/watchlist?collection=movie&availability=plex,not_on_plex,unreleased,unknown_match&sort=title_asc'
Invoke-RestMethod 'http://localhost:5010/api/sync/status'
```

Expected:

- `POST /api/sync/letterboxd` returns `status` `completed` and `itemsFetched` greater than `0`.
- Movie watchlist includes real Letterboxd titles such as `10 Things I Hate About You` when present in the source.
- `/api/sync/status` returns `letterboxd_completed`.

- [ ] Run diff check:

```powershell
git diff --check
```

Expected: no whitespace errors.

- [ ] Commit:

```powershell
git add docs/api.md docs/integrations.md docs/todo.md
git commit -m "docs: document letterboxd movie sync"
```
