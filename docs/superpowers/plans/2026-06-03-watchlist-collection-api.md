# Watchlist Collection API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current watchlist query contract with backend-owned collection, availability, and sort controls, then migrate Android TV to use that contract.

**Architecture:** Extend the backend read model with `AddedAt`, introduce collection-query value objects in the application layer, and make `GET /api/watchlist` parse the new breaking query contract. Android keeps its current UI but constructs backend query parameters instead of filtering and sorting locally.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, MongoDB.Driver, xUnit, FluentAssertions, Android Java 17, JUnit 4.

---

### Task 1: Add `AddedAt` To The Read Model

**Files:**
- Modify: `backend/src/Watchlist.Domain/WatchlistItem.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistItemDto.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`
- Modify: `backend/src/Watchlist.Infrastructure/SeedData.cs`
- Modify: `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`
- Modify: `backend/tests/Watchlist.Application.Tests/SeedDataTests.cs`
- Modify: `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`

- [ ] Write failing tests for `AddedAt` mapping and legacy MongoDB fallback:

```csharp
[Fact]
public void ToDomain_WhenDocumentHasAddedAt_MapsAddedAtAndUpdatedAt()
{
    DateTimeOffset addedAt = DateTimeOffset.Parse("2026-05-20T10:00:00+02:00");
    DateTimeOffset updatedAt = DateTimeOffset.Parse("2026-05-25T10:00:00+02:00");
    MongoWatchlistItemDocument document = new()
    {
        Id = "movie-example",
        MediaType = MediaType.Movie,
        Source = WatchlistSource.Letterboxd,
        SourceId = "letterboxd-example",
        Title = "Example Movie",
        Year = 2025,
        Overview = "Overview",
        PosterUrl = "https://example.com/poster.jpg",
        BackdropUrl = "https://example.com/backdrop.jpg",
        ReleaseStatus = ReleaseStatus.Released,
        AvailabilityStatus = AvailabilityStatus.AvailableOnPlex,
        AddedAt = addedAt,
        UpdatedAt = updatedAt
    };

    WatchlistItem item = document.ToDomain();

    item.AddedAt.Should().Be(addedAt);
    item.UpdatedAt.Should().Be(updatedAt);
}

[Fact]
public void ToDomain_WhenDocumentHasNoAddedAt_UsesUpdatedAtForLocalCompatibility()
{
    DateTimeOffset updatedAt = DateTimeOffset.Parse("2026-05-25T10:00:00+02:00");
    MongoWatchlistItemDocument document = new()
    {
        Id = "movie-example",
        MediaType = MediaType.Movie,
        Source = WatchlistSource.Letterboxd,
        SourceId = "letterboxd-example",
        Title = "Example Movie",
        Year = 2025,
        Overview = "Overview",
        PosterUrl = "https://example.com/poster.jpg",
        BackdropUrl = "https://example.com/backdrop.jpg",
        ReleaseStatus = ReleaseStatus.Released,
        AvailabilityStatus = AvailabilityStatus.AvailableOnPlex,
        UpdatedAt = updatedAt
    };

    WatchlistItem item = document.ToDomain();

    item.AddedAt.Should().Be(updatedAt);
}
```

- [ ] Run `dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter "MongoWatchlistItemDocumentTests|SeedDataTests|WatchlistQueryServiceTests"` and verify failure because `AddedAt` is not part of the domain/DTO/document model.
- [ ] Add `DateTimeOffset AddedAt` to `WatchlistItem` immediately before `UpdatedAt`.
- [ ] Add `DateTimeOffset AddedAt` to `WatchlistItemDto` immediately before `UpdatedAt`.
- [ ] Add nullable `DateTimeOffset? AddedAt` to `MongoWatchlistItemDocument`; map `AddedAt ?? UpdatedAt` in `ToDomain()` and write `AddedAt = item.AddedAt` in `FromDomain()`.
- [ ] Update seed records with deterministic `AddedAt` values:
  - `movie-dune-part-two`: `2026-05-20T10:00:00+02:00`
  - `movie-unreleased-example`: `2026-05-21T10:00:00+02:00`
  - `tv-andor`: `2026-05-22T10:00:00+02:00`
