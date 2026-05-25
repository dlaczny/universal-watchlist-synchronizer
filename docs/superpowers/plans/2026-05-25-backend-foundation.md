# Backend Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first .NET backend foundation for the watchlist app: domain contracts, seeded read model, read-only API endpoints, and MongoDB-ready persistence boundaries.

**Architecture:** Create a .NET solution under `backend/` with API, Application, Domain, Infrastructure, and test projects. The first slice uses an in-memory seeded repository so the API contract is usable before Letterboxd, TMDB, Plex, and MongoDB integrations are implemented. Interfaces and DTOs are shaped so MongoDB and sync jobs can replace the seed repository without changing Android-facing endpoints.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, xUnit, FluentAssertions, Microsoft.AspNetCore.Mvc.Testing, MongoDB driver added only when persistence implementation begins.

---

## Scope Notes

This plan intentionally covers only the backend foundation. Android TV and live sync integrations should get separate plans after this API contract is committed.

## File Structure

- Create `backend/Watchlist.sln`: solution file.
- Create `backend/src/Watchlist.Api/`: ASP.NET Core API host and endpoint mapping.
- Create `backend/src/Watchlist.Application/`: query services, DTOs, and repository interfaces.
- Create `backend/src/Watchlist.Domain/`: domain records and enums.
- Create `backend/src/Watchlist.Infrastructure/`: seeded repository and later MongoDB/integration adapters.
- Create `backend/tests/Watchlist.Api.Tests/`: endpoint integration tests.
- Create `backend/tests/Watchlist.Application.Tests/`: query service unit tests.
- Modify `docs/architecture.md`: link the implemented endpoint contract after tests pass.

## Task 1: Scaffold Solution And Projects

**Files:**
- Create: `backend/Watchlist.sln`
- Create: `backend/src/Watchlist.Api/Watchlist.Api.csproj`
- Create: `backend/src/Watchlist.Application/Watchlist.Application.csproj`
- Create: `backend/src/Watchlist.Domain/Watchlist.Domain.csproj`
- Create: `backend/src/Watchlist.Infrastructure/Watchlist.Infrastructure.csproj`
- Create: `backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj`
- Create: `backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj`

- [ ] **Step 1: Create the solution**

Run:

```powershell
New-Item -ItemType Directory -Force -Path backend | Out-Null
dotnet new sln -n Watchlist -o backend
```

Expected: `backend/Watchlist.sln` exists.

- [ ] **Step 2: Create projects**

Run:

```powershell
dotnet new web -n Watchlist.Api -o backend/src/Watchlist.Api
dotnet new classlib -n Watchlist.Application -o backend/src/Watchlist.Application
dotnet new classlib -n Watchlist.Domain -o backend/src/Watchlist.Domain
dotnet new classlib -n Watchlist.Infrastructure -o backend/src/Watchlist.Infrastructure
dotnet new xunit -n Watchlist.Api.Tests -o backend/tests/Watchlist.Api.Tests
dotnet new xunit -n Watchlist.Application.Tests -o backend/tests/Watchlist.Application.Tests
```

Expected: each project directory contains a `.csproj` file.

- [ ] **Step 3: Add projects to solution**

Run:

```powershell
dotnet sln backend/Watchlist.sln add backend/src/Watchlist.Api/Watchlist.Api.csproj
dotnet sln backend/Watchlist.sln add backend/src/Watchlist.Application/Watchlist.Application.csproj
dotnet sln backend/Watchlist.sln add backend/src/Watchlist.Domain/Watchlist.Domain.csproj
dotnet sln backend/Watchlist.sln add backend/src/Watchlist.Infrastructure/Watchlist.Infrastructure.csproj
dotnet sln backend/Watchlist.sln add backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj
dotnet sln backend/Watchlist.sln add backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj
```

Expected: `dotnet sln backend/Watchlist.sln list` lists six projects.

- [ ] **Step 4: Add project references**

Run:

