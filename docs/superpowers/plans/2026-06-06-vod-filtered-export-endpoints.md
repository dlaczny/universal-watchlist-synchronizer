# VOD-Filtered Export Endpoints Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add backend-only export endpoints that return Radarr-style Letterboxd movies excluding items already available on subscribed VOD services, plus a placeholder TV export endpoint.

**Architecture:** Add a dedicated export query path instead of reusing Android watchlist DTOs. The application layer owns export DTOs and filtering rules; infrastructure reads cached MongoDB watchlist document fields; the API maps two read-only endpoints and uses existing global Mongo error handling.

**Tech Stack:** .NET minimal API, C# records, MongoDB.Driver, xUnit, FluentAssertions, ASP.NET Core WebApplicationFactory.

---

## File Structure

- Create `backend/src/Watchlist.Application/RadarrMovieExportItemDto.cs`: Radarr/Letterboxd-compatible JSON DTO with snake_case property names.
- Create `backend/src/Watchlist.Application/WatchlistExportMovieModel.cs`: internal export source model containing cached Mongo fields needed for filtering and mapping.
- Create `backend/src/Watchlist.Application/IWatchlistExportRepository.cs`: application boundary for cached export source reads.
- Create `backend/src/Watchlist.Application/WatchlistExportService.cs`: filters VOD-available movies and maps export DTOs; returns empty TV list for v1.
- Create `backend/src/Watchlist.Infrastructure/MongoWatchlistExportRepository.cs`: Mongo implementation that reads Letterboxd movie export fields from `watchlist_items`.
- Modify `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`: register `IWatchlistExportRepository`.
- Modify `backend/src/Watchlist.Api/Program.cs`: register `WatchlistExportService` and map `GET /api/export/radarr/movies` and `GET /api/export/sonarr/tv`.
- Modify `backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj`: no package changes expected.
- Create `backend/tests/Watchlist.Application.Tests/WatchlistExportServiceTests.cs`: TDD coverage for filtering and mapping.
- Modify `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`: replace export repository with a seeded fake for API tests.
- Modify `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`: endpoint contract tests.
- Modify `backend/tests/Watchlist.Api.Tests/MongoUnavailableApiFactory.cs`: no change expected unless it needs export repository removal; verify after tests.
- Modify `docs/api.md`: document both export endpoints.
- Modify `docs/integrations.md`: document export purpose and cached-data behavior.

Before implementation, run `git status --short` and preserve unrelated local changes. The current worktree may contain existing dirty files from earlier badge/API work; do not revert them.

---

### Task 1: Application Export Service

**Files:**
- Create: `backend/src/Watchlist.Application/RadarrMovieExportItemDto.cs`
- Create: `backend/src/Watchlist.Application/WatchlistExportMovieModel.cs`
- Create: `backend/src/Watchlist.Application/IWatchlistExportRepository.cs`
- Create: `backend/src/Watchlist.Application/WatchlistExportService.cs`
- Test: `backend/tests/Watchlist.Application.Tests/WatchlistExportServiceTests.cs`

- [ ] **Step 1: Write failing tests for movie export filtering and mapping**

Create `backend/tests/Watchlist.Application.Tests/WatchlistExportServiceTests.cs`:

```csharp
using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class WatchlistExportServiceTests
{
    [Fact]
    public async Task GetRadarrMoviesAsync_WhenMovieHasNoOwnedVodAvailability_IncludesMovie()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository(
        [
            new WatchlistExportMovieModel(
                "1297842",
                "tt27613895",
                "GOAT",
                2026,
                "/film/goat-2026/",
                [])
        ]));

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = 1297842,
            ImdbId = "tt27613895",
            Title = "GOAT",
            ReleaseYear = "2026",
            CleanTitle = "/film/goat-2026/",
            Adult = false
        });
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenMovieHasOwnedVodAvailability_ExcludesMovie()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository(
        [
            new WatchlistExportMovieModel(
                "4951",
                "tt0147800",
                "10 Things I Hate About You",
                1999,
                "/film/10-things-i-hate-about-you/",
                ["Amazon Prime Video"])
        ]));

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenTmdbEnrichmentMissing_IncludesMovie()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository(
        [
            new WatchlistExportMovieModel(
                "1418998",
                "tt35450621",
                "Karma",
                2026,
                "/film/karma-2026/",
                [])
        ]));

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Select(item => item.Title).Should().Equal("Karma");
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenSourceIdCannotBeParsed_SkipsMovie()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository(
        [
            new WatchlistExportMovieModel(
                "bad-source",
                "tt0000001",
                "Malformed",
                2026,
                "/film/malformed/",
                [])
        ]));

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenOptionalFieldsMissing_UsesEmptyStrings()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository(
        [
            new WatchlistExportMovieModel(
                "1635594",
                null,
                "Ti Amo!",
                null,
                null,
                [])
        ]));

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        RadarrMovieExportItemDto item = result.Should().ContainSingle().Subject;
        item.ImdbId.Should().BeEmpty();
        item.ReleaseYear.Should().BeEmpty();
        item.CleanTitle.Should().BeEmpty();
    }

    [Fact]
    public Task GetSonarrTvAsync_ForV1_ReturnsEmptyList()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository([]));

        return service.GetSonarrTvAsync(CancellationToken.None)
            .ContinueWith(task => task.Result.Should().BeEmpty(), CancellationToken.None);
    }

    private sealed class StubWatchlistExportRepository(
        IReadOnlyList<WatchlistExportMovieModel> movies) : IWatchlistExportRepository
    {
        public Task<IReadOnlyList<WatchlistExportMovieModel>> GetLetterboxdMoviesAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(movies);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter WatchlistExportServiceTests
```

Expected: compile failure because `WatchlistExportService`, `WatchlistExportMovieModel`, `RadarrMovieExportItemDto`, and `IWatchlistExportRepository` do not exist.

- [ ] **Step 3: Add export DTO and source model**

Create `backend/src/Watchlist.Application/RadarrMovieExportItemDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace Watchlist.Application;

public sealed record RadarrMovieExportItemDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("imdb_id")] string ImdbId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("release_year")] string ReleaseYear,
    [property: JsonPropertyName("clean_title")] string CleanTitle,
    [property: JsonPropertyName("adult")] bool Adult);
```

Create `backend/src/Watchlist.Application/WatchlistExportMovieModel.cs`:

```csharp
namespace Watchlist.Application;

public sealed record WatchlistExportMovieModel(
    string SourceId,
    string? ImdbId,
    string Title,
    int? Year,
    string? LetterboxdPath,
    IReadOnlyList<string> OwnedServiceAvailability);
```

Create `backend/src/Watchlist.Application/IWatchlistExportRepository.cs`:

```csharp
namespace Watchlist.Application;

public interface IWatchlistExportRepository
{
    Task<IReadOnlyList<WatchlistExportMovieModel>> GetLetterboxdMoviesAsync(
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Add minimal export service**

Create `backend/src/Watchlist.Application/WatchlistExportService.cs`:

```csharp
namespace Watchlist.Application;

public sealed class WatchlistExportService(IWatchlistExportRepository repository)
{
    public async Task<IReadOnlyList<RadarrMovieExportItemDto>> GetRadarrMoviesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WatchlistExportMovieModel> movies =
            await repository.GetLetterboxdMoviesAsync(cancellationToken);

        return movies
            .Where(movie => movie.OwnedServiceAvailability.Count == 0)
            .Select(ToRadarrItemOrNull)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    public Task<IReadOnlyList<object>> GetSonarrTvAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<object>>([]);
    }

    private static RadarrMovieExportItemDto? ToRadarrItemOrNull(WatchlistExportMovieModel movie)
    {
        if (!int.TryParse(movie.SourceId, out int sourceId))
        {
            return null;
        }

        return new RadarrMovieExportItemDto(
            sourceId,
            movie.ImdbId ?? string.Empty,
            movie.Title,
            movie.Year?.ToString() ?? string.Empty,
            movie.LetterboxdPath ?? string.Empty,
            false);
    }
}
```

- [ ] **Step 5: Run tests to verify GREEN**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter WatchlistExportServiceTests
```

Expected: all `WatchlistExportServiceTests` pass.

- [ ] **Step 6: Commit Task 1**

Run:

```powershell
git add backend\src\Watchlist.Application\RadarrMovieExportItemDto.cs `
  backend\src\Watchlist.Application\WatchlistExportMovieModel.cs `
  backend\src\Watchlist.Application\IWatchlistExportRepository.cs `
  backend\src\Watchlist.Application\WatchlistExportService.cs `
  backend\tests\Watchlist.Application.Tests\WatchlistExportServiceTests.cs
git commit -m "feat: add watchlist export service"
```