- [ ] Update every test helper that constructs `WatchlistItem` to pass `AddedAt`.
- [ ] Re-run the focused tests and verify they pass.
- [ ] Commit with `feat: add watchlist added timestamp`.

### Task 2: Add Backend Collection Query Semantics

**Files:**
- Create: `backend/src/Watchlist.Application/WatchlistCollection.cs`
- Create: `backend/src/Watchlist.Application/WatchlistSort.cs`
- Create: `backend/src/Watchlist.Application/WatchlistQuery.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistQueryService.cs`
- Modify: `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`

- [ ] Write failing service tests for the new query behavior:

```csharp
[Fact]
public async Task GetItemsAsync_WhenCollectionAll_ReturnsMoviesAndTv()
{
    IReadOnlyList<WatchlistItem> items =
    [
        CreateItem("old-movie", MediaType.Movie, "Dune: Part Two", DateTimeOffset.Parse("2026-05-20T10:00:00+02:00")),
        CreateItem("new-tv", MediaType.TvShow, "Andor", DateTimeOffset.Parse("2026-05-22T10:00:00+02:00"))
    ];
    WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
    WatchlistQuery query = new(
        WatchlistCollection.All,
        [AvailabilityStatus.AvailableOnPlex, AvailabilityStatus.NotOnPlex],
        WatchlistSort.AddedDescending);

    IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

    result.Select(item => item.Id).Should().Equal("new-tv", "old-movie");
}

[Fact]
public async Task GetItemsAsync_WhenAvailabilityHasSplitReasons_ReturnsOnlyRequestedStates()
{
    IReadOnlyList<WatchlistItem> items =
    [
        CreateItem("available", MediaType.Movie, "Available", DateTimeOffset.Parse("2026-05-20T10:00:00+02:00"), AvailabilityStatus.AvailableOnPlex),
        CreateItem("missing", MediaType.Movie, "Missing", DateTimeOffset.Parse("2026-05-21T10:00:00+02:00"), AvailabilityStatus.NotOnPlex),
        CreateItem("uncertain", MediaType.TvShow, "Uncertain", DateTimeOffset.Parse("2026-05-22T10:00:00+02:00"), AvailabilityStatus.UnknownMatch)
    ];
    WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
    WatchlistQuery query = new(
        WatchlistCollection.All,
        [AvailabilityStatus.AvailableOnPlex, AvailabilityStatus.UnknownMatch],
        WatchlistSort.AddedDescending);

    IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

    result.Should().OnlyContain(item =>
        item.AvailabilityStatus is "available_on_plex" or "unknown_match");
}

[Fact]
public async Task GetItemsAsync_WhenSortTitleAsc_SortsCaseInsensitively()
{
    IReadOnlyList<WatchlistItem> items =
    [
        CreateItem("future", MediaType.Movie, "Future Movie", DateTimeOffset.Parse("2026-05-21T10:00:00+02:00"), AvailabilityStatus.Unreleased),
        CreateItem("dune", MediaType.Movie, "Dune: Part Two", DateTimeOffset.Parse("2026-05-20T10:00:00+02:00"), AvailabilityStatus.AvailableOnPlex),
        CreateItem("andor", MediaType.TvShow, "Andor", DateTimeOffset.Parse("2026-05-22T10:00:00+02:00"), AvailabilityStatus.NotOnPlex)
    ];
    WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
    WatchlistQuery query = new(
        WatchlistCollection.All,
        [AvailabilityStatus.AvailableOnPlex, AvailabilityStatus.NotOnPlex, AvailabilityStatus.Unreleased, AvailabilityStatus.UnknownMatch],
        WatchlistSort.TitleAscending);

    IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

    result.Select(item => item.Title).Should().Equal("Andor", "Dune: Part Two", "Future Movie");
}
```