```powershell
dotnet add backend/src/Watchlist.Application/Watchlist.Application.csproj reference backend/src/Watchlist.Domain/Watchlist.Domain.csproj
dotnet add backend/src/Watchlist.Infrastructure/Watchlist.Infrastructure.csproj reference backend/src/Watchlist.Application/Watchlist.Application.csproj
dotnet add backend/src/Watchlist.Infrastructure/Watchlist.Infrastructure.csproj reference backend/src/Watchlist.Domain/Watchlist.Domain.csproj
dotnet add backend/src/Watchlist.Api/Watchlist.Api.csproj reference backend/src/Watchlist.Application/Watchlist.Application.csproj
dotnet add backend/src/Watchlist.Api/Watchlist.Api.csproj reference backend/src/Watchlist.Infrastructure/Watchlist.Infrastructure.csproj
dotnet add backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj reference backend/src/Watchlist.Application/Watchlist.Application.csproj
dotnet add backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj reference backend/src/Watchlist.Domain/Watchlist.Domain.csproj
dotnet add backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj reference backend/src/Watchlist.Api/Watchlist.Api.csproj
```

Expected: each `dotnet add reference` command reports the reference was added.

- [ ] **Step 5: Add test packages**

Run:

```powershell
dotnet add backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj package FluentAssertions
dotnet add backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj package FluentAssertions
dotnet add backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
```

Expected: package restore succeeds.

- [ ] **Step 6: Remove template files**

Delete:

```text
backend/src/Watchlist.Application/Class1.cs
backend/src/Watchlist.Domain/Class1.cs
backend/src/Watchlist.Infrastructure/Class1.cs
backend/tests/Watchlist.Application.Tests/UnitTest1.cs
backend/tests/Watchlist.Api.Tests/UnitTest1.cs
```

- [ ] **Step 7: Build**

Run:

```powershell
dotnet build backend/Watchlist.sln
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 8: Commit**

Run:

```powershell
git add backend
git commit -m "chore: scaffold backend solution"
```

## Task 2: Add Domain Model

**Files:**
- Create: `backend/src/Watchlist.Domain/WatchlistItem.cs`
- Create: `backend/src/Watchlist.Domain/MediaType.cs`
- Create: `backend/src/Watchlist.Domain/WatchlistSource.cs`
- Create: `backend/src/Watchlist.Domain/ReleaseStatus.cs`
- Create: `backend/src/Watchlist.Domain/AvailabilityStatus.cs`

- [ ] **Step 1: Write domain enums**

Create `backend/src/Watchlist.Domain/MediaType.cs`:

```csharp
namespace Watchlist.Domain;

public enum MediaType
{
    Movie,
    TvShow
}
```

Create `backend/src/Watchlist.Domain/WatchlistSource.cs`:

```csharp
namespace Watchlist.Domain;

public enum WatchlistSource
{
    Letterboxd,
    Tmdb
}
```

Create `backend/src/Watchlist.Domain/ReleaseStatus.cs`:

```csharp
namespace Watchlist.Domain;

public enum ReleaseStatus
{
    Released,
    Unreleased,
    Unknown
}
```

Create `backend/src/Watchlist.Domain/AvailabilityStatus.cs`:

```csharp
namespace Watchlist.Domain;

public enum AvailabilityStatus
{
    AvailableOnPlex,
    NotOnPlex,
    Unreleased,
    UnknownMatch
}
```

- [ ] **Step 2: Write watchlist item record**

Create `backend/src/Watchlist.Domain/WatchlistItem.cs`:

```csharp
namespace Watchlist.Domain;

public sealed record WatchlistItem(
    string Id,
    MediaType MediaType,
    WatchlistSource Source,
    string SourceId,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    ReleaseStatus ReleaseStatus,
    AvailabilityStatus AvailabilityStatus,
    DateTimeOffset UpdatedAt);
```

- [ ] **Step 3: Build**

Run:

```powershell
dotnet build backend/Watchlist.sln
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 4: Commit**

Run:

```powershell
git add backend/src/Watchlist.Domain
git commit -m "feat: add watchlist domain model"
```

## Task 3: Add Application Query Service With Tests

**Files:**
- Create: `backend/src/Watchlist.Application/WatchlistFilter.cs`
- Create: `backend/src/Watchlist.Application/WatchlistItemDto.cs`
- Create: `backend/src/Watchlist.Application/IWatchlistReadRepository.cs`
- Create: `backend/src/Watchlist.Application/WatchlistQueryService.cs`
- Create: `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`

