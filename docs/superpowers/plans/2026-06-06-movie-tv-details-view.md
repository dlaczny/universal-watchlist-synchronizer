# Movie And TV Details View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Plex-like Android TV detail screen opened from poster Select, backed by a richer cached backend detail endpoint.

**Architecture:** Keep `GET /api/watchlist` lean for the grid and make `GET /api/watchlist/{id}` return a richer shared detail DTO. TMDB movie sync stores runtime/language/vote metadata in MongoDB; Android opens `DetailsActivity` immediately from the grid item and refreshes from the detail endpoint.

**Tech Stack:** .NET 8 minimal API, C# records, MongoDB driver, xUnit/FluentAssertions, Android Java programmatic UI, JUnit.

---

## File Structure

Backend files to create:

- `backend/src/Watchlist.Application/WatchlistItemDetailsDto.cs`: richer detail response contract.
- `backend/src/Watchlist.Application/WatchlistPrimaryAction.cs`: pure mapper for detail primary action labels/states.

Backend files to modify:

- `backend/src/Watchlist.Application/TmdbMovieDetailsDto.cs`: add runtime, original language, vote average, vote count.
- `backend/src/Watchlist.Application/TmdbMovieMetadataUpdate.cs`: carry the new TMDB fields into persistence.
- `backend/src/Watchlist.Application/WatchlistQueryService.cs`: keep list mapping unchanged and add detail mapping.
- `backend/src/Watchlist.Domain/WatchlistItem.cs`: add detail-only optional fields used by the query service.
- `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`: add Mongo fields and map them to domain.
- `backend/src/Watchlist.Infrastructure/MongoTmdbMovieMetadataRepository.cs`: persist the new fields.
- `backend/src/Watchlist.Infrastructure/TmdbMovieClient.cs`: parse TMDB `runtime`, `original_language`, `vote_average`, `vote_count`.
- `backend/src/Watchlist.Infrastructure/SeedData.cs`: seed at least one item with detail metadata for API tests.
- `backend/src/Watchlist.Api/Program.cs`: return details DTO from `/api/watchlist/{id}` and proxy image URLs for it.
- `backend/tests/Watchlist.Application.Tests/*.cs`: focused unit coverage.
- `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`: endpoint contract coverage.
- `docs/api.md`: document the richer detail endpoint.

Android files to create:

- `android/app/src/main/java/com/watchlist/tv/WatchlistItemDetails.java`: Android detail model.
- `android/app/src/main/java/com/watchlist/tv/DetailsActivity.java`: Plex-like detail screen.
- `android/app/src/test/java/com/watchlist/tv/WatchlistItemDetailsTest.java`: action/metadata helper tests.

Android files to modify:

- `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`: detail path, detail fetch, parser.
- `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`: make grid item serializable through Intent extras.
- `android/app/src/main/java/com/watchlist/tv/MainActivity.java`: launch `DetailsActivity` on poster click.
- `android/app/src/main/AndroidManifest.xml`: register `DetailsActivity`.
- `android/app/src/main/res/values/strings.xml`: add detail screen strings.
- `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`: parser/path tests.
- `docs/android-tv.md`: update manual TV verification.

---

### Task 1: Backend Detail Contract And Primary Action Mapper

**Files:**
- Create: `backend/src/Watchlist.Application/WatchlistItemDetailsDto.cs`
- Create: `backend/src/Watchlist.Application/WatchlistPrimaryAction.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistQueryService.cs`
- Test: `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`

- [ ] **Step 1: Write failing query-service tests for the rich detail DTO**

Append these tests to `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs` before `CreateItem`:

```csharp
[Fact]
public async Task GetItemDetailsAsync_WhenItemExists_ReturnsDetailOnlyFieldsAndPrimaryAction()
{
    IReadOnlyList<WatchlistItem> items =
    [
        CreateItem("movie-1", MediaType.Movie, "Alien", AvailabilityStatus.AvailableOnPlex) with
        {
            Genres = ["Horror", "Science Fiction"],
            RuntimeMinutes = 117,
            OriginalLanguage = "en",
            TmdbVoteAverage = 8.2,
            TmdbVoteCount = 15000
        }
    ];
    WatchlistQueryService service = new(new StubWatchlistReadRepository(items));

    WatchlistItemDetailsDto? result = await service.GetItemDetailsAsync("movie-1", CancellationToken.None);

    result.Should().NotBeNull();
    result!.Id.Should().Be("movie-1");
    result.Genres.Should().Equal("Horror", "Science Fiction");
    result.RuntimeMinutes.Should().Be(117);
    result.OriginalLanguage.Should().Be("en");
    result.TmdbVoteAverage.Should().Be(8.2);
    result.TmdbVoteCount.Should().Be(15000);
    result.PrimaryActionLabel.Should().Be("Open in Plex");
    result.PrimaryActionEnabled.Should().BeTrue();
    result.PrimaryActionTarget.Should().BeNull();
}

[Theory]
[InlineData(AvailabilityStatus.AvailableOnPlex, "Open in Plex", true)]
[InlineData(AvailabilityStatus.NotOnPlex, "Unavailable", false)]
[InlineData(AvailabilityStatus.Unreleased, "Not released", false)]
[InlineData(AvailabilityStatus.UnknownMatch, "Match uncertain", false)]
public async Task GetItemDetailsAsync_MapsPrimaryActionFromAvailability(
    AvailabilityStatus availability,
    string label,
    bool enabled)
{
    IReadOnlyList<WatchlistItem> items =
    [
        CreateItem("movie-1", MediaType.Movie, "Alien", availability)
    ];
    WatchlistQueryService service = new(new StubWatchlistReadRepository(items));

    WatchlistItemDetailsDto? result = await service.GetItemDetailsAsync("movie-1", CancellationToken.None);

    result.Should().NotBeNull();
    result!.PrimaryActionLabel.Should().Be(label);
    result.PrimaryActionEnabled.Should().Be(enabled);
    result.PrimaryActionTarget.Should().BeNull();
}

[Fact]
public async Task GetItemDetailsAsync_WhenItemMissing_ReturnsNull()
{
    WatchlistQueryService service = new(new StubWatchlistReadRepository([]));

    WatchlistItemDetailsDto? result = await service.GetItemDetailsAsync("missing", CancellationToken.None);

    result.Should().BeNull();
}
```

Also update the `CreateItem` helper object initializer in the same test file to include the new default detail fields:

```csharp
return new WatchlistItem(
    id,
    mediaType,
    WatchlistSource.Letterboxd,
    $"source-{id}",
    title,
    1979,
    "A test overview.",
    "https://example.test/poster.jpg",
    "https://example.test/backdrop.jpg",
    ReleaseStatus.Released,
    availabilityStatus,
    addedAt,
    UpdatedAt)
{
    VodReleaseKnown = true,
    ReleasedOnVod = true,
    VodRegions = ["PL", "US"],
    OwnedServiceAvailability = ["Amazon Prime Video", "Max"],
    Genres = ["Drama"],
    RuntimeMinutes = 93,
    OriginalLanguage = "en",
    TmdbVoteAverage = 7.7,
    TmdbVoteCount = 100
};
```

- [ ] **Step 2: Run the failing test**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~WatchlistQueryServiceTests" --no-restore
```

Expected: fail because `WatchlistItem` has no detail fields, `WatchlistItemDetailsDto` is missing, and `GetItemDetailsAsync` is not defined.

- [ ] **Step 3: Add domain detail fields**

Modify `backend/src/Watchlist.Domain/WatchlistItem.cs` so the record keeps the existing constructor and adds these init properties:

```csharp
public IReadOnlyList<string> Genres { get; init; } = [];

public int? RuntimeMinutes { get; init; }

public string? OriginalLanguage { get; init; }

public double? TmdbVoteAverage { get; init; }

public int? TmdbVoteCount { get; init; }
```

- [ ] **Step 4: Add the detail DTO**

Create `backend/src/Watchlist.Application/WatchlistItemDetailsDto.cs`:

```csharp
namespace Watchlist.Application;

/// <summary>
/// Read model returned by the single-item details endpoint.
/// </summary>
public sealed record WatchlistItemDetailsDto(
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
    bool VodReleaseKnown,
    bool ReleasedOnVod,
    IReadOnlyList<string> VodRegions,
    IReadOnlyList<string> OwnedServiceAvailability,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Genres,
    int? RuntimeMinutes,
    string? OriginalLanguage,
    double? TmdbVoteAverage,
    int? TmdbVoteCount,
    string PrimaryActionLabel,
    bool PrimaryActionEnabled,
    string? PrimaryActionTarget);
```

- [ ] **Step 5: Add the primary action mapper**

Create `backend/src/Watchlist.Application/WatchlistPrimaryAction.cs`:

```csharp
using Watchlist.Domain;

namespace Watchlist.Application;

public sealed record WatchlistPrimaryAction(
    string Label,
    bool Enabled,
    string? Target);

public static class WatchlistPrimaryActionMapper
{
    public static WatchlistPrimaryAction FromAvailability(AvailabilityStatus status)
    {
        return status switch
        {
            AvailabilityStatus.AvailableOnPlex => new WatchlistPrimaryAction("Open in Plex", true, null),
            AvailabilityStatus.Unreleased => new WatchlistPrimaryAction("Not released", false, null),
            AvailabilityStatus.UnknownMatch => new WatchlistPrimaryAction("Match uncertain", false, null),
            _ => new WatchlistPrimaryAction("Unavailable", false, null)
        };
    }
}
```

- [ ] **Step 6: Add detail mapping to the query service**

In `backend/src/Watchlist.Application/WatchlistQueryService.cs`, add this method after `GetItemAsync`:

```csharp
/// <summary>
/// Gets a single watchlist item by identifier using the richer details contract.
/// </summary>
public async Task<WatchlistItemDetailsDto?> GetItemDetailsAsync(
    string id,
    CancellationToken cancellationToken)
{
    IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(cancellationToken);
    WatchlistItem? item = items.FirstOrDefault(item => item.Id == id);

    return item is null ? null : ToDetailsDto(item);
}
```

Add this private mapper below `ToDto`:

```csharp
private static WatchlistItemDetailsDto ToDetailsDto(WatchlistItem item)
{
    WatchlistPrimaryAction action = WatchlistPrimaryActionMapper.FromAvailability(item.AvailabilityStatus);

    return new WatchlistItemDetailsDto(
        item.Id,
        ToApiValue(item.MediaType),
        ToApiValue(item.Source),
        item.SourceId,
        item.Title,
        item.Year,
        item.Overview,
        item.PosterUrl,
        item.BackdropUrl,
        ToApiValue(item.ReleaseStatus),
        ToApiValue(item.AvailabilityStatus),
        item.VodReleaseKnown,
        item.ReleasedOnVod,
        item.VodRegions,
        item.OwnedServiceAvailability,
        item.AddedAt,
        item.UpdatedAt,
        item.Genres,
        item.RuntimeMinutes,
        item.OriginalLanguage,
        item.TmdbVoteAverage,
        item.TmdbVoteCount,
        action.Label,
        action.Enabled,
        action.Target);
}
```

- [ ] **Step 7: Run the query-service tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~WatchlistQueryServiceTests" --no-restore
```

Expected: pass.

- [ ] **Step 8: Commit**

```powershell
git add -- backend/src/Watchlist.Application/WatchlistItemDetailsDto.cs backend/src/Watchlist.Application/WatchlistPrimaryAction.cs backend/src/Watchlist.Application/WatchlistQueryService.cs backend/src/Watchlist.Domain/WatchlistItem.cs backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs
git commit -m "feat: add watchlist detail contract"
```

---

### Task 2: TMDB Runtime, Language, And Vote Metadata

**Files:**
- Modify: `backend/src/Watchlist.Application/TmdbMovieDetailsDto.cs`
- Modify: `backend/src/Watchlist.Application/TmdbMovieMetadataUpdate.cs`
- Modify: `backend/src/Watchlist.Application/TmdbMovieEnrichmentService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/TmdbMovieClient.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TmdbMovieClientTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TmdbMovieEnrichmentServiceTests.cs`

- [ ] **Step 1: Update failing TMDB client test fixture expectations**

In `backend/tests/Watchlist.Application.Tests/TmdbMovieClientTests.cs`, update the first details fixture to include:

```json
"runtime": 96,
"original_language": "en",
"vote_average": 7.4,
"vote_count": 812,
```

Update the `metadata.Details.Should().BeEquivalentTo(new TmdbMovieDetailsDto(...))` expected value to:

```csharp
metadata.Details.Should().BeEquivalentTo(new TmdbMovieDetailsDto(
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
    ["Drama"],
    96,
    "en",
    7.4,
    812));
```

For every other `new TmdbMovieDetailsDto(...)` in tests, append:

```csharp
null,
null,
null,
null
```

- [ ] **Step 2: Add enrichment persistence expectation**

In `backend/tests/Watchlist.Application.Tests/TmdbMovieEnrichmentServiceTests.cs`, update the first test's update assertion to include:

```csharp
&& update.RuntimeMinutes == 96
&& update.OriginalLanguage == "en"
&& update.TmdbVoteAverage == 7.4
&& update.TmdbVoteCount == 812
```

Update the helper `CreateMetadata` so `new TmdbMovieDetailsDto(...)` ends with:

```csharp
["Drama"],
96,
"en",
7.4,
812)
```

- [ ] **Step 3: Run the failing TMDB tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TmdbMovieClientTests|FullyQualifiedName~TmdbMovieEnrichmentServiceTests" --no-restore
```

Expected: fail because DTO constructors and metadata update do not include the new fields.

- [ ] **Step 4: Extend `TmdbMovieDetailsDto`**

Modify `backend/src/Watchlist.Application/TmdbMovieDetailsDto.cs`:

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
    IReadOnlyList<string> Genres,
    int? RuntimeMinutes,
    string? OriginalLanguage,
    double? TmdbVoteAverage,
    int? TmdbVoteCount);
```

- [ ] **Step 5: Extend `TmdbMovieMetadataUpdate`**

Modify `backend/src/Watchlist.Application/TmdbMovieMetadataUpdate.cs` by adding these properties immediately after `IReadOnlyList<string> Genres`:

```csharp
int? RuntimeMinutes,
string? OriginalLanguage,
double? TmdbVoteAverage,
int? TmdbVoteCount,
```

- [ ] **Step 6: Pass new fields from enrichment service**

In `backend/src/Watchlist.Application/TmdbMovieEnrichmentService.cs`, find the `new TmdbMovieMetadataUpdate(...)` for successful metadata and pass these values immediately after `metadata.Details.Genres`:

```csharp
metadata.Details.RuntimeMinutes,
metadata.Details.OriginalLanguage,
metadata.Details.TmdbVoteAverage,
metadata.Details.TmdbVoteCount,
```

For failed/not-found update constructors, pass:

```csharp
[],
null,
null,
null,
null,
```

where the constructor now expects genres and the four detail fields.

- [ ] **Step 7: Parse TMDB fields**

In `backend/src/Watchlist.Infrastructure/TmdbMovieClient.cs`, extend `TmdbMovieDetailsResponse`:

```csharp
private sealed record TmdbMovieDetailsResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("imdb_id")] string? ImdbId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("original_title")] string OriginalTitle,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("release_date")] string? ReleaseDate,
    [property: JsonPropertyName("poster_path")] string? PosterPath,
    [property: JsonPropertyName("backdrop_path")] string? BackdropPath,
    [property: JsonPropertyName("genres")] IReadOnlyList<TmdbGenreResponse>? Genres,
    [property: JsonPropertyName("runtime")] int? Runtime,
    [property: JsonPropertyName("original_language")] string? OriginalLanguage,
    [property: JsonPropertyName("vote_average")] double? VoteAverage,
    [property: JsonPropertyName("vote_count")] int? VoteCount);
```

Update the `return new TmdbMovieDetailsDto(...)` call to append:

```csharp
details.Runtime is > 0 ? details.Runtime : null,
NormalizeOptionalString(details.OriginalLanguage),
details.VoteAverage,
details.VoteCount);
```

- [ ] **Step 8: Run the TMDB tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TmdbMovieClientTests|FullyQualifiedName~TmdbMovieEnrichmentServiceTests" --no-restore
```

Expected: pass.

- [ ] **Step 9: Commit**

```powershell
git add -- backend/src/Watchlist.Application/TmdbMovieDetailsDto.cs backend/src/Watchlist.Application/TmdbMovieMetadataUpdate.cs backend/src/Watchlist.Application/TmdbMovieEnrichmentService.cs backend/src/Watchlist.Infrastructure/TmdbMovieClient.cs backend/tests/Watchlist.Application.Tests/TmdbMovieClientTests.cs backend/tests/Watchlist.Application.Tests/TmdbMovieEnrichmentServiceTests.cs
git commit -m "feat: parse tmdb detail metadata"
```

---

### Task 3: Mongo Persistence And API Detail Endpoint

**Files:**
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoTmdbMovieMetadataRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/SeedData.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTmdbMovieMetadataRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Write failing Mongo document mapping test**

In `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`, extend `ToDomain_WhenDocumentHasTmdbMetadata_MapsDisplayFieldsFromDocument` with these document fields:

```csharp
RuntimeMinutes = 93,
OriginalLanguage = "en",
TmdbVoteAverage = 7.7,
TmdbVoteCount = 1200,
```

Add these assertions:

```csharp
item.Genres.Should().Equal("Drama");
item.RuntimeMinutes.Should().Be(93);
item.OriginalLanguage.Should().Be("en");
item.TmdbVoteAverage.Should().Be(7.7);
item.TmdbVoteCount.Should().Be(1200);
```

- [ ] **Step 2: Write failing API detail endpoint test**

In `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`, extend `GetWatchlistItem_WhenItemExists_ReturnsItem` with:

```csharp
document.RootElement.GetProperty("genres").EnumerateArray()
    .Select(genre => genre.GetString())
    .Should()
    .Equal("Science Fiction", "Adventure");
document.RootElement.GetProperty("runtimeMinutes").GetInt32().Should().Be(166);
document.RootElement.GetProperty("originalLanguage").GetString().Should().Be("en");
document.RootElement.GetProperty("tmdbVoteAverage").GetDouble().Should().Be(8.1);
document.RootElement.GetProperty("tmdbVoteCount").GetInt32().Should().BeGreaterThan(10);
document.RootElement.GetProperty("primaryActionLabel").GetString().Should().Be("Open in Plex");
document.RootElement.GetProperty("primaryActionEnabled").GetBoolean().Should().BeTrue();
document.RootElement.GetProperty("primaryActionTarget").ValueKind.Should().Be(JsonValueKind.Null);
```

Add a list compatibility assertion to `GetWatchlist_WhenDefaultQuery_ReturnsAllItemsWithAddedAt`:

```csharp
items[0].TryGetProperty("runtimeMinutes", out _).Should().BeFalse();
items[0].TryGetProperty("primaryActionLabel", out _).Should().BeFalse();
```

- [ ] **Step 3: Run failing persistence/API tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~MongoWatchlistItemDocumentTests|FullyQualifiedName~MongoTmdbMovieMetadataRepositoryTests" --no-restore
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~WatchlistApiTests" --no-restore
```

Expected: fail because fields are not stored/mapped and the API still returns `WatchlistItemDto`.

- [ ] **Step 4: Add Mongo document fields and mapping**

In `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`, add:

```csharp
public int? RuntimeMinutes { get; init; }

public string? OriginalLanguage { get; init; }

public double? TmdbVoteAverage { get; init; }

public int? TmdbVoteCount { get; init; }
```

Update `ToDomain()` object initializer:

```csharp
Genres = Genres,
RuntimeMinutes = RuntimeMinutes,
OriginalLanguage = OriginalLanguage,
TmdbVoteAverage = TmdbVoteAverage,
TmdbVoteCount = TmdbVoteCount,
VodReleaseKnown = string.Equals(TmdbMetadataStatus, "enriched", StringComparison.Ordinal),
ReleasedOnVod = ReleasedOnVod,
VodRegions = VodRegions,
OwnedServiceAvailability = OwnedServiceAvailability
```

Update `FromDomain(...)` to set:

```csharp
Genres = item.Genres,
RuntimeMinutes = item.RuntimeMinutes,
OriginalLanguage = item.OriginalLanguage,
TmdbVoteAverage = item.TmdbVoteAverage,
TmdbVoteCount = item.TmdbVoteCount,
```

- [ ] **Step 5: Persist new fields in Mongo TMDB update**

In `backend/src/Watchlist.Infrastructure/MongoTmdbMovieMetadataRepository.cs`, add these setters after `.Set(document => document.Genres, metadata.Genres)`:

```csharp
.Set(document => document.RuntimeMinutes, metadata.RuntimeMinutes)
.Set(document => document.OriginalLanguage, metadata.OriginalLanguage)
.Set(document => document.TmdbVoteAverage, metadata.TmdbVoteAverage)
.Set(document => document.TmdbVoteCount, metadata.TmdbVoteCount)
```

- [ ] **Step 6: Seed Dune with detail metadata**

In `backend/src/Watchlist.Infrastructure/SeedData.cs`, update the `movie-dune-part-two` object initializer:

```csharp
OwnedServiceAvailability = ["Amazon Prime Video"],
Genres = ["Science Fiction", "Adventure"],
RuntimeMinutes = 166,
OriginalLanguage = "en",
TmdbVoteAverage = 8.1,
TmdbVoteCount = 7000
```

- [ ] **Step 7: Return detail DTO from the single-item endpoint**

In `backend/src/Watchlist.Api/Program.cs`, replace the `/api/watchlist/{id}` handler body with:

```csharp
WatchlistItemDetailsDto? item = await queryService.GetItemDetailsAsync(id, cancellationToken);

return item is null ? Results.NotFound() : Results.Ok(ToBackendImageUrls(item));
```

Add overloads below the existing `ToBackendImageUrls(WatchlistItemDto item)`:

```csharp
static WatchlistItemDetailsDto ToBackendImageUrls(WatchlistItemDetailsDto item)
{
    return item with
    {
        PosterUrl = ToBackendTmdbImageUrl(item.PosterUrl),
        BackdropUrl = ToBackendTmdbImageUrl(item.BackdropUrl)
    };
}
```

- [ ] **Step 8: Run persistence and API tests**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~MongoWatchlistItemDocumentTests|FullyQualifiedName~MongoTmdbMovieMetadataRepositoryTests|FullyQualifiedName~WatchlistQueryServiceTests" --no-restore
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~WatchlistApiTests" --no-restore
```

Expected: pass.

- [ ] **Step 9: Commit**

```powershell
git add -- backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs backend/src/Watchlist.Infrastructure/MongoTmdbMovieMetadataRepository.cs backend/src/Watchlist.Infrastructure/SeedData.cs backend/src/Watchlist.Api/Program.cs backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs backend/tests/Watchlist.Application.Tests/MongoTmdbMovieMetadataRepositoryTests.cs backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs
git commit -m "feat: expose watchlist item details"
```

---

### Task 4: Android Detail Model And API Parser

**Files:**
- Create: `android/app/src/main/java/com/watchlist/tv/WatchlistItemDetails.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`
- Test: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`
- Test: `android/app/src/test/java/com/watchlist/tv/WatchlistItemDetailsTest.java`

- [ ] **Step 1: Write failing Android parser/path tests**

Add to `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`:

```java
@Test
public void buildWatchlistDetailPath_usesItemId() {
    assertEquals("/api/watchlist/movie-dune-part-two", WatchlistApiClient.buildWatchlistDetailPath("movie-dune-part-two"));
}

@Test
public void parseItemDetails_parsesRichDetailJson() throws Exception {
    String json = "{"
            + "\"id\":\"movie-dune-part-two\","
            + "\"mediaType\":\"movie\","
            + "\"source\":\"letterboxd\","
            + "\"sourceId\":\"letterboxd-dune-part-two\","
            + "\"title\":\"Dune: Part Two\","
            + "\"year\":2024,"
            + "\"overview\":\"Overview\","
            + "\"posterUrl\":\"/api/images/tmdb/w500/poster.jpg\","
            + "\"backdropUrl\":\"/api/images/tmdb/w1280/backdrop.jpg\","
            + "\"releaseStatus\":\"released\","
            + "\"availabilityStatus\":\"available_on_plex\","
            + "\"vodReleaseKnown\":true,"
            + "\"releasedOnVod\":true,"
            + "\"vodRegions\":[\"PL\"],"
            + "\"ownedServiceAvailability\":[\"Amazon Prime Video\"],"
            + "\"addedAt\":\"2026-05-24T10:00:00+02:00\","
            + "\"updatedAt\":\"2026-05-25T10:00:00+02:00\","
            + "\"genres\":[\"Science Fiction\",\"Adventure\"],"
            + "\"runtimeMinutes\":166,"
            + "\"originalLanguage\":\"en\","
            + "\"tmdbVoteAverage\":8.1,"
            + "\"tmdbVoteCount\":7000,"
            + "\"primaryActionLabel\":\"Open in Plex\","
            + "\"primaryActionEnabled\":true,"
            + "\"primaryActionTarget\":null"
            + "}";

    WatchlistItemDetails details = WatchlistApiClient.parseItemDetails(json, "http://10.0.2.2:5000");

    assertEquals("movie-dune-part-two", details.id());
    assertEquals("http://10.0.2.2:5000/api/images/tmdb/w500/poster.jpg", details.posterUrl());
    assertEquals(Integer.valueOf(166), details.runtimeMinutes());
    assertEquals("en", details.originalLanguage());
    assertEquals(Double.valueOf(8.1), details.tmdbVoteAverage());
    assertEquals(Integer.valueOf(7000), details.tmdbVoteCount());
    assertEquals("Science Fiction", details.genres().get(0));
    assertEquals("Open in Plex", details.primaryActionLabel());
    assertEquals(true, details.primaryActionEnabled());
    assertEquals(null, details.primaryActionTarget());
}

@Test
public void parseItemDetails_whenOptionalFieldsMissing_defaultsSafely() throws Exception {
    String json = "{"
            + "\"id\":\"movie-minimal\","
            + "\"mediaType\":\"movie\","
            + "\"source\":\"letterboxd\","
            + "\"sourceId\":\"source\","
            + "\"title\":\"Minimal\","
            + "\"year\":null,"
            + "\"overview\":null,"
            + "\"posterUrl\":null,"
            + "\"backdropUrl\":null,"
            + "\"releaseStatus\":\"released\","
            + "\"availabilityStatus\":\"not_on_plex\","
            + "\"vodReleaseKnown\":false,"
            + "\"releasedOnVod\":false,"
            + "\"vodRegions\":[],"
            + "\"ownedServiceAvailability\":[],"
            + "\"addedAt\":\"2026-05-24T10:00:00+02:00\","
            + "\"updatedAt\":\"2026-05-25T10:00:00+02:00\","
            + "\"primaryActionLabel\":\"Unavailable\","
            + "\"primaryActionEnabled\":false,"
            + "\"primaryActionTarget\":null"
            + "}";

    WatchlistItemDetails details = WatchlistApiClient.parseItemDetails(json);

    assertEquals(0, details.genres().size());
    assertEquals(null, details.runtimeMinutes());
    assertEquals(null, details.originalLanguage());
    assertEquals(null, details.tmdbVoteAverage());
    assertEquals(null, details.tmdbVoteCount());
}
```

