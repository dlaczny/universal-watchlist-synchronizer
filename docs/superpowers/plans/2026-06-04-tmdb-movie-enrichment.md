# TMDB Movie Enrichment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add backend TMDB movie enrichment so imported Letterboxd movies gain poster/backdrop URLs, overview, release metadata, watch-provider data, owned-service availability, and VOD-release flags.

**Architecture:** Add a TMDB client behind application interfaces, then add a backend enrichment service that updates existing Letterboxd movie records in MongoDB. Keep provider/VOD fields backend-only in this slice; Android benefits immediately through existing `posterUrl`, `backdropUrl`, and `overview` fields.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, MongoDB.Driver, System.Text.Json, HttpClientFactory, xUnit, FluentAssertions, local MongoDB for repository smoke tests.

---

## File Structure

- `backend/src/Watchlist.Application/TmdbMovieDetailsDto.cs`: normalized TMDB movie details used by the enrichment service.
- `backend/src/Watchlist.Application/TmdbWatchProviderDto.cs`: normalized provider entry for one TMDB watch-provider category.
- `backend/src/Watchlist.Application/TmdbRegionWatchProvidersDto.cs`: normalized providers for one region.
- `backend/src/Watchlist.Application/TmdbMovieProviderDataDto.cs`: normalized PL/US provider data for one movie.
- `backend/src/Watchlist.Application/TmdbMovieMetadataDto.cs`: combined details and provider data.
- `backend/src/Watchlist.Application/ITmdbMovieClient.cs`: application boundary for TMDB API calls.
- `backend/src/Watchlist.Application/TmdbMovieEnrichmentResultDto.cs`: batch sync API response.
- `backend/src/Watchlist.Application/TmdbSingleMovieEnrichmentResultDto.cs`: single sync API response.
- `backend/src/Watchlist.Application/ITmdbMovieEnrichmentService.cs`: API-facing enrichment service boundary.
- `backend/src/Watchlist.Application/ITmdbMovieMetadataRepository.cs`: write/read boundary for TMDB enrichment persistence.
- `backend/src/Watchlist.Application/TmdbMovieMetadataUpdate.cs`: persistence update payload built by the enrichment service.
- `backend/src/Watchlist.Application/TmdbMovieEnrichmentService.cs`: orchestrates movie selection, TMDB lookup, provider rules, and persistence.
- `backend/src/Watchlist.Infrastructure/TmdbOptions.cs`: TMDB configuration.
- `backend/src/Watchlist.Infrastructure/TmdbMovieClient.cs`: HTTP client and JSON parser for TMDB movie details/providers/find fallback.
- `backend/src/Watchlist.Infrastructure/MongoWatchProviderDocument.cs`: Mongo provider entry document.
- `backend/src/Watchlist.Infrastructure/MongoRegionWatchProvidersDocument.cs`: Mongo provider groups for one region.
- `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`: add TMDB metadata fields.
- `backend/src/Watchlist.Infrastructure/MongoTmdbMovieMetadataRepository.cs`: Mongo implementation for enrichment persistence.
- `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`: register TMDB options/client/repository/service.
- `backend/src/Watchlist.Api/Program.cs`: add manual TMDB sync endpoints.
- `backend/src/Watchlist.Api/MongoUnavailableExceptionHandler.cs`: include TMDB dependency errors.
- `backend/src/Watchlist.Api/appsettings.json`: add non-secret TMDB defaults.
- Tests in `backend/tests/Watchlist.Application.Tests/` and `backend/tests/Watchlist.Api.Tests/`.
- Docs in `docs/api.md`, `docs/integrations.md`, and `docs/todo.md`.

---

### Task 1: TMDB Client And DTOs

**Files:**
- Create: `backend/src/Watchlist.Application/TmdbMovieDetailsDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbWatchProviderDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbRegionWatchProvidersDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbMovieProviderDataDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbMovieMetadataDto.cs`
- Create: `backend/src/Watchlist.Application/ITmdbMovieClient.cs`
- Create: `backend/src/Watchlist.Infrastructure/TmdbOptions.cs`
- Create: `backend/src/Watchlist.Infrastructure/TmdbMovieClient.cs`
- Create: `backend/tests/Watchlist.Application.Tests/TmdbMovieClientTests.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/Watchlist.Api/appsettings.json`