- [ ] **Step 1: Write failing query service tests**

Create `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`:

```csharp
using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class WatchlistQueryServiceTests
{
    [Fact]
    public async Task GetItemsAsync_WhenMediaTypeIsMovie_ReturnsOnlyMovies()
    {
        StaticWatchlistReadRepository repository = new([
            Item("movie-1", MediaType.Movie, AvailabilityStatus.AvailableOnPlex),
            Item("show-1", MediaType.TvShow, AvailabilityStatus.AvailableOnPlex)
        ]);
        WatchlistQueryService service = new(repository);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(MediaType.Movie, WatchlistFilter.All, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("movie-1");
    }

    [Fact]
    public async Task GetItemsAsync_WhenFilterIsAvailable_ReturnsOnlyAvailableOnPlex()
    {
        StaticWatchlistReadRepository repository = new([
            Item("movie-1", MediaType.Movie, AvailabilityStatus.AvailableOnPlex),
            Item("movie-2", MediaType.Movie, AvailabilityStatus.NotOnPlex),
            Item("movie-3", MediaType.Movie, AvailabilityStatus.Unreleased),
            Item("movie-4", MediaType.Movie, AvailabilityStatus.UnknownMatch)
        ]);
        WatchlistQueryService service = new(repository);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(MediaType.Movie, WatchlistFilter.Available, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("movie-1");
        result[0].AvailabilityStatus.Should().Be("available_on_plex");
    }

    [Fact]
    public async Task GetItemAsync_WhenItemExists_ReturnsItem()
    {
        StaticWatchlistReadRepository repository = new([
            Item("movie-1", MediaType.Movie, AvailabilityStatus.AvailableOnPlex)
        ]);
        WatchlistQueryService service = new(repository);

        WatchlistItemDto? result = await service.GetItemAsync("movie-1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Title movie-1");
    }

    private static WatchlistItem Item(string id, MediaType mediaType, AvailabilityStatus availabilityStatus)
    {
        return new WatchlistItem(
            id,
            mediaType,
            mediaType == MediaType.Movie ? WatchlistSource.Letterboxd : WatchlistSource.Tmdb,
            $"{id}-source",
            $"Title {id}",
            2024,
            $"Overview {id}",
            $"https://image.example/{id}/poster.jpg",
            $"https://image.example/{id}/backdrop.jpg",
            availabilityStatus == AvailabilityStatus.Unreleased ? ReleaseStatus.Unreleased : ReleaseStatus.Released,
            availabilityStatus,
            DateTimeOffset.Parse("2026-05-25T10:00:00+02:00"));
    }

    private sealed class StaticWatchlistReadRepository(IReadOnlyList<WatchlistItem> items) : IWatchlistReadRepository
    {
        public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(items);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj
```

Expected: compile fails because `WatchlistQueryService`, `WatchlistFilter`, `WatchlistItemDto`, and `IWatchlistReadRepository` do not exist.

- [ ] **Step 3: Add application types**

Create `backend/src/Watchlist.Application/WatchlistFilter.cs`:

```csharp
namespace Watchlist.Application;

public enum WatchlistFilter
{
    All,
    Available
}
```

Create `backend/src/Watchlist.Application/IWatchlistReadRepository.cs`:

```csharp
using Watchlist.Domain;

namespace Watchlist.Application;

public interface IWatchlistReadRepository
{
    Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken);
}
```

Create `backend/src/Watchlist.Application/WatchlistItemDto.cs`:

```csharp
namespace Watchlist.Application;

public sealed record WatchlistItemDto(
    string Id,
    string MediaType,
    string Source,
    string SourceId,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    string ReleaseStatus,
    string AvailabilityStatus,
    DateTimeOffset UpdatedAt);
```

Create `backend/src/Watchlist.Application/WatchlistQueryService.cs`:

```csharp
using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class WatchlistQueryService(IWatchlistReadRepository repository)
{
    public async Task<IReadOnlyList<WatchlistItemDto>> GetItemsAsync(
        MediaType mediaType,
        WatchlistFilter filter,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);

        IEnumerable<WatchlistItem> filteredItems = items.Where(item => item.MediaType == mediaType);

        if (filter == WatchlistFilter.Available)
        {
            filteredItems = filteredItems.Where(item => item.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex);
        }

        return filteredItems
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto)
            .ToList();
    }

    public async Task<WatchlistItemDto?> GetItemAsync(string id, CancellationToken cancellationToken)
    {
        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);
        WatchlistItem? item = items.SingleOrDefault(candidate => candidate.Id == id);

        return item is null ? null : ToDto(item);
    }

    private static WatchlistItemDto ToDto(WatchlistItem item)
    {
        return new WatchlistItemDto(
            item.Id,
            ToSnakeCase(item.MediaType),
            ToSnakeCase(item.Source),
            item.SourceId,
            item.Title,
            item.Year,
            item.Overview,
            item.PosterUrl,
            item.BackdropUrl,
            ToSnakeCase(item.ReleaseStatus),
            ToSnakeCase(item.AvailabilityStatus),
            item.UpdatedAt);
    }

    private static string ToSnakeCase(MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.Movie => "movie",
            MediaType.TvShow => "tv",
            _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, null)
        };
    }

    private static string ToSnakeCase(WatchlistSource source)
    {
        return source switch
        {
            WatchlistSource.Letterboxd => "letterboxd",
            WatchlistSource.Tmdb => "tmdb",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
    }

    private static string ToSnakeCase(ReleaseStatus releaseStatus)
    {
        return releaseStatus switch
        {
            ReleaseStatus.Released => "released",
            ReleaseStatus.Unreleased => "unreleased",
            ReleaseStatus.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(releaseStatus), releaseStatus, null)
        };
    }

    private static string ToSnakeCase(AvailabilityStatus availabilityStatus)
    {
        return availabilityStatus switch
        {
            AvailabilityStatus.AvailableOnPlex => "available_on_plex",
            AvailabilityStatus.NotOnPlex => "not_on_plex",
            AvailabilityStatus.Unreleased => "unreleased",
            AvailabilityStatus.UnknownMatch => "unknown_match",
            _ => throw new ArgumentOutOfRangeException(nameof(availabilityStatus), availabilityStatus, null)
        };
    }
}
```

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add backend/src/Watchlist.Application backend/tests/Watchlist.Application.Tests
git commit -m "feat: add watchlist query service"
```

## Task 4: Add Seeded Infrastructure Repository

**Files:**
- Create: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- Create: `backend/src/Watchlist.Infrastructure/SeededWatchlistReadRepository.cs`

- [ ] **Step 1: Add seeded repository**

Create `backend/src/Watchlist.Infrastructure/SeededWatchlistReadRepository.cs`:

```csharp
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class SeededWatchlistReadRepository : IWatchlistReadRepository
{
    private static readonly IReadOnlyList<WatchlistItem> Items =
    [
        new WatchlistItem(
            "movie-dune-part-two",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            "letterboxd-dune-part-two",
            "Dune: Part Two",
            2024,
            "Paul Atreides unites with Chani and the Fremen while seeking revenge against the conspirators who destroyed his family.",
            "https://image.tmdb.org/t/p/w500/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg",
            "https://image.tmdb.org/t/p/w1280/xOMo8BRK7PfcJv9JCnx7s5hj0PX.jpg",
            ReleaseStatus.Released,
            AvailabilityStatus.AvailableOnPlex,
            DateTimeOffset.Parse("2026-05-25T10:00:00+02:00")),
        new WatchlistItem(
            "movie-unreleased-example",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            "letterboxd-unreleased-example",
            "Future Movie",
            2027,
            "A seed item representing a wanted movie that has not been released yet.",
            null,
            null,
            ReleaseStatus.Unreleased,
            AvailabilityStatus.Unreleased,
            DateTimeOffset.Parse("2026-05-25T10:00:00+02:00")),
        new WatchlistItem(
            "tv-andor",
            MediaType.TvShow,
            WatchlistSource.Tmdb,
            "tmdb-tv-83867",
            "Andor",
            2022,
            "The story of Cassian Andor's journey to discover the difference he can make.",
            "https://image.tmdb.org/t/p/w500/59SVNwLfoMnZPPB6ukW6dlPxAdI.jpg",
            "https://image.tmdb.org/t/p/w1280/5NbdcZdsu7Rr0RthcYk4qqv7W7J.jpg",
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-05-25T10:00:00+02:00"))
    ];

    public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Items);
    }
}
```

- [ ] **Step 2: Add infrastructure registration**

Create `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWatchlistInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IWatchlistReadRepository, SeededWatchlistReadRepository>();

        return services;
    }
}
```

- [ ] **Step 3: Build**

Run:

```powershell
dotnet build backend/Watchlist.sln
```

Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 4: Commit**

Run:

```powershell
git add backend/src/Watchlist.Infrastructure
git commit -m "feat: add seeded watchlist repository"
```

## Task 5: Add Read-only API Endpoints With Tests

**Files:**
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Modify: `backend/src/Watchlist.Api/Watchlist.Api.csproj`
- Create: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Make API project testable**

Modify `backend/src/Watchlist.Api/Watchlist.Api.csproj` to include:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Watchlist.Application\Watchlist.Application.csproj" />
    <ProjectReference Include="..\Watchlist.Infrastructure\Watchlist.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write failing API tests**

Create `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Watchlist.Api.Tests;