---

### Task 2: Mongo Export Repository

**Files:**
- Create: `backend/src/Watchlist.Infrastructure/MongoWatchlistExportRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoWatchlistExportRepositoryTests.cs`

- [ ] **Step 1: Write a failing repository mapper test**

Create `backend/tests/Watchlist.Application.Tests/MongoWatchlistExportRepositoryTests.cs`:

```csharp
using FluentAssertions;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoWatchlistExportRepositoryTests
{
    [Fact]
    public void ToExportModel_MapsRadarrExportFieldsFromMongoDocument()
    {
        MongoWatchlistItemDocument document = new()
        {
            Id = "movie-letterboxd-1297842",
            MediaType = MediaType.Movie,
            Source = WatchlistSource.Letterboxd,
            SourceId = "1297842",
            ImdbId = "tt27613895",
            Title = "GOAT",
            Year = 2026,
            LetterboxdPath = "/film/goat-2026/",
            OwnedServiceAvailability = ["Amazon Prime Video"],
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.NotOnPlex,
            UpdatedAt = DateTimeOffset.Parse("2026-06-05T12:00:00Z")
        };

        WatchlistExportMovieModel result = MongoWatchlistExportRepository.ToExportModel(document);

        result.SourceId.Should().Be("1297842");
        result.ImdbId.Should().Be("tt27613895");
        result.Title.Should().Be("GOAT");
        result.Year.Should().Be(2026);
        result.LetterboxdPath.Should().Be("/film/goat-2026/");
        result.OwnedServiceAvailability.Should().Equal("Amazon Prime Video");
    }
}
```

- [ ] **Step 2: Run test to verify RED**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter MongoWatchlistExportRepositoryTests
```

Expected: compile failure because `MongoWatchlistExportRepository` does not exist.

- [ ] **Step 3: Add Mongo repository implementation**

Create `backend/src/Watchlist.Infrastructure/MongoWatchlistExportRepository.cs`:

```csharp
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoWatchlistExportRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : IWatchlistExportRepository
{
    private readonly IMongoCollection<MongoWatchlistItemDocument> watchlistItems =
        database.GetCollection<MongoWatchlistItemDocument>(options.Value.WatchlistItemsCollectionName);

    public async Task<IReadOnlyList<WatchlistExportMovieModel>> GetLetterboxdMoviesAsync(
        CancellationToken cancellationToken)
    {
        FilterDefinition<MongoWatchlistItemDocument> filter =
            Builders<MongoWatchlistItemDocument>.Filter.Eq(document => document.MediaType, MediaType.Movie)
            & Builders<MongoWatchlistItemDocument>.Filter.Eq(document => document.Source, WatchlistSource.Letterboxd);

        List<MongoWatchlistItemDocument> documents = await watchlistItems
            .Find(filter)
            .ToListAsync(cancellationToken);

        return documents
            .Select(ToExportModel)
            .ToList();
    }

    public static WatchlistExportMovieModel ToExportModel(MongoWatchlistItemDocument document)
    {
        return new WatchlistExportMovieModel(
            document.SourceId,
            document.ImdbId,
            document.Title,
            document.Year,
            document.LetterboxdPath,
            document.OwnedServiceAvailability);
    }
}
```

- [ ] **Step 4: Register repository in dependency injection**

Modify `backend/src/Watchlist.Infrastructure/DependencyInjection.cs` near other repository registrations:

```csharp
services.AddSingleton<IWatchlistExportRepository, MongoWatchlistExportRepository>();
```

Place it after:

```csharp
services.AddSingleton<IWatchlistReadRepository, MongoWatchlistReadRepository>();
```

- [ ] **Step 5: Run application tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "WatchlistExportServiceTests|MongoWatchlistExportRepositoryTests"
```

Expected: all export service and Mongo export repository tests pass.

- [ ] **Step 6: Run infrastructure compile through backend solution**

Run:

```powershell
dotnet test backend\Watchlist.sln --filter WatchlistExportServiceTests
```

Expected: solution builds and the filtered export service tests pass.

- [ ] **Step 7: Commit Task 2**

Run:

```powershell
git add backend\src\Watchlist.Infrastructure\MongoWatchlistExportRepository.cs `
  backend\src\Watchlist.Infrastructure\DependencyInjection.cs `
  backend\tests\Watchlist.Application.Tests\MongoWatchlistExportRepositoryTests.cs
git commit -m "feat: read export movies from mongo"
```