- [ ] **Step 2: Write failing detail helper tests**

Create `android/app/src/test/java/com/watchlist/tv/WatchlistItemDetailsTest.java`:

```java
package com.watchlist.tv;

import static org.junit.Assert.assertEquals;

import java.util.List;
import org.junit.Test;

public class WatchlistItemDetailsTest {
    @Test
    public void metadataSummary_hidesMissingValuesAndRequiresMeaningfulVoteCount() {
        WatchlistItemDetails details = createDetails(
                List.of("Drama", "Comedy"),
                93,
                "en",
                7.7,
                9);

        assertEquals("1977 • 1h 33m • EN • Drama, Comedy", details.metadataSummary());
    }

    @Test
    public void metadataSummary_includesTmdbScoreWhenVoteCountIsAtLeastTen() {
        WatchlistItemDetails details = createDetails(
                List.of("Comedy"),
                93,
                "en",
                7.7,
                10);

        assertEquals("1977 • 1h 33m • EN • Comedy • 7.7 TMDB", details.metadataSummary());
    }

    private static WatchlistItemDetails createDetails(
            List<String> genres,
            Integer runtimeMinutes,
            String language,
            Double score,
            Integer voteCount) {
        return new WatchlistItemDetails(
                "movie-annie-hall",
                "movie",
                "letterboxd",
                "source",
                "Annie Hall",
                1977,
                "Overview",
                null,
                null,
                "released",
                "available_on_plex",
                false,
                false,
                List.of(),
                List.of(),
                "2026-05-24T10:00:00+02:00",
                "2026-05-25T10:00:00+02:00",
                genres,
                runtimeMinutes,
                language,
                score,
                voteCount,
                "Open in Plex",
                true,
                null);
    }
}
```

- [ ] **Step 3: Run failing Android tests**

Run:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --tests "com.watchlist.tv.WatchlistApiClientTest" --tests "com.watchlist.tv.WatchlistItemDetailsTest" --no-daemon
```

Expected: fail because `WatchlistItemDetails` and parser methods are missing.

- [ ] **Step 4: Add `WatchlistItemDetails`**

Create `android/app/src/main/java/com/watchlist/tv/WatchlistItemDetails.java` with immutable fields matching `WatchlistItem`, plus detail fields and helper formatting:

```java
package com.watchlist.tv;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

public final class WatchlistItemDetails {
    private final String id;
    private final String mediaType;
    private final String source;
    private final String sourceId;
    private final String title;
    private final Integer year;
    private final String overview;
    private final String posterUrl;
    private final String backdropUrl;
    private final String releaseStatus;
    private final String availabilityStatus;
    private final boolean vodReleaseKnown;
    private final boolean releasedOnVod;
    private final List<String> vodRegions;
    private final List<String> ownedServiceAvailability;
    private final String addedAt;
    private final String updatedAt;
    private final List<String> genres;
    private final Integer runtimeMinutes;
    private final String originalLanguage;
    private final Double tmdbVoteAverage;
    private final Integer tmdbVoteCount;
    private final String primaryActionLabel;
    private final boolean primaryActionEnabled;
    private final String primaryActionTarget;

    public WatchlistItemDetails(
            String id,
            String mediaType,
            String source,
            String sourceId,
            String title,
            Integer year,
            String overview,
            String posterUrl,
            String backdropUrl,
            String releaseStatus,
            String availabilityStatus,
            boolean vodReleaseKnown,
            boolean releasedOnVod,
            List<String> vodRegions,
            List<String> ownedServiceAvailability,
            String addedAt,
            String updatedAt,
            List<String> genres,
            Integer runtimeMinutes,
            String originalLanguage,
            Double tmdbVoteAverage,
            Integer tmdbVoteCount,
            String primaryActionLabel,
            boolean primaryActionEnabled,
            String primaryActionTarget) {
        this.id = id;
        this.mediaType = mediaType;
        this.source = source;
        this.sourceId = sourceId;
        this.title = title;
        this.year = year;
        this.overview = overview;
        this.posterUrl = posterUrl;
        this.backdropUrl = backdropUrl;
        this.releaseStatus = releaseStatus;
        this.availabilityStatus = availabilityStatus;
        this.vodReleaseKnown = vodReleaseKnown;
        this.releasedOnVod = releasedOnVod;
        this.vodRegions = Collections.unmodifiableList(new ArrayList<>(vodRegions));
        this.ownedServiceAvailability = Collections.unmodifiableList(new ArrayList<>(ownedServiceAvailability));
        this.addedAt = addedAt;
        this.updatedAt = updatedAt;
        this.genres = Collections.unmodifiableList(new ArrayList<>(genres));
        this.runtimeMinutes = runtimeMinutes;
        this.originalLanguage = originalLanguage;
        this.tmdbVoteAverage = tmdbVoteAverage;
        this.tmdbVoteCount = tmdbVoteCount;
        this.primaryActionLabel = primaryActionLabel;
        this.primaryActionEnabled = primaryActionEnabled;
        this.primaryActionTarget = primaryActionTarget;
    }