- [ ] Run `dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter WatchlistQueryServiceTests` and verify failure because `WatchlistQuery`, `WatchlistCollection`, and `WatchlistSort` do not exist.
- [ ] Add `WatchlistCollection` enum with `All`, `Movie`, and `Tv`.
- [ ] Add `WatchlistSort` enum with `AddedDescending` and `TitleAscending`.
- [ ] Add `WatchlistQuery` sealed record with `WatchlistCollection Collection`, `IReadOnlySet<AvailabilityStatus> Availability`, and `WatchlistSort Sort`.
- [ ] Replace `WatchlistQueryService.GetItemsAsync(MediaType?, WatchlistFilter, ...)` with `GetItemsAsync(WatchlistQuery query, CancellationToken cancellationToken)`.
- [ ] Implement collection filtering:
  - `All`: no media filter.
  - `Movie`: `MediaType.Movie`.
  - `Tv`: `MediaType.TvShow`.
- [ ] Implement availability filtering using the query set.
- [ ] Implement sorting:
  - `AddedDescending`: `OrderByDescending(item => item.AddedAt)` only, relying on LINQ stable ordering to preserve repository order for equal timestamps.
  - `TitleAscending`: `OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)`.
- [ ] Ensure `ToDto()` includes both `AddedAt` and `UpdatedAt`.
- [ ] Keep `GetItemAsync` behavior unchanged except for DTO `AddedAt`.
- [ ] Re-run focused application tests and verify they pass.
- [ ] Commit with `feat: add watchlist collection query`.

### Task 3: Replace API Query Parsing And Validation

**Files:**
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/MongoUnavailableApiFactory.cs`

- [ ] Write failing API tests for the new breaking contract:

```csharp
[Fact]
public async Task GetWatchlist_WhenDefaultQuery_ReturnsAllItemsWithAddedAt()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.GetAsync("/api/watchlist");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    JsonElement items = document.RootElement;
    items.EnumerateArray().Should().Contain(item => item.GetProperty("mediaType").GetString() == "movie");
    items.EnumerateArray().Should().Contain(item => item.GetProperty("mediaType").GetString() == "tv");
    items[0].TryGetProperty("addedAt", out _).Should().BeTrue();
}

[Fact]
public async Task GetWatchlist_WhenCollectionTv_ReturnsTvShows()
{
    using SeededApiFactory factory = new();
    HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.GetAsync("/api/watchlist?collection=tv");
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using JsonDocument document = await ReadJsonDocumentAsync(response);
    JsonElement items = document.RootElement;

    items.EnumerateArray().Should().OnlyContain(item => item.GetProperty("mediaType").GetString() == "tv");
}