---

### Task 3: API Endpoints

**Files:**
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Write failing API tests**

Append these tests to `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`:

```csharp
[Fact]
public async Task GetRadarrMovieExport_ReturnsLetterboxdProxyShapeAndExcludesOwnedVodMovies()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.GetAsync("/api/export/radarr/movies");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    JsonElement items = document.RootElement;
    items.GetArrayLength().Should().Be(1);
    JsonElement item = items[0];
    item.GetProperty("id").GetInt32().Should().Be(1297842);
    item.GetProperty("imdb_id").GetString().Should().Be("tt27613895");
    item.GetProperty("title").GetString().Should().Be("GOAT");
    item.GetProperty("release_year").GetString().Should().Be("2026");
    item.GetProperty("clean_title").GetString().Should().Be("/film/goat-2026/");
    item.GetProperty("adult").GetBoolean().Should().BeFalse();
}

[Fact]
public async Task GetSonarrTvExport_ForV1_ReturnsEmptyArray()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.GetAsync("/api/export/sonarr/tv");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    document.RootElement.GetArrayLength().Should().Be(0);
}
```

- [ ] **Step 2: Add seeded export repository to API test factory**

Modify `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`.

Add this removal near other removals:

```csharp
services.RemoveAll<IWatchlistExportRepository>();
```

Add this registration near other seeded repositories:

```csharp
services.AddSingleton<IWatchlistExportRepository, SeededWatchlistExportRepository>();
```

Add this nested class before the closing brace:

```csharp
private sealed class SeededWatchlistExportRepository : IWatchlistExportRepository
{
    public Task<IReadOnlyList<WatchlistExportMovieModel>> GetLetterboxdMoviesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WatchlistExportMovieModel> movies =
        [
            new WatchlistExportMovieModel(
                "1297842",
                "tt27613895",
                "GOAT",
                2026,
                "/film/goat-2026/",
                []),
            new WatchlistExportMovieModel(
                "4951",
                "tt0147800",
                "10 Things I Hate About You",
                1999,
                "/film/10-things-i-hate-about-you/",
                ["Amazon Prime Video"])
        ];

        return Task.FromResult(movies);
    }
}
```

- [ ] **Step 3: Run API tests to verify RED**

Run:

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "GetRadarrMovieExport|GetSonarrTvExport"
```

Expected: tests fail with `404 Not Found` because endpoints are not mapped yet.

- [ ] **Step 4: Register export service in API startup**

Modify `backend/src/Watchlist.Api/Program.cs` after:

```csharp
builder.Services.AddScoped<WatchlistQueryService>();
```

Add:

```csharp
builder.Services.AddScoped<WatchlistExportService>();
```

- [ ] **Step 5: Map export endpoints**

Modify `backend/src/Watchlist.Api/Program.cs` after the `GET /api/watchlist/{id}` endpoint and before image proxy endpoints:

```csharp
app.MapGet("/api/export/radarr/movies", async (
    WatchlistExportService exportService,
    CancellationToken cancellationToken) =>
{
    IReadOnlyList<RadarrMovieExportItemDto> items =
        await exportService.GetRadarrMoviesAsync(cancellationToken);

    return Results.Ok(items);
});

app.MapGet("/api/export/sonarr/tv", async (
    WatchlistExportService exportService,
    CancellationToken cancellationToken) =>
{
    IReadOnlyList<object> items = await exportService.GetSonarrTvAsync(cancellationToken);

    return Results.Ok(items);
});
```

- [ ] **Step 6: Run API tests to verify GREEN**

Run:

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "GetRadarrMovieExport|GetSonarrTvExport"
```

Expected: both tests pass.

- [ ] **Step 7: Add Mongo unavailable test**

Append this test to `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`:

```csharp
[Fact]
public async Task GetRadarrMovieExport_WhenMongoUnavailable_ReturnsServiceUnavailable()
{
    using MongoUnavailableApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.GetAsync("/api/export/radarr/movies");

    response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    document.RootElement.GetProperty("error").GetString().Should().Be("MongoDB is unavailable.");
}
```

- [ ] **Step 8: Run Mongo unavailable test**