    public static WatchlistItemDetails fromItem(WatchlistItem item) {
        return new WatchlistItemDetails(
                item.id(),
                item.mediaType(),
                item.source(),
                item.sourceId(),
                item.title(),
                item.year(),
                item.overview(),
                item.posterUrl(),
                item.backdropUrl(),
                item.releaseStatus(),
                item.availabilityStatus(),
                item.vodReleaseKnown(),
                item.releasedOnVod(),
                item.vodRegions(),
                item.ownedServiceAvailability(),
                item.addedAt(),
                item.updatedAt(),
                List.of(),
                null,
                null,
                null,
                null,
                MainActivity.formatAvailability(item),
                WatchlistFilters.AVAILABLE_ON_PLEX.equals(item.availabilityStatus()),
                null);
    }

    public String metadataSummary() {
        List<String> parts = new ArrayList<>();
        if (year != null) {
            parts.add(String.valueOf(year));
        }
        if (runtimeMinutes != null && runtimeMinutes > 0) {
            parts.add(formatRuntime(runtimeMinutes));
        }
        if (originalLanguage != null && !originalLanguage.isEmpty()) {
            parts.add(originalLanguage.toUpperCase(Locale.ROOT));
        }
        if (!genres.isEmpty()) {
            parts.add(String.join(", ", genres));
        }
        if (tmdbVoteAverage != null && tmdbVoteCount != null && tmdbVoteCount >= 10) {
            parts.add(String.format(Locale.US, "%.1f TMDB", tmdbVoteAverage));
        }
        return String.join(" • ", parts);
    }

    private static String formatRuntime(int minutes) {
        int hours = minutes / 60;
        int remainingMinutes = minutes % 60;
        if (hours <= 0) {
            return remainingMinutes + "m";
        }
        if (remainingMinutes == 0) {
            return hours + "h";
        }
        return hours + "h " + remainingMinutes + "m";
    }

    public String id() { return id; }
    public String mediaType() { return mediaType; }
    public String source() { return source; }
    public String sourceId() { return sourceId; }
    public String title() { return title; }
    public Integer year() { return year; }
    public String overview() { return overview; }
    public String posterUrl() { return posterUrl; }
    public String backdropUrl() { return backdropUrl; }
    public String releaseStatus() { return releaseStatus; }
    public String availabilityStatus() { return availabilityStatus; }
    public boolean vodReleaseKnown() { return vodReleaseKnown; }
    public boolean releasedOnVod() { return releasedOnVod; }
    public List<String> vodRegions() { return vodRegions; }
    public List<String> ownedServiceAvailability() { return ownedServiceAvailability; }
    public String addedAt() { return addedAt; }
    public String updatedAt() { return updatedAt; }
    public List<String> genres() { return genres; }
    public Integer runtimeMinutes() { return runtimeMinutes; }
    public String originalLanguage() { return originalLanguage; }
    public Double tmdbVoteAverage() { return tmdbVoteAverage; }
    public Integer tmdbVoteCount() { return tmdbVoteCount; }
    public String primaryActionLabel() { return primaryActionLabel; }
    public boolean primaryActionEnabled() { return primaryActionEnabled; }
    public String primaryActionTarget() { return primaryActionTarget; }
}
```

- [ ] **Step 5: Add detail parsing to `WatchlistApiClient`**

In `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`, add:

```java
public WatchlistItemDetails getWatchlistItemDetails(String id) throws IOException, JSONException {
    String json = get(buildWatchlistDetailPath(id));
    return parseItemDetails(json, baseUrl);
}

public static String buildWatchlistDetailPath(String id) {
    return "/api/watchlist/" + id;
}

public static WatchlistItemDetails parseItemDetails(String json) throws JSONException {
    return parseItemDetails(json, null);
}

public static WatchlistItemDetails parseItemDetails(String json, String baseUrl) throws JSONException {
    JSONObject item = new JSONObject(json);
    return new WatchlistItemDetails(
            item.getString("id"),
            item.getString("mediaType"),
            item.getString("source"),
            item.getString("sourceId"),
            item.getString("title"),
            item.has("year") && !item.isNull("year") ? item.getInt("year") : null,
            nullableString(item, "overview"),
            resolveImageUrl(baseUrl, nullableString(item, "posterUrl")),
            resolveImageUrl(baseUrl, nullableString(item, "backdropUrl")),
            item.getString("releaseStatus"),
            item.getString("availabilityStatus"),
            item.has("vodReleaseKnown") && !item.isNull("vodReleaseKnown")
                    && item.getBoolean("vodReleaseKnown"),
            item.has("releasedOnVod") && !item.isNull("releasedOnVod")
                    && item.getBoolean("releasedOnVod"),
            parseStringArray(item.optJSONArray("vodRegions")),
            parseStringArray(item.optJSONArray("ownedServiceAvailability")),
            item.getString("addedAt"),
            item.getString("updatedAt"),
            parseStringArray(item.optJSONArray("genres")),
            item.has("runtimeMinutes") && !item.isNull("runtimeMinutes") ? item.getInt("runtimeMinutes") : null,
            nullableString(item, "originalLanguage"),
            item.has("tmdbVoteAverage") && !item.isNull("tmdbVoteAverage") ? item.getDouble("tmdbVoteAverage") : null,
            item.has("tmdbVoteCount") && !item.isNull("tmdbVoteCount") ? item.getInt("tmdbVoteCount") : null,
            item.optString("primaryActionLabel", "Unavailable"),
            item.has("primaryActionEnabled") && !item.isNull("primaryActionEnabled")
                    && item.getBoolean("primaryActionEnabled"),
            nullableString(item, "primaryActionTarget"));
}
```

- [ ] **Step 6: Run Android parser tests**

Run:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --tests "com.watchlist.tv.WatchlistApiClientTest" --tests "com.watchlist.tv.WatchlistItemDetailsTest" --no-daemon
```

Expected: pass.

- [ ] **Step 7: Commit**

```powershell
git add -- android/app/src/main/java/com/watchlist/tv/WatchlistItemDetails.java android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java android/app/src/test/java/com/watchlist/tv/WatchlistItemDetailsTest.java
git commit -m "feat: parse watchlist item details on android"
```

---

### Task 5: Android Details Activity UI