- [ ] Add failing client tests in `backend/tests/Watchlist.Application.Tests/TmdbMovieClientTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TmdbMovieClientTests
{
    [Fact]
    public async Task GetMovieMetadataAsync_WhenDetailsAndProvidersExist_ParsesMetadata()
    {
        Dictionary<string, string> responses = new()
        {
            ["/movie/1297842"] = """
            {
              "id": 1297842,
              "imdb_id": "tt27613895",
              "title": "GOAT",
              "original_title": "GOAT",
              "overview": "A promising athlete story.",
              "release_date": "2026-02-13",
              "poster_path": "/poster.jpg",
              "backdrop_path": "/backdrop.jpg",
              "genres": [{ "id": 18, "name": "Drama" }]
            }
            """,
            ["/movie/1297842/watch/providers"] = """
            {
              "results": {
                "PL": {
                  "flatrate": [
                    { "provider_id": 119, "provider_name": "Amazon Prime Video", "logo_path": "/prime.jpg", "display_priority": 1 }
                  ],
                  "rent": [
                    { "provider_id": 10, "provider_name": "Amazon Video", "logo_path": "/amazon.jpg", "display_priority": 2 }
                  ]
                },
                "US": {
                  "buy": [
                    { "provider_id": 2, "provider_name": "Apple TV", "logo_path": "/apple.jpg", "display_priority": 3 }
                  ]
                }
              }
            }
            """
        };
        TmdbMovieClient client = CreateClient(responses);

        TmdbMovieMetadataDto metadata = await client.GetMovieMetadataAsync(
            1297842,
            "tt27613895",
            CancellationToken.None);

        metadata.Details.Should().Be(new TmdbMovieDetailsDto(
            1297842,
            "tt27613895",
            "GOAT",
            "GOAT",
            "A promising athlete story.",
            "2026-02-13",
            "/poster.jpg",
            "/backdrop.jpg",
            "https://image.tmdb.org/t/p/w500/poster.jpg",
            "https://image.tmdb.org/t/p/w1280/backdrop.jpg",
            ["Drama"]));
        metadata.Providers.Regions.Should().ContainKey("PL");
        metadata.Providers.Regions["PL"].Flatrate.Should().ContainSingle(provider =>
            provider.ProviderName == "Amazon Prime Video"
            && provider.ProviderId == 119);
        metadata.Providers.Regions["US"].Buy.Should().ContainSingle(provider =>
            provider.ProviderName == "Apple TV");
    }

    [Fact]
    public async Task GetMovieMetadataAsync_WhenDirectMovieNotFound_UsesImdbFallback()
    {
        Dictionary<string, string> responses = new()
        {
            ["/movie/1"] = "__404__",
            ["/find/tt27613895?external_source=imdb_id"] = """
            {
              "movie_results": [
                { "id": 1297842, "title": "GOAT" }
              ]
            }
            """,
            ["/movie/1297842"] = """
            {
              "id": 1297842,
              "imdb_id": "tt27613895",
              "title": "GOAT",
              "original_title": "GOAT",
              "overview": "A promising athlete story.",
              "release_date": "2026-02-13",
              "poster_path": "/poster.jpg",
              "backdrop_path": "/backdrop.jpg",
              "genres": []
            }
            """,
            ["/movie/1297842/watch/providers"] = """{ "results": {} }"""
        };
        TmdbMovieClient client = CreateClient(responses);

        TmdbMovieMetadataDto metadata = await client.GetMovieMetadataAsync(
            1,
            "tt27613895",
            CancellationToken.None);

        metadata.Details.TmdbId.Should().Be(1297842);
    }

    [Fact]
    public async Task GetMovieMetadataAsync_WhenMovieMissingAndNoFallback_ThrowsTmdbMovieNotFoundException()
    {
        TmdbMovieClient client = CreateClient(new Dictionary<string, string>
        {
            ["/movie/1"] = "__404__"
        });

        Func<Task> action = () => client.GetMovieMetadataAsync(1, null, CancellationToken.None);

        await action.Should().ThrowAsync<TmdbMovieNotFoundException>();
    }

    [Fact]
    public async Task GetMovieMetadataAsync_WhenUnauthorized_ThrowsTmdbUnavailableException()
    {
        TmdbMovieClient client = CreateClient(new Dictionary<string, string>
        {
            ["/movie/1297842"] = "__401__"
        });

        Func<Task> action = () => client.GetMovieMetadataAsync(
            1297842,
            "tt27613895",
            CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetMovieMetadataAsync_WhenAccessTokenMissing_ThrowsTmdbUnavailableException()
    {
        TmdbMovieClient client = CreateClient(
            new Dictionary<string, string>(),
            accessToken: "");

        Func<Task> action = () => client.GetMovieMetadataAsync(
            1297842,
            "tt27613895",
            CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    private static TmdbMovieClient CreateClient(
        IReadOnlyDictionary<string, string> responses,
        string accessToken = "token")
    {
        HttpClient httpClient = new(new StaticTmdbHandler(responses))
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = new()
        {
            AccessToken = accessToken,
            BaseUrl = "https://api.themoviedb.org/3",
            ImageBaseUrl = "https://image.tmdb.org/t/p"
        };

        return new TmdbMovieClient(httpClient, Options.Create(options));
    }

    private sealed class StaticTmdbHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.Authorization.Should().NotBeNull();
            string key = request.RequestUri!.PathAndQuery.Replace("/3", string.Empty, StringComparison.Ordinal);
            if (!responses.TryGetValue(key, out string? content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (content == "__404__")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (content == "__401__")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
```