public sealed class WatchlistApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetWatchlist_WhenMoviesAll_ReturnsMovies()
    {
        HttpClient client = factory.CreateClient();

        WatchlistItemResponse[]? response = await client.GetFromJsonAsync<WatchlistItemResponse[]>("/api/watchlist?mediaType=movie&filter=all");

        response.Should().NotBeNull();
        response!.Should().Contain(item => item.MediaType == "movie");
        response.Should().NotContain(item => item.MediaType == "tv");
    }

    [Fact]
    public async Task GetWatchlist_WhenMoviesAvailable_ReturnsOnlyPlexAvailableMovies()
    {
        HttpClient client = factory.CreateClient();

        WatchlistItemResponse[]? response = await client.GetFromJsonAsync<WatchlistItemResponse[]>("/api/watchlist?mediaType=movie&filter=available");

        response.Should().NotBeNull();
        response!.Should().OnlyContain(item => item.AvailabilityStatus == "available_on_plex");
    }

    [Fact]
    public async Task GetWatchlist_WhenMediaTypeInvalid_ReturnsBadRequest()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist?mediaType=music&filter=all");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetWatchlistItem_WhenItemExists_ReturnsItem()
    {
        HttpClient client = factory.CreateClient();

        WatchlistItemResponse? response = await client.GetFromJsonAsync<WatchlistItemResponse>("/api/watchlist/movie-dune-part-two");

        response.Should().NotBeNull();
        response!.Title.Should().Be("Dune: Part Two");
    }

    [Fact]
    public async Task GetWatchlistItem_WhenItemMissing_ReturnsNotFound()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist/missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record WatchlistItemResponse(
        string Id,
        string MediaType,
        string Source,
        string SourceId,
        string Title,
        int? Year,
        string? Overview,
        string? PosterUrl,
        string? BackdropUrl,
        string ReleaseStatus,
        string AvailabilityStatus,
        DateTimeOffset UpdatedAt);
}
```

- [ ] **Step 3: Run tests to verify failure**

Run:

```powershell
dotnet test backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj
```

Expected: tests fail because endpoints are not mapped and `Program` is not public to the test host.

- [ ] **Step 4: Implement Program.cs**

Replace `backend/src/Watchlist.Api/Program.cs` with:

```csharp
using Watchlist.Application;
using Watchlist.Domain;
using Watchlist.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddWatchlistInfrastructure();
builder.Services.AddScoped<WatchlistQueryService>();

WebApplication app = builder.Build();

app.MapGet("/api/watchlist", async (
    string mediaType,
    string filter,
    WatchlistQueryService queryService,
    CancellationToken cancellationToken) =>
{
    if (!TryParseMediaType(mediaType, out MediaType parsedMediaType))
    {
        return Results.BadRequest(new { error = "mediaType must be movie or tv" });
    }

    if (!TryParseFilter(filter, out WatchlistFilter parsedFilter))
    {
        return Results.BadRequest(new { error = "filter must be all or available" });
    }

    IReadOnlyList<WatchlistItemDto> items = await queryService.GetItemsAsync(parsedMediaType, parsedFilter, cancellationToken);

    return Results.Ok(items);
});