**Files:**
- Create: `android/app/src/main/java/com/watchlist/tv/DetailsActivity.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`
- Modify: `android/app/src/main/AndroidManifest.xml`
- Modify: `android/app/src/main/res/values/strings.xml`
- Test: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`

- [ ] **Step 1: Make `WatchlistItem` serializable**

Modify `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`:

```java
import java.io.Serializable;
```

Change the class declaration:

```java
public final class WatchlistItem implements Serializable {
    private static final long serialVersionUID = 1L;
```

- [ ] **Step 2: Add strings**

In `android/app/src/main/res/values/strings.xml`, add:

```xml
<string name="message_detail_backend_error">Could not refresh details. %1$s</string>
<string name="message_no_description">No description available.</string>
```

- [ ] **Step 3: Create `DetailsActivity`**

Create `android/app/src/main/java/com/watchlist/tv/DetailsActivity.java`. Use the same programmatic style as `MainActivity` and keep focus on the primary button:

```java
package com.watchlist.tv;

import android.app.Activity;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;

public final class DetailsActivity extends Activity {
    public static final String EXTRA_ITEM = "com.watchlist.tv.ITEM";

    private final ExecutorService apiExecutor = Executors.newSingleThreadExecutor();
    private final ExecutorService imageExecutor = Executors.newFixedThreadPool(2);
    private final Handler mainHandler = new Handler(Looper.getMainLooper());

    private WatchlistApiClient apiClient;
    private RemoteImageLoader imageLoader;
    private WatchlistItemDetails currentDetails;
    private FrameLayout root;
    private ImageView backdropView;
    private ImageView posterView;
    private TextView missingPosterView;
    private TextView titleView;
    private TextView metadataView;
    private TextView overviewView;
    private TextView errorView;
    private Button primaryActionButton;
    private volatile boolean destroyed;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        WatchlistItem item = (WatchlistItem) getIntent().getSerializableExtra(EXTRA_ITEM);
        if (item == null) {
            finish();
            return;
        }

        apiClient = new WatchlistApiClient(WatchlistConfig.apiBaseUrl());
        imageLoader = new RemoteImageLoader(imageExecutor, () -> !destroyed);
        currentDetails = WatchlistItemDetails.fromItem(item);
        setContentView(createContentView());
        render(currentDetails);
        primaryActionButton.requestFocus();
        fetchDetails(item.id());
    }

    @Override
    protected void onDestroy() {
        destroyed = true;
        mainHandler.removeCallbacksAndMessages(null);
        imageLoader.discardObsoleteRequests();
        apiExecutor.shutdownNow();
        imageExecutor.shutdownNow();
        super.onDestroy();
    }