- [ ] Run focused tests and verify compile failure:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter TmdbMovieClientTests
```

Expected: compile failure mentioning missing `TmdbMovieClient` or DTO types.

- [ ] Add application DTOs:

```csharp
namespace Watchlist.Application;

public sealed record TmdbMovieDetailsDto(
    int TmdbId,
    string? ImdbId,
    string Title,
    string OriginalTitle,
    string? Overview,
    string? ReleaseDate,
    string? PosterPath,
    string? BackdropPath,
    string? PosterUrl,
    string? BackdropUrl,
    IReadOnlyList<string> Genres);
```

```csharp
namespace Watchlist.Application;

public sealed record TmdbWatchProviderDto(
    int ProviderId,
    string ProviderName,
    string? LogoPath,
    int DisplayPriority);
```

```csharp
namespace Watchlist.Application;

public sealed record TmdbRegionWatchProvidersDto(
    IReadOnlyList<TmdbWatchProviderDto> Flatrate,
    IReadOnlyList<TmdbWatchProviderDto> Rent,
    IReadOnlyList<TmdbWatchProviderDto> Buy);
```

```csharp
namespace Watchlist.Application;

public sealed record TmdbMovieProviderDataDto(
    IReadOnlyDictionary<string, TmdbRegionWatchProvidersDto> Regions);
```

```csharp
namespace Watchlist.Application;

public sealed record TmdbMovieMetadataDto(
    TmdbMovieDetailsDto Details,
    TmdbMovieProviderDataDto Providers);
```

- [ ] Add `backend/src/Watchlist.Application/ITmdbMovieClient.cs`:

```csharp
namespace Watchlist.Application;

public interface ITmdbMovieClient
{
    Task<TmdbMovieMetadataDto> GetMovieMetadataAsync(
        int candidateTmdbId,
        string? imdbId,
        CancellationToken cancellationToken);
}
```

- [ ] Add `backend/src/Watchlist.Infrastructure/TmdbOptions.cs`:

```csharp
namespace Watchlist.Infrastructure;

public sealed class TmdbOptions
{
    public const string SectionName = "Tmdb";