[Theory]
[InlineData("/api/watchlist?collection=music", "Invalid collection.")]
[InlineData("/api/watchlist?availability=plex,bad", "Invalid availability.")]
[InlineData("/api/watchlist?availability=", "Invalid availability.")]
[InlineData("/api/watchlist?sort=random", "Invalid sort.")]
public async Task GetWatchlist_WhenQueryInvalid_ReturnsBadRequest(string url, string error)
{
    HttpResponseMessage response = await client.GetAsync(url);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

- [ ] Run `dotnet test backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj --filter GetWatchlist` and verify failure because the API still expects `mediaType` and `filter`.
- [ ] Replace the `/api/watchlist` parameters with `string? collection`, `string? availability`, and `string? sort`.
- [ ] Parse `collection`:
  - missing or `all` -> `WatchlistCollection.All`
  - `movie` -> `WatchlistCollection.Movie`
  - `tv` -> `WatchlistCollection.Tv`
  - anything else -> `400 { error = "Invalid collection." }`
- [ ] Parse `availability`:
  - missing -> all four states.
  - comma-separated values with no empty entries.
  - `plex` -> `AvailabilityStatus.AvailableOnPlex`
  - `not_on_plex` -> `AvailabilityStatus.NotOnPlex`
  - `unreleased` -> `AvailabilityStatus.Unreleased`
  - `unknown_match` -> `AvailabilityStatus.UnknownMatch`
  - unknown or empty list -> `400 { error = "Invalid availability." }`
- [ ] Parse `sort`:
  - missing or `added_desc` -> `WatchlistSort.AddedDescending`
  - `title_asc` -> `WatchlistSort.TitleAscending`
  - anything else -> `400 { error = "Invalid sort." }`
- [ ] Construct `WatchlistQuery` and call the new service method.
- [ ] Update `MongoUnavailableApiFactory` test URL to `/api/watchlist?collection=movie`.
- [ ] Re-run focused API tests and verify they pass.
- [ ] Commit with `feat: replace watchlist api query contract`.

### Task 4: Update MongoDB And Seed API Documentation

**Files:**
- Modify: `docs/api.md`
- Modify: `docs/architecture.md`
- Modify: `docs/todo.md`

- [ ] Update `docs/api.md` to document the breaking `/api/watchlist` query contract:
  - `collection`
  - `availability`
  - `sort`
  - default behavior
  - `addedAt` and `updatedAt`
  - validation errors.
- [ ] Update `docs/architecture.md` initial API surface to list:

```text
GET /api/watchlist?collection=all|movie|tv&availability=plex,not_on_plex,unreleased,unknown_match&sort=added_desc|title_asc
GET /api/watchlist/{id}
GET /api/sync/status
```

- [ ] Update `docs/todo.md`:
  - Mark combined `All` query complete.
  - Mark stable `addedAt` API data complete for seeded/read-model records.
  - Mark source-aware split availability filtering complete for backend Plex/unavailable reasons.
  - Keep subscribed streaming-service availability open.
- [ ] Run `git diff --check`.
- [ ] Commit with `docs: document watchlist collection api`.

### Task 5: Migrate Android TV Request Construction

**Files:**
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/BrowsingState.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`
- Modify: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`
- Modify: `android/app/src/test/java/com/watchlist/tv/BrowsingStateTest.java`

- [ ] Write failing Android unit tests for URL construction and DTO parsing:

```java
@Test
public void buildWatchlistPath_whenAllPlexOnlyDateAdded_usesCollectionApiContract() {
    String path = WatchlistApiClient.buildWatchlistPath(
            BrowsingState.MEDIA_ALL,
            CollectionOrganizer.SORT_DATE_ADDED,
            false);

    assertEquals("/api/watchlist?collection=all&availability=plex&sort=added_desc", path);
}

@Test
public void buildWatchlistPath_whenUnavailableIncluded_expandsSplitAvailabilityReasons() {
    String path = WatchlistApiClient.buildWatchlistPath(
            BrowsingState.MEDIA_TV,
            CollectionOrganizer.SORT_ALPHABETICAL,
            true);

    assertEquals("/api/watchlist?collection=tv&availability=plex,not_on_plex,unreleased,unknown_match&sort=title_asc", path);
}

@Test
public void parseItems_parsesAddedAtAndUpdatedAt() {
    String json = "[{"
            + "\"id\":\"movie-dune-part-two\","
            + "\"mediaType\":\"movie\","
            + "\"source\":\"letterboxd\","
            + "\"sourceId\":\"letterboxd-dune-part-two\","
            + "\"title\":\"Dune: Part Two\","
            + "\"year\":2024,"
            + "\"overview\":\"Overview\","
            + "\"posterUrl\":\"https://image.example/poster.jpg\","
            + "\"backdropUrl\":\"https://image.example/backdrop.jpg\","
            + "\"releaseStatus\":\"released\","
            + "\"availabilityStatus\":\"available_on_plex\","
            + "\"addedAt\":\"2026-05-20T10:00:00+02:00\","
            + "\"updatedAt\":\"2026-05-25T10:00:00+02:00\""
            + "}]";

    WatchlistItem item = WatchlistApiClient.parseItems(json).get(0);

    assertEquals("2026-05-20T10:00:00+02:00", item.addedAt());
    assertEquals("2026-05-25T10:00:00+02:00", item.updatedAt());
}
```

- [ ] Run `android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.WatchlistApiClientTest --no-daemon` with the project `JAVA_HOME` and verify failure because the request builder and `addedAt` parser do not exist.
- [ ] Add `addedAt` field and getter to `WatchlistItem`.
- [ ] Update `WatchlistApiClient.parseItems` to require `addedAt` and keep `updatedAt`.
- [ ] Add `WatchlistApiClient.buildWatchlistPath(String mediaType, String sortMode, boolean includeUnavailable)`.
- [ ] Update `WatchlistApiClient.getWatchlist` signature to `getWatchlist(String collection, String sortMode, boolean includeUnavailable)`.
- [ ] Map Android state to API values:
  - `BrowsingState.MEDIA_ALL` -> `collection=all`
  - `BrowsingState.MEDIA_MOVIES` -> `collection=movie`
  - `BrowsingState.MEDIA_TV` -> `collection=tv`
  - `CollectionOrganizer.SORT_DATE_ADDED` -> `sort=added_desc`
  - `CollectionOrganizer.SORT_ALPHABETICAL` -> `sort=title_asc`
  - `includeUnavailable=false` -> `availability=plex`
  - `includeUnavailable=true` -> `availability=plex,not_on_plex,unreleased,unknown_match`
- [ ] Change `BrowsingState.defaults()` to default to `MEDIA_ALL` because the backend now supports combined collection.
- [ ] Update `BrowsingStateTest` expected default media value to `MEDIA_ALL`.
- [ ] Update `MainActivity`:
  - Enable the `All` button.
  - Wire `All` to `selectMediaType(BrowsingState.MEDIA_ALL)`.
  - Include `All` in restored valid media states.
  - Call `apiClient.getWatchlist(browsingState.mediaType(), browsingState.sortMode(), browsingState.includeUnavailable())`.
  - Stop calling `CollectionOrganizer.organize` for primary API-backed browsing; render `loadedItems` directly.
  - Keep `CollectionOrganizer` and its tests unchanged in this slice; a separate cleanup can remove it after the backend-owned contract has settled.
- [ ] Re-run Android focused tests and verify they pass.
- [ ] Commit with `feat: migrate android tv to collection api`.

### Task 6: End-To-End Verification

**Files:**
- Modify: `docs/android-tv.md`

- [ ] Update `docs/android-tv.md`:
  - `All` is now enabled.
  - `Date added` is backend-owned and uses `addedAt`.
  - Availability popup maps to backend split availability reasons.
- [ ] Run backend tests:

```powershell
dotnet test backend\Watchlist.sln
```

- [ ] Run Android tests, build, and lint:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest :app:assembleDebug :app:lintDebug --no-daemon
```

- [ ] Start MongoDB:

```powershell
docker compose up -d mongo
```

- [ ] Start the backend on a non-conflicting port such as `5010`:

```powershell
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5010
```

- [ ] Smoke-test the new API:

```powershell
Invoke-RestMethod 'http://localhost:5010/api/watchlist?collection=all&availability=plex,not_on_plex,unreleased,unknown_match&sort=added_desc'
Invoke-RestMethod 'http://localhost:5010/api/watchlist?collection=tv&availability=not_on_plex&sort=title_asc'
```

- [ ] Verify the first response includes both movie and TV items and each item has `addedAt`.
- [ ] Verify the second response returns `tv-andor` with `availabilityStatus` `not_on_plex`.
- [ ] Run `git diff --check`.
- [ ] Commit with `docs: update android tv collection api guide`.