app.MapGet("/api/watchlist/{id}", async (
    string id,
    WatchlistQueryService queryService,
    CancellationToken cancellationToken) =>
{
    WatchlistItemDto? item = await queryService.GetItemAsync(id, cancellationToken);

    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapGet("/api/sync/status", () => Results.Ok(new
{
    status = "seeded",
    lastSuccessfulSyncAt = DateTimeOffset.Parse("2026-05-25T10:00:00+02:00")
}));

app.Run();

static bool TryParseMediaType(string value, out MediaType mediaType)
{
    switch (value)
    {
        case "movie":
            mediaType = MediaType.Movie;
            return true;
        case "tv":
            mediaType = MediaType.TvShow;
            return true;
        default:
            mediaType = default;
            return false;
    }
}

static bool TryParseFilter(string value, out WatchlistFilter filter)
{
    switch (value)
    {
        case "all":
            filter = WatchlistFilter.All;
            return true;
        case "available":
            filter = WatchlistFilter.Available;
            return true;
        default:
            filter = default;
            return false;
    }
}

public partial class Program
{
}
```

- [ ] **Step 5: Run API tests**

Run:

```powershell
dotnet test backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 6: Run all backend tests**

Run:

```powershell
dotnet test backend/Watchlist.sln
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add backend/src/Watchlist.Api backend/tests/Watchlist.Api.Tests
git commit -m "feat: expose read-only watchlist api"
```

## Task 6: Document Implemented Backend Contract

**Files:**
- Modify: `docs/architecture.md`
- Create: `docs/api.md`

- [ ] **Step 1: Add API contract doc**

Create `docs/api.md`:

````markdown
# API Contract

## GET /api/watchlist

Returns watchlist items for one media type and filter.

Query parameters:

- `mediaType`: `movie` or `tv`
- `filter`: `all` or `available`

Availability filter behavior:

- `all`: returns every wanted item for the selected media type.
- `available`: returns only items with `availabilityStatus` equal to `available_on_plex`.

Example response:

```json
[
  {
    "id": "movie-dune-part-two",
    "mediaType": "movie",
    "source": "letterboxd",
    "sourceId": "letterboxd-dune-part-two",
    "title": "Dune: Part Two",
    "year": 2024,
    "overview": "Paul Atreides unites with Chani and the Fremen while seeking revenge against the conspirators who destroyed his family.",
    "posterUrl": "https://image.tmdb.org/t/p/w500/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg",
    "backdropUrl": "https://image.tmdb.org/t/p/w1280/xOMo8BRK7PfcJv9JCnx7s5hj0PX.jpg",
    "releaseStatus": "released",
    "availabilityStatus": "available_on_plex",
    "updatedAt": "2026-05-25T10:00:00+02:00"
  }
]
```

## GET /api/watchlist/{id}

Returns a single watchlist item by normalized backend ID.

Responses:

- `200 OK`: item exists.
- `404 Not Found`: item does not exist.

## GET /api/sync/status

Returns the latest backend sync status.

Current seeded response:

```json
{
  "status": "seeded",
  "lastSuccessfulSyncAt": "2026-05-25T10:00:00+02:00"
}
```
````

- [ ] **Step 2: Link API doc from architecture**

Append this section to `docs/architecture.md`:

```markdown
## API Contract

The implemented backend API contract is documented in [api.md](api.md).
```

- [ ] **Step 3: Verify documentation and tests**

Run:

```powershell
dotnet test backend/Watchlist.sln
git diff --check
```

Expected: all tests pass and `git diff --check` reports no whitespace errors.

- [ ] **Step 4: Commit**

Run:

```powershell
git add docs/api.md docs/architecture.md
git commit -m "docs: document backend api contract"
```

## Plan Self-Review

- Spec coverage: This plan covers the backend, read-only API, availability states, source/availability separation, and documentation. Android TV and live Letterboxd/TMDB/Plex/MongoDB sync are intentionally split into later plans.
- Red-flag scan: The plan avoids unresolved markers and vague implementation instructions.
- Type consistency: Domain enum names, DTO property names, endpoint query values, and test response fields are consistent across tasks.
