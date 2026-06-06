# Plex-Owned Browse Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show Plex library movies and TV shows in the Android TV `On Plex` browse list even when they are not on the watchlist, with details clearly marking them as Plex-only.

**Architecture:** Keep watchlist records and Plex inventory separate. Add media-neutral Plex inventory/query contracts, then have the backend browse service combine watchlist matches with unmatched Plex inventory only when the Plex availability state is selected. Android consumes one browse/details API and uses `libraryMembership` to render Plex-only messaging.

**Tech Stack:** .NET 8 minimal API, C# application/infrastructure layers, MongoDB driver, Android Java TV client, JUnit tests.

---

## File Structure

- Modify `backend/src/Watchlist.Application/WatchlistItemDto.cs`: add `LibraryMembership`.
- Modify `backend/src/Watchlist.Application/WatchlistItemDetailsDto.cs`: add `LibraryMembership`.
- Create `backend/src/Watchlist.Application/LibraryMembership.cs`: backend membership enum.
- Create `backend/src/Watchlist.Application/PlexLibraryItemDto.cs`: media-neutral Plex inventory DTO.
- Modify `backend/src/Watchlist.Application/IPlexLibraryClient.cs`: add TV show retrieval and media-neutral return shape.
- Modify `backend/src/Watchlist.Application/IPlexMovieInventoryRepository.cs`: expand in place to media-neutral repository methods while preserving existing movie method wrappers.
- Modify `backend/src/Watchlist.Application/PlexMovieSyncService.cs`: scan movie and TV sections, apply inventory, match movies and TV.
- Create `backend/src/Watchlist.Application/PlexLibraryMatcher.cs`: media-neutral matching logic for movie and TV watchlist items.
- Modify `backend/src/Watchlist.Application/WatchlistQueryService.cs`: combine watchlist items and unmatched Plex inventory for Plex queries; resolve Plex-only details.
- Modify `backend/src/Watchlist.Infrastructure/PlexLibraryClient.cs`: parse TV sections and Plex metadata needed for list/details.
- Modify `backend/src/Watchlist.Infrastructure/MongoPlexLibraryItemDocument.cs`: store media-neutral fields including overview and image paths.
- Modify `backend/src/Watchlist.Infrastructure/MongoPlexMovieInventoryRepository.cs`: store/query movie and TV inventory and expose matched watchlist rating keys.
- Modify `backend/src/Watchlist.Api/Program.cs`: return `libraryMembership` and support Plex image proxy routes for stored Plex image paths.
- Modify backend tests under `backend/tests/Watchlist.Application.Tests` and `backend/tests/Watchlist.Api.Tests`.
- Modify `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`: add `libraryMembership`.
- Modify `android/app/src/main/java/com/watchlist/tv/WatchlistItemDetails.java`: add `libraryMembership` and helper for Plex-only status.
- Modify `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`: parse `libraryMembership`.
- Modify `android/app/src/main/java/com/watchlist/tv/DetailsActivity.java`: render Plex-only status line.
- Modify `android/app/src/main/res/values/strings.xml`: add Plex-only details text.
- Modify Android tests under `android/app/src/test/java/com/watchlist/tv`.
- Modify `docs/architecture.md`, `docs/integrations.md`, and `docs/android-tv.md`.

---

### Task 1: Add Membership To Browse DTOs

**Files:**
- Create: `backend/src/Watchlist.Application/LibraryMembership.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistItemDto.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistItemDetailsDto.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistQueryService.cs`
- Test: `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`

- [ ] **Step 1: Write failing tests for watchlist membership**

Add tests proving existing watchlist responses now expose membership:

```csharp
[Fact]
public async Task GetItemsAsync_returns_watchlist_membership_for_unmatched_watchlist_items()
{
    WatchlistQueryService service = new(new StubWatchlistReadRepository([
        SeedData.Movie(
            id: "movie-one",
            title: "Movie One",
            availabilityStatus: AvailabilityStatus.NotOnPlex)
    ]));

    IReadOnlyList<WatchlistItemDto> items = await service.GetItemsAsync(
        new WatchlistQuery(
            WatchlistCollection.All,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.NotOnPlex },
            WatchlistSort.AddedDescending),
        CancellationToken.None);

    Assert.Single(items);
    Assert.Equal("watchlist", items[0].LibraryMembership);
}

[Fact]
public async Task GetItemsAsync_returns_watchlist_and_plex_membership_for_available_watchlist_items()
{
    WatchlistQueryService service = new(new StubWatchlistReadRepository([
        SeedData.Movie(
            id: "movie-one",
            title: "Movie One",
            availabilityStatus: AvailabilityStatus.AvailableOnPlex)
    ]));

    IReadOnlyList<WatchlistItemDto> items = await service.GetItemsAsync(
        new WatchlistQuery(
            WatchlistCollection.All,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex },
            WatchlistSort.AddedDescending),
        CancellationToken.None);

    Assert.Single(items);
    Assert.Equal("watchlist_and_plex", items[0].LibraryMembership);
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter WatchlistQueryServiceTests
```