    private View createContentView() {
        root = new FrameLayout(this);
        root.setBackgroundColor(Color.rgb(15, 20, 25));

        backdropView = new ImageView(this);
        backdropView.setScaleType(ImageView.ScaleType.CENTER_CROP);
        root.addView(backdropView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        View scrim = new View(this);
        scrim.setBackgroundColor(Color.argb(205, 10, 14, 18));
        root.addView(scrim, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        LinearLayout content = new LinearLayout(this);
        content.setOrientation(LinearLayout.HORIZONTAL);
        content.setGravity(Gravity.CENTER_VERTICAL);
        content.setPadding(dp(72), dp(54), dp(72), dp(54));
        root.addView(content, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        FrameLayout posterFrame = new FrameLayout(this);
        posterFrame.setBackgroundColor(Color.rgb(42, 48, 56));
        content.addView(posterFrame, new LinearLayout.LayoutParams(dp(230), dp(345)));

        posterView = new ImageView(this);
        posterView.setScaleType(ImageView.ScaleType.CENTER_CROP);
        posterFrame.addView(posterView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        missingPosterView = new TextView(this);
        missingPosterView.setText(R.string.message_artwork_unavailable);
        missingPosterView.setTextColor(Color.rgb(203, 213, 225));
        missingPosterView.setTextSize(18);
        missingPosterView.setGravity(Gravity.CENTER);
        posterFrame.addView(missingPosterView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        LinearLayout details = new LinearLayout(this);
        details.setOrientation(LinearLayout.VERTICAL);
        details.setPadding(dp(42), 0, 0, 0);
        content.addView(details, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1));

        titleView = new TextView(this);
        titleView.setTextColor(Color.WHITE);
        titleView.setTextSize(38);
        titleView.setTypeface(Typeface.DEFAULT_BOLD);
        titleView.setMaxLines(2);
        titleView.setEllipsize(TextUtils.TruncateAt.END);
        details.addView(titleView);

        metadataView = new TextView(this);
        metadataView.setTextColor(Color.rgb(203, 213, 225));
        metadataView.setTextSize(18);
        metadataView.setPadding(0, dp(12), 0, 0);
        details.addView(metadataView);

        primaryActionButton = new Button(this);
        primaryActionButton.setAllCaps(false);
        primaryActionButton.setTextSize(18);
        primaryActionButton.setMinHeight(dp(54));
        primaryActionButton.setOnClickListener(view -> view.requestFocus());
        LinearLayout.LayoutParams actionParams = new LinearLayout.LayoutParams(dp(220), dp(58));
        actionParams.setMargins(0, dp(26), 0, dp(24));
        details.addView(primaryActionButton, actionParams);

        overviewView = new TextView(this);
        overviewView.setTextColor(Color.WHITE);
        overviewView.setTextSize(18);
        overviewView.setLineSpacing(0, 1.1f);
        overviewView.setMaxLines(5);
        overviewView.setEllipsize(TextUtils.TruncateAt.END);
        details.addView(overviewView);

        errorView = new TextView(this);
        errorView.setTextColor(Color.rgb(252, 165, 165));
        errorView.setTextSize(15);
        errorView.setPadding(0, dp(18), 0, 0);
        details.addView(errorView);

        return root;
    }

    private void fetchDetails(String id) {
        try {
            apiExecutor.execute(() -> {
                try {
                    WatchlistItemDetails details = apiClient.getWatchlistItemDetails(id);
                    mainHandler.post(() -> {
                        if (!destroyed) {
                            currentDetails = details;
                            render(details);
                        }
                    });
                } catch (Exception exception) {
                    mainHandler.post(() -> {
                        if (!destroyed) {
                            errorView.setText(getString(R.string.message_detail_backend_error, exception.getMessage()));
                        }
                    });
                }
            });
        } catch (RejectedExecutionException ignored) {
            // The Activity is tearing down.
        }
    }

    private void render(WatchlistItemDetails details) {
        titleView.setText(details.title());
        String metadata = details.metadataSummary();
        metadataView.setText(metadata);
        metadataView.setVisibility(metadata.isEmpty() ? View.GONE : View.VISIBLE);
        overviewView.setText(details.overview() == null || details.overview().isEmpty()
                ? getString(R.string.message_no_description)
                : details.overview());
        primaryActionButton.setText(details.primaryActionLabel());
        primaryActionButton.setEnabled(details.primaryActionEnabled());
        primaryActionButton.setTextColor(details.primaryActionEnabled() ? Color.rgb(15, 20, 25) : Color.rgb(203, 213, 225));
        primaryActionButton.setBackground(actionBackground(details.primaryActionEnabled(), primaryActionButton.hasFocus()));
        primaryActionButton.setOnFocusChangeListener((view, hasFocus) ->
                view.setBackground(actionBackground(details.primaryActionEnabled(), hasFocus)));

        imageLoader.load(backdropView, details.backdropUrl(), Color.rgb(15, 20, 25), loaded -> {});
        missingPosterView.setVisibility(details.posterUrl() == null || details.posterUrl().isEmpty()
                ? View.VISIBLE
                : View.GONE);
        imageLoader.load(posterView, details.posterUrl(), Color.rgb(42, 48, 56), loaded ->
                missingPosterView.setVisibility(loaded ? View.GONE : View.VISIBLE));
    }

    private GradientDrawable actionBackground(boolean enabled, boolean focused) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setCornerRadius(dp(5));
        drawable.setColor(enabled ? Color.rgb(103, 232, 249) : Color.rgb(43, 53, 64));
        drawable.setStroke(dp(focused ? 3 : 1), focused ? Color.WHITE : Color.rgb(75, 85, 99));
        return drawable;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density);
    }
}
```

- [ ] **Step 4: Register the Activity**

In `android/app/src/main/AndroidManifest.xml`, add this before the existing `MainActivity` entry:

```xml
<activity
    android:name=".DetailsActivity"
    android:exported="false" />
```

- [ ] **Step 5: Launch details from poster Select**

In `android/app/src/main/java/com/watchlist/tv/MainActivity.java`, add import:

```java
import android.content.Intent;
```

In `createPosterTile`, replace:

```java
tile.setOnClickListener(view -> view.requestFocus());
```

with:

```java
tile.setOnClickListener(view -> openDetails(item));
```

Add method near `selectSortMode`:

```java
private void openDetails(WatchlistItem item) {
    browsingState = browsingState.withFocusedItemId(item.id());
    persistBrowsingState();
    Intent intent = new Intent(this, DetailsActivity.class);
    intent.putExtra(DetailsActivity.EXTRA_ITEM, item);
    startActivity(intent);
}
```

- [ ] **Step 6: Run Android unit tests and assemble**

Run:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

Expected: tests pass and debug APK builds.

- [ ] **Step 7: Commit**

```powershell
git add -- android/app/src/main/java/com/watchlist/tv/DetailsActivity.java android/app/src/main/java/com/watchlist/tv/WatchlistItem.java android/app/src/main/java/com/watchlist/tv/MainActivity.java android/app/src/main/AndroidManifest.xml android/app/src/main/res/values/strings.xml
git commit -m "feat: add android tv details screen"
```

---

### Task 6: Documentation And Full Verification

**Files:**
- Modify: `docs/api.md`
- Modify: `docs/android-tv.md`
- Modify: `docs/architecture.md` only if implementation changes backend/client boundaries beyond this plan.

- [ ] **Step 1: Update API documentation**

In `docs/api.md`, replace the `GET /api/watchlist/{id}` response example with one that includes detail-only fields:

```json
{
  "id": "movie-dune-part-two",
  "mediaType": "movie",
  "source": "letterboxd",
  "sourceId": "letterboxd-dune-part-two",
  "title": "Dune: Part Two",
  "year": 2024,
  "overview": "Paul Atreides unites with Chani and the Fremen while seeking revenge against the conspirators who destroyed his family.",
  "posterUrl": "/api/images/tmdb/w500/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg",
  "backdropUrl": "/api/images/tmdb/w1280/xOMo8BRK7PfcJv9JCnx7s5hj0PX.jpg",
  "releaseStatus": "released",
  "availabilityStatus": "available_on_plex",
  "vodReleaseKnown": true,
  "releasedOnVod": true,
  "vodRegions": ["PL", "US"],
  "ownedServiceAvailability": ["Amazon Prime Video"],
  "addedAt": "2026-05-20T10:00:00+02:00",
  "updatedAt": "2026-05-25T10:00:00+02:00",
  "genres": ["Science Fiction", "Adventure"],
  "runtimeMinutes": 166,
  "originalLanguage": "en",
  "tmdbVoteAverage": 8.1,
  "tmdbVoteCount": 7000,
  "primaryActionLabel": "Open in Plex",
  "primaryActionEnabled": true,
  "primaryActionTarget": null
}
```

Add this note:

```markdown
The list endpoint remains optimized for poster-grid browsing. Detail-only fields are returned by `GET /api/watchlist/{id}` so Android can render a richer page without increasing every grid response.
```

- [ ] **Step 2: Update Android TV documentation**

In `docs/android-tv.md`, add to Current UX:

```markdown
- Pressing Select on a focused poster opens a Plex-like detail screen with backdrop, poster, metadata, description, and a state-aware primary action button.
- The details screen renders from grid data immediately, then refreshes from `GET /api/watchlist/{id}`.
```

Add to Manual Remote Test:

```markdown
8. Focus a poster and press Select. Confirm the detail screen opens, the primary action button receives focus, and Back returns to the grid with the poster focus restored.
9. Confirm missing detail metadata is hidden and missing artwork uses the neutral fallback.
```

- [ ] **Step 3: Run backend verification**

Run:

```powershell
dotnet test backend\Watchlist.sln --no-restore
```

Expected: all backend tests pass.

- [ ] **Step 4: Run Android verification**

Run:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

Expected: tests pass and debug APK builds.

- [ ] **Step 5: Manual smoke test**

Start the backend:

```powershell
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5000
```

In a second terminal, confirm the detail endpoint:

```powershell
Invoke-RestMethod http://localhost:5000/api/watchlist/movie-dune-part-two
```

Expected response includes `runtimeMinutes`, `genres`, `primaryActionLabel`, and backend-relative image URLs.

Launch the Android TV app on emulator/device and verify:

```text
Poster grid -> Select on poster -> details screen opens -> primary action focused -> Back returns to grid.
```

- [ ] **Step 6: Commit docs**

```powershell
git add -- docs/api.md docs/android-tv.md
git commit -m "docs: document details view api and tv flow"
```

---

## Final Verification

Before calling the implementation complete, run:

```powershell
git status --short
dotnet test backend\Watchlist.sln --no-restore
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

Expected:

- Only intentional files are modified.
- Backend tests pass.
- Android unit tests pass.
- Android debug APK builds.
- Any pre-existing unrelated untracked files remain uncommitted unless the user explicitly asks otherwise.