    public string AccessToken { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.themoviedb.org/3";

    public string ImageBaseUrl { get; init; } = "https://image.tmdb.org/t/p";
}
```

- [ ] Add `backend/src/Watchlist.Infrastructure/TmdbMovieClient.cs`. Use `JsonPropertyName` for snake_case fields, set `Authorization: Bearer`, fetch details, fetch providers, build full poster/backdrop URLs from `TmdbOptions.ImageBaseUrl`, and fallback through `/find/{imdbId}?external_source=imdb_id` when direct details return 404. Define:

```csharp
public sealed class TmdbUnavailableException : Exception
public sealed class TmdbMovieNotFoundException : Exception
public sealed class TmdbParseException : Exception
```

Map:

- `401`, `403`, `429`, 5xx, `HttpRequestException`, and timeout to `TmdbUnavailableException`.
- missing or blank `Tmdb:AccessToken` to `TmdbUnavailableException`.
- malformed JSON to `TmdbParseException`.
- direct 404 with no fallback result to `TmdbMovieNotFoundException`.

- [ ] Register options and client in `DependencyInjection.cs`:

```csharp
services.AddOptions<TmdbOptions>()
    .Bind(configuration.GetSection(TmdbOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Tmdb:BaseUrl must be absolute.")
    .Validate(options => Uri.TryCreate(options.ImageBaseUrl, UriKind.Absolute, out _), "Tmdb:ImageBaseUrl must be absolute.");
services.AddHttpClient<ITmdbMovieClient, TmdbMovieClient>((serviceProvider, httpClient) =>
{
    TmdbOptions options = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.BaseUrl);
});
```

- [ ] Add non-secret defaults to `appsettings.json`:

```json
"Tmdb": {
  "AccessToken": "",
  "BaseUrl": "https://api.themoviedb.org/3",
  "ImageBaseUrl": "https://image.tmdb.org/t/p"
}
```

- [ ] Run tests:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter TmdbMovieClientTests
```

Expected: all `TmdbMovieClientTests` pass.

- [ ] Commit:

```powershell
git add backend/src/Watchlist.Application/TmdbMovieDetailsDto.cs backend/src/Watchlist.Application/TmdbWatchProviderDto.cs backend/src/Watchlist.Application/TmdbRegionWatchProvidersDto.cs backend/src/Watchlist.Application/TmdbMovieProviderDataDto.cs backend/src/Watchlist.Application/TmdbMovieMetadataDto.cs backend/src/Watchlist.Application/ITmdbMovieClient.cs backend/src/Watchlist.Infrastructure/TmdbOptions.cs backend/src/Watchlist.Infrastructure/TmdbMovieClient.cs backend/src/Watchlist.Infrastructure/DependencyInjection.cs backend/src/Watchlist.Api/appsettings.json backend/tests/Watchlist.Application.Tests/TmdbMovieClientTests.cs
git commit -m "feat: add tmdb movie client"
```

### Task 2: Mongo Metadata Storage

**Files:**
- Create: `backend/src/Watchlist.Infrastructure/MongoWatchProviderDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoRegionWatchProvidersDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`
- Modify: `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`

- [ ] Add failing document mapping test:

```csharp
[Fact]
public void ToDomain_WhenDocumentHasTmdbMetadata_KeepsExistingDisplayFields()
{
    MongoWatchlistItemDocument document = new()
    {
        Id = "movie-letterboxd-1297842",
        MediaType = MediaType.Movie,
        Source = WatchlistSource.Letterboxd,
        SourceId = "1297842",
        Title = "GOAT",
        Year = 2026,
        Overview = "A promising athlete story.",
        PosterUrl = "https://image.tmdb.org/t/p/w500/poster.jpg",
        BackdropUrl = "https://image.tmdb.org/t/p/w1280/backdrop.jpg",
        ReleaseStatus = ReleaseStatus.Unreleased,
        AvailabilityStatus = AvailabilityStatus.Unreleased,
        AddedAt = DateTimeOffset.Parse("2026-06-04T12:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-06-04T12:00:00Z"),
        TmdbId = 1297842,
        TmdbTitle = "GOAT",
        OriginalTitle = "GOAT",
        ReleaseDate = "2026-02-13",
        Genres = ["Drama"],
        PosterPath = "/poster.jpg",
        BackdropPath = "/backdrop.jpg",
        ReleasedOnVod = true,
        VodRegions = ["PL", "US"],
        OwnedServiceAvailability = ["Amazon Prime Video"],
        TmdbMetadataStatus = "completed"
    };

    WatchlistItem item = document.ToDomain();

    item.Overview.Should().Be("A promising athlete story.");
    item.PosterUrl.Should().Be("https://image.tmdb.org/t/p/w500/poster.jpg");
    item.BackdropUrl.Should().Be("https://image.tmdb.org/t/p/w1280/backdrop.jpg");
}
```

- [ ] Run focused tests and verify failure due to missing fields:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter MongoWatchlistItemDocumentTests
```

- [ ] Add Mongo provider documents:

```csharp
namespace Watchlist.Infrastructure;

public sealed class MongoWatchProviderDocument
{
    public int ProviderId { get; init; }

    public string ProviderName { get; init; } = string.Empty;

    public string? LogoPath { get; init; }

    public int DisplayPriority { get; init; }
}
```

```csharp
namespace Watchlist.Infrastructure;

public sealed class MongoRegionWatchProvidersDocument
{
    public IReadOnlyList<MongoWatchProviderDocument> Flatrate { get; init; } = [];

    public IReadOnlyList<MongoWatchProviderDocument> Rent { get; init; } = [];

    public IReadOnlyList<MongoWatchProviderDocument> Buy { get; init; } = [];
}
```

- [ ] Add fields to `MongoWatchlistItemDocument`:

```csharp
public int? TmdbId { get; init; }
public string? TmdbTitle { get; init; }
public string? OriginalTitle { get; init; }
public string? ReleaseDate { get; init; }
public IReadOnlyList<string> Genres { get; init; } = [];
public string? PosterPath { get; init; }
public string? BackdropPath { get; init; }
public IReadOnlyDictionary<string, MongoRegionWatchProvidersDocument> WatchProviders { get; init; }
    = new Dictionary<string, MongoRegionWatchProvidersDocument>();
public IReadOnlyList<string> OwnedServiceAvailability { get; init; } = [];
public bool ReleasedOnVod { get; init; }
public IReadOnlyList<string> VodRegions { get; init; } = [];
public DateTimeOffset? TmdbMetadataUpdatedAt { get; init; }
public string TmdbMetadataStatus { get; init; } = "not_synced";
public string? TmdbMetadataError { get; init; }
```

- [ ] Keep `ToDomain()` behavior unchanged except it uses the already-enriched display fields. Existing Android DTOs must not expose provider/VOD fields in this task.

- [ ] Run tests:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter MongoWatchlistItemDocumentTests
```

Expected: all document tests pass.

- [ ] Commit:

```powershell
git add backend/src/Watchlist.Infrastructure/MongoWatchProviderDocument.cs backend/src/Watchlist.Infrastructure/MongoRegionWatchProvidersDocument.cs backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs
git commit -m "feat: add tmdb metadata fields"
```

### Task 3: TMDB Enrichment Service And Repository

**Files:**
- Create: `backend/src/Watchlist.Application/TmdbMovieEnrichmentResultDto.cs`
- Create: `backend/src/Watchlist.Application/TmdbSingleMovieEnrichmentResultDto.cs`
- Create: `backend/src/Watchlist.Application/ITmdbMovieEnrichmentService.cs`
- Create: `backend/src/Watchlist.Application/ITmdbMovieMetadataRepository.cs`
- Create: `backend/src/Watchlist.Application/TmdbMovieMetadataUpdate.cs`
- Create: `backend/src/Watchlist.Application/TmdbMovieEnrichmentService.cs`
- Create: `backend/tests/Watchlist.Application.Tests/TmdbMovieEnrichmentServiceTests.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTmdbMovieMetadataRepository.cs`
- Create: `backend/tests/Watchlist.Application.Tests/MongoTmdbMovieMetadataRepositoryTests.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`

- [ ] Add service tests proving:
  - batch enriches Letterboxd movies and ignores TV/TMDB records;
  - single enrich returns not found for missing/non-Letterboxd ids;
  - owned service availability only uses PL flatrate;
  - rent/buy only affects VOD;
  - per-movie not-found/failure is counted and recorded.

Use fakes with this shape:

```csharp
private sealed class FakeTmdbMovieClient : ITmdbMovieClient
{
    public Dictionary<int, TmdbMovieMetadataDto> MetadataByTmdbId { get; } = [];
    public HashSet<int> NotFoundIds { get; } = [];

    public Task<TmdbMovieMetadataDto> GetMovieMetadataAsync(
        int candidateTmdbId,
        string? imdbId,
        CancellationToken cancellationToken)
    {
        if (NotFoundIds.Contains(candidateTmdbId))
        {
            throw new TmdbMovieNotFoundException($"Movie {candidateTmdbId} was not found.");
        }

        return Task.FromResult(MetadataByTmdbId[candidateTmdbId]);
    }
}
```

```csharp
private sealed class FakeTmdbMovieMetadataRepository : ITmdbMovieMetadataRepository
{
    public List<WatchlistItemWriteModel> ExistingMovies { get; } = [];
    public List<TmdbMovieMetadataUpdate> Updates { get; } = [];

    public Task<IReadOnlyList<WatchlistItemWriteModel>> GetLetterboxdMoviesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<WatchlistItemWriteModel>>(ExistingMovies);
    }

    public Task<WatchlistItemWriteModel?> GetLetterboxdMovieAsync(string id, CancellationToken cancellationToken)
    {
        WatchlistItemWriteModel? item = ExistingMovies.FirstOrDefault(movie => movie.Item.Id == id);
        return Task.FromResult(item);
    }

    public Task ApplyTmdbMetadataAsync(
        string id,
        TmdbMovieMetadataUpdate update,
        CancellationToken cancellationToken)
    {
        Updates.Add(update);
        return Task.CompletedTask;
    }
}
```

- [ ] Define application contracts:

```csharp
namespace Watchlist.Application;

public sealed record TmdbMovieEnrichmentResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ItemsMatched,
    int ItemsEnriched,
    int ItemsNotFound,
    int ItemsFailed);
```

```csharp
namespace Watchlist.Application;

public sealed record TmdbSingleMovieEnrichmentResultDto(
    string Status,
    string Id,
    int? TmdbId);
```

```csharp
namespace Watchlist.Application;

public interface ITmdbMovieEnrichmentService
{
    Task<TmdbMovieEnrichmentResultDto> SyncMoviesAsync(CancellationToken cancellationToken);

    Task<TmdbSingleMovieEnrichmentResultDto?> SyncMovieAsync(string id, CancellationToken cancellationToken);
}
```

```csharp
namespace Watchlist.Application;

public interface ITmdbMovieMetadataRepository
{
    Task<IReadOnlyList<WatchlistItemWriteModel>> GetLetterboxdMoviesAsync(CancellationToken cancellationToken);

    Task<WatchlistItemWriteModel?> GetLetterboxdMovieAsync(string id, CancellationToken cancellationToken);

    Task ApplyTmdbMetadataAsync(
        string id,
        TmdbMovieMetadataUpdate update,
        CancellationToken cancellationToken);
}
```

```csharp
namespace Watchlist.Application;

public sealed record TmdbMovieMetadataUpdate(
    int? TmdbId,
    string? ImdbId,
    string? TmdbTitle,
    string? OriginalTitle,
    string? Overview,
    string? ReleaseDate,
    IReadOnlyList<string> Genres,
    string? PosterPath,
    string? BackdropPath,
    string? PosterUrl,
    string? BackdropUrl,
    TmdbMovieProviderDataDto Providers,
    IReadOnlyList<string> OwnedServiceAvailability,
    bool ReleasedOnVod,
    IReadOnlyList<string> VodRegions,
    DateTimeOffset UpdatedAt,
    string MetadataStatus,
    string? MetadataError);
```

- [ ] Implement `TmdbMovieEnrichmentService`:
  - parse `WatchlistItemWriteModel.Item.SourceId` as candidate TMDB id;
  - call `ITmdbMovieClient.GetMovieMetadataAsync(candidateTmdbId, writeModel.ImdbId, cancellationToken)`;
  - use `metadata.Details.PosterUrl` and `metadata.Details.BackdropUrl` from the TMDB client;
  - owned services: PL flatrate names matching `max`, `hbo max`, `skyshowtime`, `crunchyroll`, `amazon prime video`, or `prime video` case-insensitively;
  - VOD regions: `PL`/`US` when flatrate/rent/buy has at least one entry;
  - on `TmdbMovieNotFoundException`, apply update with `MetadataStatus = "not_found"`;
  - on other `TmdbUnavailableException` in batch, count failed and apply `MetadataStatus = "failed"`;
  - for single movie, let `TmdbUnavailableException` bubble so API can return 503.

- [ ] Implement `MongoTmdbMovieMetadataRepository`:
  - query documents where `MediaType == Movie` and `Source == Letterboxd`;
  - return `WatchlistItemWriteModel(document.ToDomain(), document.ImdbId, document.LetterboxdPath)`;
  - update only TMDB/display metadata fields using `Builders<MongoWatchlistItemDocument>.Update`;
  - do not overwrite `AvailabilityStatus`, `AddedAt`, `Source`, or `SourceId`.

- [ ] Add repository integration test using local Mongo:
  - insert Letterboxd movie, TMDB movie, and TV show;
  - `GetLetterboxdMoviesAsync` returns only Letterboxd movie;
  - `ApplyTmdbMetadataAsync` updates poster/overview/provider/VOD fields;
  - non-Letterboxd records remain unchanged.

- [ ] Register repository and service:

```csharp
services.AddSingleton<ITmdbMovieMetadataRepository, MongoTmdbMovieMetadataRepository>();
services.AddScoped<ITmdbMovieEnrichmentService, TmdbMovieEnrichmentService>();
```

- [ ] Run tests:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "TmdbMovieEnrichmentServiceTests|MongoTmdbMovieMetadataRepositoryTests"
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj
```

Expected: all application tests pass.

- [ ] Commit:

```powershell
git add backend/src/Watchlist.Application/TmdbMovieEnrichmentResultDto.cs backend/src/Watchlist.Application/TmdbSingleMovieEnrichmentResultDto.cs backend/src/Watchlist.Application/ITmdbMovieEnrichmentService.cs backend/src/Watchlist.Application/ITmdbMovieMetadataRepository.cs backend/src/Watchlist.Application/TmdbMovieMetadataUpdate.cs backend/src/Watchlist.Application/TmdbMovieEnrichmentService.cs backend/src/Watchlist.Infrastructure/MongoTmdbMovieMetadataRepository.cs backend/src/Watchlist.Infrastructure/DependencyInjection.cs backend/tests/Watchlist.Application.Tests/TmdbMovieEnrichmentServiceTests.cs backend/tests/Watchlist.Application.Tests/MongoTmdbMovieMetadataRepositoryTests.cs
git commit -m "feat: enrich letterboxd movies from tmdb"
```

### Task 4: TMDB Sync API Endpoints

**Files:**
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Modify: `backend/src/Watchlist.Api/MongoUnavailableExceptionHandler.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`

- [ ] Add API tests:

```csharp
[Fact]
public async Task PostTmdbMovieSync_ReturnsBatchResult()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.PostAsync("/api/sync/tmdb/movies", null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    document.RootElement.GetProperty("status").GetString().Should().Be("completed");
    document.RootElement.GetProperty("itemsMatched").GetInt32().Should().Be(2);
    document.RootElement.GetProperty("itemsEnriched").GetInt32().Should().Be(2);
}

[Fact]
public async Task PostTmdbSingleMovieSync_ReturnsSingleResult()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.PostAsync("/api/sync/tmdb/movies/movie-letterboxd-1297842", null);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    document.RootElement.GetProperty("status").GetString().Should().Be("completed");
    document.RootElement.GetProperty("tmdbId").GetInt32().Should().Be(1297842);
}

[Fact]
public async Task PostTmdbSingleMovieSync_WhenMissing_ReturnsNotFound()
{
    using SeededApiFactory factory = new(tmdbSingleResult: null);
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.PostAsync("/api/sync/tmdb/movies/missing", null);

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
}
```

- [ ] Update `SeededApiFactory` to override `ITmdbMovieEnrichmentService` with deterministic fake results.

- [ ] Add endpoints to `Program.cs`:

```csharp
app.MapPost("/api/sync/tmdb/movies", async (
    ITmdbMovieEnrichmentService enrichmentService,
    CancellationToken cancellationToken) =>
{
    TmdbMovieEnrichmentResultDto result = await enrichmentService.SyncMoviesAsync(cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/sync/tmdb/movies/{id}", async (
    string id,
    ITmdbMovieEnrichmentService enrichmentService,
    CancellationToken cancellationToken) =>
{
    TmdbSingleMovieEnrichmentResultDto? result = await enrichmentService.SyncMovieAsync(id, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});
```

- [ ] Update exception handler:

```csharp
if (exception is TmdbUnavailableException)
{
    httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    await httpContext.Response.WriteAsJsonAsync(
        new { error = "TMDB is unavailable." },
        cancellationToken);
    return true;
}
```

- [ ] Run API tests:

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "PostTmdb|PostLetterboxdSync|GetSyncStatus" --artifacts-path .artifacts\task4-api-tests
```

Expected: focused API tests pass.

- [ ] Commit:

```powershell
git add backend/src/Watchlist.Api/Program.cs backend/src/Watchlist.Api/MongoUnavailableExceptionHandler.cs backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs
git commit -m "feat: expose tmdb movie sync endpoints"
```

### Task 5: Documentation And Smoke Verification

**Files:**
- Modify: `docs/api.md`
- Modify: `docs/integrations.md`
- Modify: `docs/todo.md`

- [ ] Update `docs/api.md` with `POST /api/sync/tmdb/movies` and `POST /api/sync/tmdb/movies/{id}` response examples and `503 { "error": "TMDB is unavailable." }`.

- [ ] Update `docs/integrations.md` TMDB section with:
  - `TMDB__AccessToken`;
  - details endpoint;
  - watch providers endpoint;
  - direct SourceId-as-TMDB-id lookup;
  - IMDb fallback;
  - PL/US provider storage;
  - VOD release rules.

- [ ] Update `docs/todo.md`:
  - mark TMDB movie metadata enrichment complete;
  - keep Android provider/VOD badges open;
  - keep image-byte caching open;
  - keep provider-id refinement open.

- [ ] Run full backend tests:

```powershell
dotnet test backend\Watchlist.sln --artifacts-path .artifacts\tmdb-full-tests
```

Expected: all application and API tests pass.

- [ ] Run Android smoke build:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest :app:assembleDebug --no-daemon
```

Expected: `BUILD SUCCESSFUL`.

- [ ] Run live smoke. Start Mongo and backend on a free port:

```powershell
docker start watchlist-mongo
$env:TMDB__AccessToken='<local token>'
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5013
```

In a second terminal:

```powershell
Invoke-RestMethod -Method Post 'http://localhost:5013/api/sync/letterboxd'
Invoke-RestMethod -Method Post 'http://localhost:5013/api/sync/tmdb/movies/movie-letterboxd-1297842'
Invoke-RestMethod 'http://localhost:5013/api/watchlist/movie-letterboxd-1297842'
Invoke-RestMethod -Method Post 'http://localhost:5013/api/sync/tmdb/movies'
```

Expected:

- single sync returns `completed` and `tmdbId = 1297842`;
- watchlist item has non-null `posterUrl`, `backdropUrl`, or `overview`;
- batch sync returns `completed` or `partial` with non-zero `itemsMatched`.

- [ ] Clean generated `.artifacts` folders and run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; only intended doc files before commit.

- [ ] Commit:

```powershell
git add docs/api.md docs/integrations.md docs/todo.md
git commit -m "docs: document tmdb movie enrichment"
```

## Self-Review Checklist

- Spec coverage:
  - TMDB client/config covered by Task 1.
  - Mongo storage fields covered by Task 2.
  - Enrichment orchestration and provider/VOD rules covered by Task 3.
  - Manual endpoints and dependency errors covered by Task 4.
  - Docs and live smoke covered by Task 5.
- Scope:
  - Android provider/VOD badges remain out of scope.
  - Image-byte caching remains out of scope.
  - TV sorting remains a separate UI follow-up.
- Known implementation caution:
  - Do not commit TMDB credentials.
  - Keep provider/VOD fields backend-only in this slice.
  - Use isolated test artifacts if the local running API process locks default build outputs.