Expected: FAIL because `WatchlistItemDto` has no `LibraryMembership` property.

- [ ] **Step 3: Add membership enum and DTO fields**

Create `backend/src/Watchlist.Application/LibraryMembership.cs`:

```csharp
namespace Watchlist.Application;

public enum LibraryMembership
{
    Watchlist,
    WatchlistAndPlex,
    PlexOnly
}
```

Add `string LibraryMembership` to `WatchlistItemDto` after `AvailabilityStatus`.

Add `string LibraryMembership` to `WatchlistItemDetailsDto` after `AvailabilityStatus`.

- [ ] **Step 4: Map membership in `WatchlistQueryService`**

Add:

```csharp
private static string ToApiValue(LibraryMembership membership)
{
    return membership switch
    {
        LibraryMembership.Watchlist => "watchlist",
        LibraryMembership.WatchlistAndPlex => "watchlist_and_plex",
        LibraryMembership.PlexOnly => "plex_only",
        _ => "watchlist"
    };
}

private static LibraryMembership MembershipFor(WatchlistItem item)
{
    return item.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex
        ? LibraryMembership.WatchlistAndPlex
        : LibraryMembership.Watchlist;
}
```

Pass `ToApiValue(MembershipFor(item))` into both DTO constructors.

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter WatchlistQueryServiceTests
```

Expected: PASS.

Commit:

```powershell
git add backend/src/Watchlist.Application backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs
git commit -m "feat: expose library membership on watchlist items"
```

---

### Task 2: Introduce Media-Neutral Plex Inventory Contracts

**Files:**
- Create: `backend/src/Watchlist.Application/PlexLibraryItemDto.cs`
- Modify: `backend/src/Watchlist.Application/IPlexLibraryClient.cs`
- Modify: `backend/src/Watchlist.Application/IPlexMovieInventoryRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoPlexLibraryItemDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoPlexMovieInventoryRepository.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoPlexMovieInventoryRepositoryTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoPlexLibraryItemDocumentTests.cs`

- [ ] **Step 1: Write failing document mapping tests**

Add tests that map movie and TV Plex inventory documents:

```csharp
[Fact]
public void FromDto_maps_tv_show_metadata()
{
    DateTimeOffset seenAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z");
    PlexLibraryItemDto item = new(
        "show-1",
        MediaType.TvShow,
        "Example Show",
        2024,
        "Show summary",
        "/library/metadata/show-1/thumb/1",
        "/library/metadata/show-1/art/1",
        "2",
        "Seriale",
        "plex://show",
        "tt1234567",
        123,
        456);

    MongoPlexLibraryItemDocument document = MongoPlexLibraryItemDocument.FromDto(item, seenAt);

    Assert.Equal("plex-tv-show-1", document.Id);
    Assert.Equal(MediaType.TvShow, document.MediaType);
    Assert.Equal("Show summary", document.Summary);
    Assert.Equal("/library/metadata/show-1/thumb/1", document.PosterUrl);
    Assert.Equal("/library/metadata/show-1/art/1", document.BackdropUrl);
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter MongoPlexLibraryItemDocumentTests
```

Expected: FAIL because `PlexLibraryItemDto`, `Summary`, `PosterUrl`, and TV IDs do not exist.

- [ ] **Step 3: Add media-neutral DTO**

Create `backend/src/Watchlist.Application/PlexLibraryItemDto.cs`:

```csharp
using Watchlist.Domain;

namespace Watchlist.Application;

public sealed record PlexLibraryItemDto(
    string RatingKey,
    MediaType MediaType,
    string Title,
    int? Year,
    string? Summary,
    string? PosterUrl,
    string? BackdropUrl,
    string LibrarySectionKey,
    string LibrarySectionTitle,
    string? PlexGuid,
    string? ImdbId,
    int? TmdbId,
    int? TvdbId,
    DateTimeOffset LastSeenAt = default);
```

- [ ] **Step 4: Expand repository interface**

Add these members to `IPlexMovieInventoryRepository` without deleting existing method names yet:

```csharp
Task<PlexInventoryApplyResult> ApplyInventoryAsync(
    IReadOnlyList<PlexLibraryItemDto> items,
    IReadOnlySet<string> scannedSectionKeys,
    DateTimeOffset syncTime,
    CancellationToken cancellationToken);

Task<IReadOnlyList<PlexLibraryItemDto>> GetLibraryItemsAsync(CancellationToken cancellationToken);

Task<IReadOnlyList<WatchlistItemWriteModel>> GetWatchlistItemsForPlexMatchingAsync(
    CancellationToken cancellationToken);

Task<IReadOnlySet<string>> GetMatchedPlexRatingKeysAsync(CancellationToken cancellationToken);
```

- [ ] **Step 5: Update Mongo document mapping**

Add `Summary`, `PosterUrl`, and `BackdropUrl` properties to `MongoPlexLibraryItemDocument`.

Replace `FromDto(PlexMovieDto...)` with an overload for `PlexLibraryItemDto`:

```csharp
public static MongoPlexLibraryItemDocument FromDto(PlexLibraryItemDto item, DateTimeOffset lastSeenAt)
{
    string mediaPrefix = item.MediaType == MediaType.TvShow ? "tv" : "movie";
    return new MongoPlexLibraryItemDocument
    {
        Id = $"plex-{mediaPrefix}-{item.RatingKey}",
        RatingKey = item.RatingKey,
        MediaType = item.MediaType,
        Title = item.Title,
        Year = item.Year,
        Summary = item.Summary,
        PosterUrl = item.PosterUrl,
        BackdropUrl = item.BackdropUrl,
        LibrarySectionKey = item.LibrarySectionKey,
        LibrarySectionTitle = item.LibrarySectionTitle,
        PlexGuid = item.PlexGuid,
        ImdbId = item.ImdbId,
        TmdbId = item.TmdbId,
        TvdbId = item.TvdbId,
        LastSeenAt = lastSeenAt
    };
}

public PlexLibraryItemDto ToLibraryItemDto()
{
    return new PlexLibraryItemDto(
        RatingKey,
        MediaType,
        Title,
        Year,
        Summary,
        PosterUrl,
        BackdropUrl,
        LibrarySectionKey,
        LibrarySectionTitle,
        PlexGuid,
        ImdbId,
        TmdbId,
        TvdbId,
        LastSeenAt);
}
```

- [ ] **Step 6: Implement media-neutral Mongo repository methods**

In `MongoPlexMovieInventoryRepository`, implement `ApplyInventoryAsync`, `GetLibraryItemsAsync`, and `GetWatchlistItemsForPlexMatchingAsync`.

Use this delete filter:

```csharp
HashSet<string> currentDocumentIds = items
    .Select(item => MongoPlexLibraryItemDocument.FromDto(item, syncTime).Id)
    .ToHashSet(StringComparer.Ordinal);

DeleteResult deleteResult = await plexItems.DeleteManyAsync(
    filter.In(item => item.LibrarySectionKey, scannedSectionKeys)
    & filter.Nin(item => item.Id, currentDocumentIds),
    cancellationToken);
```

Use this watchlist filter:

```csharp
List<MongoWatchlistItemDocument> documents = await watchlistItems
    .Find(item => item.MediaType == MediaType.Movie || item.MediaType == MediaType.TvShow)
    .ToListAsync(cancellationToken);
```

Map documents to `WatchlistItemWriteModel` with `ImdbId`, `LetterboxdPath`, `TmdbId`, and `TvdbId`.

- [ ] **Step 7: Preserve compatibility wrappers**

Keep existing movie-only methods delegating to the media-neutral methods so current tests still compile:

```csharp
public async Task<PlexInventoryApplyResult> ApplyMovieInventoryAsync(
    IReadOnlyList<PlexMovieDto> movies,
    IReadOnlySet<string> scannedSectionKeys,
    DateTimeOffset syncTime,
    CancellationToken cancellationToken)
{
    return await ApplyInventoryAsync(
        movies.Select(movie => new PlexLibraryItemDto(
            movie.RatingKey,
            MediaType.Movie,
            movie.Title,
            movie.Year,
            null,
            null,
            null,
            movie.LibrarySectionKey,
            movie.LibrarySectionTitle,
            movie.PlexGuid,
            movie.ImdbId,
            movie.TmdbId,
            movie.TvdbId)).ToList(),
        scannedSectionKeys,
        syncTime,
        cancellationToken);
}
```

- [ ] **Step 8: Run tests and commit**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter "MongoPlex"
```

Expected: PASS.

Commit:

```powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/tests/Watchlist.Application.Tests
git commit -m "feat: generalize plex inventory model"
```

---

### Task 3: Parse Plex TV Show Inventory

**Files:**
- Modify: `backend/src/Watchlist.Application/IPlexLibraryClient.cs`
- Modify: `backend/src/Watchlist.Infrastructure/PlexLibraryClient.cs`
- Test: `backend/tests/Watchlist.Application.Tests/PlexLibraryClientTests.cs`

- [ ] **Step 1: Write failing TV parsing test**

Add a test using `TmdbTestHandlers` style fake HTTP responses:

```csharp
[Fact]
public async Task GetShowsAsync_parses_show_metadata_and_nested_guids()
{
    PlexLibrarySectionDto section = new("2", "show", "Seriale");
    FakePlexHandler handler = new()
        .WithXml("/library/sections/2/all?type=2&X-Plex-Token=token",
            """
            <MediaContainer>
              <Directory ratingKey="show-1" title="Example Show" year="2024" />
            </MediaContainer>
            """)
        .WithXml("/library/metadata/show-1?X-Plex-Token=token",
            """
            <MediaContainer>
              <Directory ratingKey="show-1" title="Example Show" year="2024" summary="Show summary" thumb="/thumb" art="/art" guid="plex://show">
                <Guid id="tmdb://123" />
                <Guid id="tvdb://456" />
                <Guid id="imdb://tt1234567" />
              </Directory>
            </MediaContainer>
            """);

    PlexLibraryClient client = CreateClient(handler);

    IReadOnlyList<PlexLibraryItemDto> shows = await client.GetShowsAsync(section, CancellationToken.None);

    PlexLibraryItemDto show = Assert.Single(shows);
    Assert.Equal(MediaType.TvShow, show.MediaType);
    Assert.Equal("Example Show", show.Title);
    Assert.Equal("Show summary", show.Summary);
    Assert.Equal(123, show.TmdbId);
    Assert.Equal(456, show.TvdbId);
    Assert.Equal("tt1234567", show.ImdbId);
}
```

- [ ] **Step 2: Run test and verify failure**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter GetShowsAsync_parses_show_metadata_and_nested_guids
```

Expected: FAIL because `GetShowsAsync` does not exist.

- [ ] **Step 3: Add client method signature**

In `IPlexLibraryClient`, add:

```csharp
Task<IReadOnlyList<PlexLibraryItemDto>> GetShowsAsync(
    PlexLibrarySectionDto section,
    CancellationToken cancellationToken);
```

- [ ] **Step 4: Implement TV parsing**

In `PlexLibraryClient`, add `GetShowsAsync` using `/library/sections/{sectionKey}/all?type=2`, fetch each metadata item, and parse `Directory` nodes.

Extract GUID parsing into:

```csharp
private static (string? ImdbId, int? TmdbId, int? TvdbId) ParseExternalIds(XElement element)
```

Use it from both movie and show mapping.

Create:

```csharp
private static PlexLibraryItemDto ToShow(PlexLibrarySectionDto section, XElement directory)
{
    (string? imdbId, int? tmdbId, int? tvdbId) = ParseExternalIds(directory);
    return new PlexLibraryItemDto(
        RequiredAttribute(directory, "ratingKey"),
        MediaType.TvShow,
        RequiredAttribute(directory, "title"),
        OptionalIntAttribute(directory, "year"),
        directory.Attribute("summary")?.Value,
        directory.Attribute("thumb")?.Value,
        directory.Attribute("art")?.Value,
        section.Key,
        section.Title,
        directory.Attribute("guid")?.Value,
        imdbId,
        tmdbId,
        tvdbId);
}
```

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter PlexLibraryClientTests
```

Expected: PASS.

Commit:

```powershell
git add backend/src/Watchlist.Application/IPlexLibraryClient.cs backend/src/Watchlist.Infrastructure/PlexLibraryClient.cs backend/tests/Watchlist.Application.Tests/PlexLibraryClientTests.cs
git commit -m "feat: parse plex tv inventory"
```

---

### Task 4: Match Plex Inventory Against Movie And TV Watchlist Items

**Files:**
- Create: `backend/src/Watchlist.Application/PlexLibraryMatcher.cs`
- Modify: `backend/src/Watchlist.Application/PlexMovieSyncService.cs`
- Test: `backend/tests/Watchlist.Application.Tests/PlexMovieMatcherTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/PlexMovieSyncServiceTests.cs`

- [ ] **Step 1: Write failing TV matcher tests**

Add tests for TMDB, TVDB, IMDb, title/year, and ambiguity:

```csharp
[Fact]
public void Match_matches_tv_by_tvdb_id()
{
    WatchlistItemWriteModel watchlistShow = new(
        SeedData.TvShow("tv-one", "Example Show", 2024),
        null,
        null,
        null,
        456);

    PlexLibraryItemDto plexShow = new(
        "show-1", MediaType.TvShow, "Different Title", 2024, null, null, null,
        "2", "Seriale", null, null, null, 456);

    PlexMatchResult result = PlexLibraryMatcher.Match(watchlistShow, [plexShow]);

    Assert.Equal(AvailabilityStatus.AvailableOnPlex, result.AvailabilityStatus);
    Assert.Equal("show-1", result.PlexRatingKey);
    Assert.Equal("tvdb", result.MatchReason);
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter PlexMovieMatcherTests
```

Expected: FAIL because `PlexLibraryMatcher` does not exist.

- [ ] **Step 3: Implement media-neutral matcher**

Create `PlexLibraryMatcher` by moving the normalization and matching logic from `PlexMovieMatcher`.

Order:

1. IMDb ID exact match.
2. TMDB ID exact match.
3. TVDB ID exact match.
4. normalized title plus exact year.
5. ambiguous title/year returns `AvailabilityStatus.UnknownMatch`.
6. no match returns `Unreleased` for unreleased items and `NotOnPlex` otherwise.

For movie watchlist items, behavior must remain identical to `PlexMovieMatcher`.

- [ ] **Step 4: Update sync service to scan movies and TV**

In `PlexMovieSyncService.SyncMoviesAsync`, build a media-neutral source list:

```csharp
List<PlexLibraryItemDto> sourceItems = [];
foreach (PlexLibrarySectionDto section in movieSections)
{
    sourceItems.AddRange((await client.GetMoviesAsync(section, cancellationToken)).Select(ToLibraryItem));
}
foreach (PlexLibrarySectionDto section in tvSections)
{
    sourceItems.AddRange(await client.GetShowsAsync(section, cancellationToken));
}
```

Add `tvSections` where `section.Type == "show"`.

Use `repository.ApplyInventoryAsync`, `repository.GetLibraryItemsAsync`, and `repository.GetWatchlistItemsForPlexMatchingAsync`.

Filter candidates inside the matcher call:

```csharp
IReadOnlyList<PlexLibraryItemDto> candidates = plexItems
    .Where(plexItem => plexItem.MediaType == item.Item.MediaType)
    .ToList();
```

- [ ] **Step 5: Preserve response shape**

Keep `PlexMovieSyncResultDto` unchanged for this task. Set `SectionsScanned` to movie section count plus TV section count and `ItemsFetched` to all source items. Rename can happen in a later cleanup after API behavior is stable.

- [ ] **Step 6: Run tests and commit**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter "PlexMovieMatcherTests|PlexMovieSyncServiceTests"
```

Expected: PASS.

Commit:

```powershell
git add backend/src/Watchlist.Application backend/tests/Watchlist.Application.Tests
git commit -m "feat: match plex library across movies and tv"
```

---

### Task 5: Combine Plex-Only Items In Backend Browse And Details

**Files:**
- Modify: `backend/src/Watchlist.Application/WatchlistQueryService.cs`
- Modify: `backend/src/Watchlist.Application/IWatchlistReadRepository.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Test: `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`
- Test: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Write failing combined browse tests**

Add tests:

```csharp
[Fact]
public async Task GetItemsAsync_with_plex_availability_includes_unmatched_plex_items()
{
    StubWatchlistReadRepository watchlist = new([
        SeedData.Movie("movie-watchlist", "Watchlist Movie", AvailabilityStatus.AvailableOnPlex)
    ]);
    StubPlexInventoryReadRepository plex = new([
        new PlexLibraryItemDto("plex-extra", MediaType.Movie, "Plex Extra", 2020, "Summary", null, null, "1", "Filmy", null, null, null, null)
    ]);

    WatchlistQueryService service = new(watchlist, plex);

    IReadOnlyList<WatchlistItemDto> items = await service.GetItemsAsync(
        new WatchlistQuery(
            WatchlistCollection.Movie,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex },
            WatchlistSort.TitleAscending),
        CancellationToken.None);

    Assert.Collection(
        items,
        item => Assert.Equal("plex_only", item.LibraryMembership),
        item => Assert.Equal("watchlist_and_plex", item.LibraryMembership));
}
```

- [ ] **Step 2: Write failing Plex-only details test**

Add:

```csharp
[Fact]
public async Task GetItemDetailsAsync_returns_plex_only_details()
{
    StubPlexInventoryReadRepository plex = new([
        new PlexLibraryItemDto("plex-extra", MediaType.Movie, "Plex Extra", 2020, "Summary", "/thumb", "/art", "1", "Filmy", null, null, null, null)
    ]);
    WatchlistQueryService service = new(new StubWatchlistReadRepository([]), plex);

    WatchlistItemDetailsDto? details = await service.GetItemDetailsAsync("plex-movie-plex-extra", CancellationToken.None);

    Assert.NotNull(details);
    Assert.Equal("plex_only", details.LibraryMembership);
    Assert.Equal("plex", details.Source);
    Assert.False(details.PrimaryActionEnabled);
}
```

- [ ] **Step 3: Run tests and verify failure**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter WatchlistQueryServiceTests
```

Expected: FAIL because `WatchlistQueryService` does not accept Plex inventory.

- [ ] **Step 4: Add query service dependency**

Change constructor to:

```csharp
public sealed class WatchlistQueryService(
    IWatchlistReadRepository repository,
    IPlexMovieInventoryRepository plexRepository)
```

Update tests that construct `WatchlistQueryService` to pass an empty stub Plex repository.

- [ ] **Step 5: Add Plex-only projection**

In `WatchlistQueryService`, add:

```csharp
private static string PlexOnlyId(PlexLibraryItemDto item)
{
    string media = item.MediaType == MediaType.TvShow ? "tv" : "movie";
    return $"plex-{media}-{item.RatingKey}";
}
```

Build Plex-only items only when `query.Availability.Contains(AvailabilityStatus.AvailableOnPlex)`.

Exclude matched inventory using repository-owned sync trace data:

```csharp
IReadOnlySet<string> matchedRatingKeys =
    await plexRepository.GetMatchedPlexRatingKeysAsync(cancellationToken);
```

`GetMatchedPlexRatingKeysAsync` should read non-empty `PlexRatingKey` values from MongoDB watchlist documents where `AvailabilityStatus == AvailableOnPlex`. Do not add Plex sync trace fields to the public `WatchlistItem` domain record.

- [ ] **Step 6: Add Plex-only DTO mapping**

Create DTOs with:

```csharp
new WatchlistItemDto(
    PlexOnlyId(item),
    ToApiValue(item.MediaType),
    "plex",
    item.RatingKey,
    item.Title,
    item.Year,
    item.Summary,
    item.PosterUrl,
    item.BackdropUrl,
    "unknown",
    "available_on_plex",
    "plex_only",
    false,
    false,
    [],
    ["plex"],
    item.LastSeenAt,
    item.LastSeenAt)
```

- [ ] **Step 7: Support Plex-only details IDs**

In `GetItemDetailsAsync`, after watchlist lookup fails, look up Plex inventory by generated ID. Return a details DTO with:

```csharp
PrimaryActionLabel = "Unavailable";
PrimaryActionEnabled = false;
PrimaryActionTarget = null;
LibraryMembership = "plex_only";
```

- [ ] **Step 8: Run backend tests and commit**

Run:

```powershell
dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter WatchlistQueryServiceTests
dotnet test backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj --filter WatchlistApiTests
```

Expected: PASS.

Commit:

```powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Api backend/tests/Watchlist.Application.Tests backend/tests/Watchlist.Api.Tests
git commit -m "feat: include plex-only items in plex browse"
```

---

### Task 6: Add Plex Image Proxy For Inventory Artwork

**Files:**
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistQueryService.cs`
- Test: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] **Step 1: Write failing image URL test**