Run:

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter GetRadarrMovieExport_WhenMongoUnavailable_ReturnsServiceUnavailable
```

Expected: pass through existing exception handler.

- [ ] **Step 9: Commit Task 3**

Run:

```powershell
git add backend\src\Watchlist.Api\Program.cs `
  backend\tests\Watchlist.Api.Tests\SeededApiFactory.cs `
  backend\tests\Watchlist.Api.Tests\WatchlistApiTests.cs
git commit -m "feat: expose vod-filtered export endpoints"
```

---

### Task 4: Documentation

**Files:**
- Modify: `docs/api.md`
- Modify: `docs/integrations.md`

- [ ] **Step 1: Update `docs/api.md`**

Add this section before `## GET /api/images/tmdb/{size}/{fileName}`:

```markdown
## GET /api/export/radarr/movies

Returns a Radarr/Letterboxd-compatible movie list containing Letterboxd watchlist movies that are not already available on the user's subscribed VOD services.

This endpoint uses cached MongoDB data only. It does not call Letterboxd or TMDB while handling the request.

Response:

```json
[
  {
    "id": 1297842,
    "imdb_id": "tt27613895",
    "title": "GOAT",
    "release_year": "2026",
    "clean_title": "/film/goat-2026/",
    "adult": false
  }
]
```

Filtering:

- Includes Letterboxd movie watchlist items with no cached subscribed-service availability.
- Excludes movies whose cached TMDB enrichment has `OwnedServiceAvailability` entries.
- Missing TMDB enrichment does not exclude a movie.
- Plex availability does not affect this endpoint.

## GET /api/export/sonarr/tv

Returns TV shows that are not already available on subscribed VOD services.

Version 1 reserves the endpoint and returns an empty array until TMDB TV watchlist sync and a Sonarr-compatible TV export shape are implemented.

Response:

```json
[]
```
```

- [ ] **Step 2: Update `docs/integrations.md`**

Add this section after the Letterboxd section:

```markdown
## Export Endpoints

Purpose: provide cached watchlist lists to external import tools without making those tools call Letterboxd, TMDB, Plex, or MongoDB directly.

Implemented endpoints:

- `GET /api/export/radarr/movies`: returns Radarr/Letterboxd-style movie JSON for Letterboxd watchlist movies that are not already available on subscribed VOD services.
- `GET /api/export/sonarr/tv`: returns an empty array in v1 and reserves the TV export contract for later TMDB TV watchlist work.

The movie export endpoint uses cached MongoDB fields only. It excludes a movie when TMDB enrichment has stored at least one `OwnedServiceAvailability` value. If TMDB enrichment has not run or provider data is missing, the movie remains in the export because the backend has no cached evidence that the movie is available on a subscribed VOD service.

Plex availability does not filter the Radarr export endpoint.
```

- [ ] **Step 3: Run docs diff**

Run:

```powershell
git diff -- docs\api.md docs\integrations.md
```

Expected: only the export endpoint documentation changes are shown.

- [ ] **Step 4: Commit Task 4**

Run:

```powershell
git add docs\api.md docs\integrations.md
git commit -m "docs: document vod-filtered export endpoints"
```

---

### Task 5: Full Verification

**Files:**
- No code changes expected.

- [ ] **Step 1: Run full backend test suite**

Run:

```powershell
dotnet test backend\Watchlist.sln --artifacts-path .artifacts\vod-filtered-export-endpoints
```

Expected: all backend tests pass. Record the total passed count in the final response.

- [ ] **Step 2: Inspect final diff**

Run:

```powershell
git status --short
```

Expected: only unrelated pre-existing dirty files remain, or no changes if every task committed cleanly. Do not stage unrelated dirty files.

- [ ] **Step 3: Manual local API smoke test**

Start Mongo if needed:

```powershell
docker compose up -d mongo
```

Start the backend:

```powershell
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5000
```

In a second terminal, run:

```powershell
Invoke-RestMethod http://localhost:5000/api/export/radarr/movies | Select-Object -First 5
Invoke-RestMethod http://localhost:5000/api/export/sonarr/tv
```

Expected:

- Movie endpoint returns Radarr-style objects with `id`, `imdb_id`, `title`, `release_year`, `clean_title`, and `adult`.
- TV endpoint returns an empty array.

- [ ] **Step 4: Final commit check**

Run:

```powershell
git log --oneline -5
```

Expected: recent commits include:

- `feat: add watchlist export service`
- `feat: read export movies from mongo`
- `feat: expose vod-filtered export endpoints`
- `docs: document vod-filtered export endpoints`