Add an API test asserting Plex-only browse uses backend image URLs:

```csharp
[Fact]
public async Task Watchlist_plex_only_item_uses_backend_plex_image_urls()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.GetAsync("/api/watchlist?availability=plex&sort=title_asc");

    response.EnsureSuccessStatusCode();
    string json = await response.Content.ReadAsStringAsync();
    Assert.Contains("/api/images/plex/", json, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run test and verify failure**

Run:

```powershell
dotnet test backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj --filter Watchlist_plex_only_item_uses_backend_plex_image_urls
```

Expected: FAIL because Plex image URLs are raw Plex paths or missing in seeded data.

- [ ] **Step 3: Convert Plex paths to backend image URLs**

In `WatchlistQueryService`, map Plex paths with:

```csharp
private static string? ToBackendPlexImageUrl(string ratingKey, string kind, string? plexPath)
{
    return string.IsNullOrWhiteSpace(plexPath)
        ? null
        : $"/api/images/plex/{Uri.EscapeDataString(ratingKey)}/{kind}";
}
```

Use `kind` values `poster` and `backdrop`.

- [ ] **Step 4: Add image proxy endpoints**

In `Program.cs`, add:

```csharp
app.MapGet("/api/images/plex/{ratingKey}/{kind}", async (
    string ratingKey,
    string kind,
    IPlexMovieInventoryRepository repository,
    IHttpClientFactory httpClientFactory,
    IOptions<PlexOptions> options,
    CancellationToken cancellationToken) =>
{
    if (kind is not ("poster" or "backdrop"))
    {
        return Results.BadRequest();
    }

    PlexLibraryItemDto? item = (await repository.GetLibraryItemsAsync(cancellationToken))
        .FirstOrDefault(item => string.Equals(item.RatingKey, ratingKey, StringComparison.Ordinal));
    string? plexPath = kind == "poster" ? item?.PosterUrl : item?.BackdropUrl;
    if (string.IsNullOrWhiteSpace(plexPath))
    {
        return Results.NotFound();
    }

    HttpClient httpClient = httpClientFactory.CreateClient();
    string separator = plexPath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
    Uri imageUri = new($"{options.Value.BaseUrl.TrimEnd('/')}{plexPath}{separator}X-Plex-Token={Uri.EscapeDataString(options.Value.Token)}");
    using HttpResponseMessage response = await httpClient.GetAsync(imageUri, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
    string contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
    return Results.File(bytes, contentType);
});
```

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj
```

Expected: PASS.

Commit:

```powershell
git add backend/src/Watchlist.Api backend/src/Watchlist.Application backend/tests/Watchlist.Api.Tests
git commit -m "feat: proxy plex artwork"
```

---

### Task 7: Parse And Render Android Library Membership

**Files:**
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistItemDetails.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/DetailsActivity.java`
- Modify: `android/app/src/main/res/values/strings.xml`
- Test: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`
- Test: `android/app/src/test/java/com/watchlist/tv/WatchlistItemDetailsTest.java`

- [ ] **Step 1: Write failing parser tests**

Add:

```java
@Test
public void parseItems_readsLibraryMembership() throws Exception {
    List<WatchlistItem> items = WatchlistApiClient.parseItems("[{"
            + "\"id\":\"plex-movie-1\","
            + "\"mediaType\":\"movie\","
            + "\"source\":\"plex\","
            + "\"sourceId\":\"1\","
            + "\"title\":\"Plex Movie\","
            + "\"year\":2024,"
            + "\"overview\":null,"
            + "\"posterUrl\":null,"
            + "\"backdropUrl\":null,"
            + "\"releaseStatus\":\"unknown\","
            + "\"availabilityStatus\":\"available_on_plex\","
            + "\"libraryMembership\":\"plex_only\","
            + "\"vodReleaseKnown\":false,"
            + "\"releasedOnVod\":false,"
            + "\"vodRegions\":[],"
            + "\"ownedServiceAvailability\":[\"plex\"],"
            + "\"addedAt\":\"2026-06-06T10:00:00Z\","
            + "\"updatedAt\":\"2026-06-06T10:00:00Z\""
            + "}]");

    assertEquals("plex_only", items.get(0).libraryMembership());
}

@Test
public void parseItems_defaultsMissingLibraryMembershipToWatchlist() throws Exception {
    List<WatchlistItem> items = WatchlistApiClient.parseItems("[{"
            + "\"id\":\"movie-1\","
            + "\"mediaType\":\"movie\","
            + "\"source\":\"letterboxd\","
            + "\"sourceId\":\"movie-1\","
            + "\"title\":\"Movie\","
            + "\"year\":2024,"
            + "\"overview\":null,"
            + "\"posterUrl\":null,"
            + "\"backdropUrl\":null,"
            + "\"releaseStatus\":\"released\","
            + "\"availabilityStatus\":\"not_on_plex\","
            + "\"addedAt\":\"2026-06-06T10:00:00Z\","
            + "\"updatedAt\":\"2026-06-06T10:00:00Z\""
            + "}]");

    assertEquals("watchlist", items.get(0).libraryMembership());
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
cd android
.\gradlew.bat testDebugUnitTest --tests "com.watchlist.tv.WatchlistApiClientTest"
```

Expected: FAIL because `libraryMembership()` does not exist.

- [ ] **Step 3: Add Android model field**

Add constants to `WatchlistItem`:

```java
public static final String MEMBERSHIP_WATCHLIST = "watchlist";
public static final String MEMBERSHIP_WATCHLIST_AND_PLEX = "watchlist_and_plex";
public static final String MEMBERSHIP_PLEX_ONLY = "plex_only";
```

Add `libraryMembership` constructor parameter, field, and getter. Existing convenience constructors should pass `MEMBERSHIP_WATCHLIST`.

- [ ] **Step 4: Add details model field and helper**

Add `libraryMembership` to `WatchlistItemDetails`, pass it through `fromItem`, and add:

```java
public boolean isPlexOnly() {
    return WatchlistItem.MEMBERSHIP_PLEX_ONLY.equals(libraryMembership);
}
```

- [ ] **Step 5: Parse membership in API client**

In both item and details parsing, use:

```java
item.optString("libraryMembership", WatchlistItem.MEMBERSHIP_WATCHLIST)
```

- [ ] **Step 6: Render Plex-only status in details**

In `strings.xml`, add:

```xml
<string name="message_plex_only_detail">In Plex library. Not on your watchlist.</string>
```

In `DetailsActivity`, add a `TextView membershipView` between metadata and primary action. In `render`, set:

```java
membershipView.setText(details.isPlexOnly() ? R.string.message_plex_only_detail : 0);
membershipView.setVisibility(details.isPlexOnly() ? View.VISIBLE : View.GONE);
```

Use `Color.rgb(203, 213, 225)`, `setTextSize(17)`, and top padding `dp(14)`.

- [ ] **Step 7: Run Android tests and commit**

Run:

```powershell
cd android
.\gradlew.bat testDebugUnitTest
```

Expected: PASS.

Commit:

```powershell
git add android/app/src/main/java/com/watchlist/tv android/app/src/main/res/values/strings.xml android/app/src/test/java/com/watchlist/tv
git commit -m "feat: show plex-only membership on android tv"
```

---

### Task 8: Update Documentation

**Files:**
- Modify: `docs/architecture.md`
- Modify: `docs/integrations.md`
- Modify: `docs/android-tv.md`

- [ ] **Step 1: Update architecture docs**

In `docs/architecture.md`, add a backend read model note:

```markdown
`GET /api/watchlist` is the Android browse endpoint. It is watchlist-only by default, but when the Plex availability state is selected the backend combines watchlist items matched to Plex with unmatched Plex inventory items. The `libraryMembership` field distinguishes `watchlist`, `watchlist_and_plex`, and `plex_only` records so Plex-only inventory does not become watchlist data.
```

- [ ] **Step 2: Update Plex integration docs**

In `docs/integrations.md`, document:

```markdown
Plex sync scans movie sections and TV show sections. Movie entries are synced at movie level. TV entries are synced at show level; season and episode completeness are not modeled in v1. Plex show matching prefers TMDB, then TVDB, then IMDb, then exact normalized title plus year.
```

- [ ] **Step 3: Update Android TV docs**

In `docs/android-tv.md`, document:

```markdown
The `On Plex` rail item shows everything available in Plex for the selected collection: watchlist items that matched Plex plus Plex-only library items. Plex-only details show `In Plex library. Not on your watchlist.` The UI remains read-only and does not add Plex-only items to Letterboxd or TMDB watchlists.
```

- [ ] **Step 4: Run documentation checks**

Run:

```powershell
git diff --check -- docs/architecture.md docs/integrations.md docs/android-tv.md
```

Expected: no output.

- [ ] **Step 5: Commit**

```powershell
git add docs/architecture.md docs/integrations.md docs/android-tv.md
git commit -m "docs: describe plex-owned browse behavior"
```

---

### Task 9: Full Verification

**Files:**
- No source edits expected.

- [ ] **Step 1: Run backend tests**

Run:

```powershell
dotnet test backend/Watchlist.sln
```

Expected: PASS.

- [ ] **Step 2: Run Android unit tests**

Run:

```powershell
cd android
.\gradlew.bat testDebugUnitTest
```

Expected: PASS.

- [ ] **Step 3: Run manual backend smoke test**

Start the backend, then run:

```powershell
Invoke-RestMethod -Method Post http://localhost:5000/api/sync/plex/movies
Invoke-RestMethod 'http://localhost:5000/api/watchlist?collection=all&availability=plex&sort=title_asc'
```

Expected: the browse response contains at least one `libraryMembership` value of `watchlist_and_plex` or `plex_only` when matching local data exists.

- [ ] **Step 4: Manual Android TV check**

Run the Android TV app and verify:

- Select `On Plex`: watchlist-and-Plex items and Plex-only items appear in the same grid.
- Select `Movies`: Plex-only TV shows disappear.
- Select `TV Shows`: Plex-only movies disappear.
- Open a Plex-only item: details show `In Plex library. Not on your watchlist.`
- No create, edit, delete, reorder, or watchlist mutation controls are visible.

- [ ] **Step 5: Final commit if verification fixes were needed**

If verification required fixes, commit the fix with a focused message. If no fixes were needed, do not create an empty commit.
