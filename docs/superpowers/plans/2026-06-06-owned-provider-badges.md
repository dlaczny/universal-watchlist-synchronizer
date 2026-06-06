# Owned Provider Badges Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a compact Android TV badge when a movie is available on one of the user's owned streaming services, using cached TMDB provider data from the backend.

**Architecture:** The backend already stores `OwnedServiceAvailability` in MongoDB. This plan exposes that list through the existing watchlist read model and API, then Android parses it and folds it into the existing single bottom badge formatter. Plex remains highest priority; provider availability only replaces `Unavailable`/`Not released` when Plex does not have the movie.

**Tech Stack:** .NET backend, MongoDB read model, xUnit/FluentAssertions tests, Android Java client, JUnit Android unit tests.

---

## Prerequisites

The working tree currently contains uncommitted VOD badge changes. Do not revert them. Before implementing this plan, either:

1. Commit the current VOD badge work, or
2. Implement this plan on top of the existing dirty files and keep both changes together.

Ignore unrelated untracked files such as `opencode.json` unless the user explicitly asks to handle them.

## File Structure

- `backend/src/Watchlist.Domain/WatchlistItem.cs`: add domain read-model property `OwnedServiceAvailability`.
- `backend/src/Watchlist.Application/WatchlistItemDto.cs`: add API DTO property `OwnedServiceAvailability`.
- `backend/src/Watchlist.Application/WatchlistQueryService.cs`: map domain provider list into API DTO.
- `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`: map Mongo `OwnedServiceAvailability` into domain and preserve it in `FromDomain`.
- `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`: application read-model contract tests.
- `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`: Mongo document mapping tests.
- `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`: JSON contract tests.
- `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`: parse model property for owned provider list.
- `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`: parse JSON array, default empty list for older responses.
- `android/app/src/main/java/com/watchlist/tv/MainActivity.java`: badge text and background priority.
- `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`: parser and badge-priority tests.
- `docs/api.md`, `docs/android-tv.md`, `docs/architecture.md`, `docs/integrations.md`, `docs/todo.md`: contract and backlog updates.

---

### Task 1: Expose Owned Provider List In Backend Read Model

**Files:**
- Modify: `backend/src/Watchlist.Domain/WatchlistItem.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistItemDto.cs`
- Modify: `backend/src/Watchlist.Application/WatchlistQueryService.cs`
- Modify: `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`
- Test: `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`

- [ ] **Step 1: Add failing query-service test assertions**

In `backend/tests/Watchlist.Application.Tests/WatchlistQueryServiceTests.cs`, update `GetItemAsync_WhenItemExists_ReturnsItem` to assert:

```csharp
result.OwnedServiceAvailability.Should().Equal("Amazon Prime Video", "Max");
```

In the private `CreateItem` helper, keep the current constructor call and add/extend the object initializer:

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
    OwnedServiceAvailability = ["Amazon Prime Video", "Max"]
};
```

If the current file does not yet have `VodReleaseKnown`, `ReleasedOnVod`, or `VodRegions`, keep only the `OwnedServiceAvailability` initializer.

- [ ] **Step 2: Add failing Mongo document mapping assertions**

In `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`, update `ToDomain_WhenDocumentHasTmdbMetadata_MapsDisplayFieldsFromDocument` to assert:

```csharp
item.OwnedServiceAvailability.Should().Equal("Amazon Prime Video");
```

The test fixture already sets:

```csharp
OwnedServiceAvailability = ["Amazon Prime Video"],
```

- [ ] **Step 3: Run backend focused tests and verify RED**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~WatchlistQueryServiceTests|FullyQualifiedName~MongoWatchlistItemDocumentTests" --artifacts-path .artifacts\provider-badges-red-backend
```

Expected: compile failure because `WatchlistItem` or `WatchlistItemDto` does not expose `OwnedServiceAvailability`.

- [ ] **Step 4: Add backend read-model properties**

In `backend/src/Watchlist.Domain/WatchlistItem.cs`, add this init-only property inside the record body:

```csharp
public IReadOnlyList<string> OwnedServiceAvailability { get; init; } = [];
```

In `backend/src/Watchlist.Application/WatchlistItemDto.cs`, add the DTO property after `VodRegions` if that field exists, otherwise after `AvailabilityStatus`:

```csharp
IReadOnlyList<string> OwnedServiceAvailability,
```

In `backend/src/Watchlist.Application/WatchlistQueryService.cs`, add the mapping argument after `item.VodRegions` if that field exists, otherwise after `ToApiValue(item.AvailabilityStatus)`:

```csharp
item.OwnedServiceAvailability,
```

In `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`, update `ToDomain()` object initializer:

```csharp
OwnedServiceAvailability = OwnedServiceAvailability
```

If the initializer already has other properties, include commas as needed:

```csharp
{
    VodReleaseKnown = string.Equals(TmdbMetadataStatus, "enriched", StringComparison.Ordinal),
    ReleasedOnVod = ReleasedOnVod,
    VodRegions = VodRegions,
    OwnedServiceAvailability = OwnedServiceAvailability
};
```

Update `FromDomain()` to preserve it:

```csharp
OwnedServiceAvailability = item.OwnedServiceAvailability,
```

- [ ] **Step 5: Run focused backend tests and verify GREEN**

Run:

```powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~WatchlistQueryServiceTests|FullyQualifiedName~MongoWatchlistItemDocumentTests" --artifacts-path .artifacts\provider-badges-green-backend
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit backend read-model slice**

If the current VOD badge work has already been committed, run:

```powershell
git add backend\src\Watchlist.Domain\WatchlistItem.cs backend\src\Watchlist.Application\WatchlistItemDto.cs backend\src\Watchlist.Application\WatchlistQueryService.cs backend\src\Watchlist.Infrastructure\MongoWatchlistItemDocument.cs backend\tests\Watchlist.Application.Tests\WatchlistQueryServiceTests.cs backend\tests\Watchlist.Application.Tests\MongoWatchlistItemDocumentTests.cs
git commit -m "feat: expose owned provider availability"
```

If the dirty VOD badge work is being kept in the same commit, do not run this commit step yet. Continue through the plan, then create one combined commit after full verification.

---

### Task 2: Expose Owned Providers In API JSON Contract

**Files:**
- Test: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`
- Potentially modify: `backend/src/Watchlist.Infrastructure/SeedData.cs`
- Potentially modify: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`

- [ ] **Step 1: Add failing API contract assertion**

In `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`, update `GetWatchlistItem_WhenItemExists_ReturnsItem` to assert:

```csharp
document.RootElement.TryGetProperty("ownedServiceAvailability", out JsonElement providers).Should().BeTrue();
providers.EnumerateArray().Select(provider => provider.GetString()).Should().Equal("Amazon Prime Video");
```

- [ ] **Step 2: Run API test and verify RED if seed data lacks providers**

Run:

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter FullyQualifiedName~GetWatchlistItem_WhenItemExists_ReturnsItem --artifacts-path .artifacts\provider-badges-red-api
```

Expected: fail if seeded Dune item has no owned providers, or pass if the app seed already includes one. If it passes immediately because JSON serialization already exposes an empty or populated array, continue and add the seed fixture in the next step so the test proves non-empty provider data.

- [ ] **Step 3: Ensure seeded API fixture has one owned provider**

If `SeededWatchlistReadRepository` or `SeedData` supplies `movie-dune-part-two`, update the Dune `WatchlistItem` object initializer to include:

```csharp
OwnedServiceAvailability = ["Amazon Prime Video"]
```

If that item is intentionally Plex-only and should not show provider badges, use another seeded item in the API test and request its ID. Keep the API assertion on an item with a non-empty provider list.

- [ ] **Step 4: Run API test and verify GREEN**

Run:

```powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter FullyQualifiedName~GetWatchlistItem_WhenItemExists_ReturnsItem --artifacts-path .artifacts\provider-badges-green-api
```

Expected: selected API test passes and JSON contains `ownedServiceAvailability`.

- [ ] **Step 5: Commit API contract slice**

Run:

```powershell
git add backend\tests\Watchlist.Api.Tests\WatchlistApiTests.cs backend\src\Watchlist.Infrastructure\SeedData.cs backend\tests\Watchlist.Api.Tests\SeededApiFactory.cs
git commit -m "test: cover owned provider API contract"
```

Only include files that actually changed.

---

### Task 3: Parse Owned Providers In Android Client

**Files:**
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`
- Test: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`

- [ ] **Step 1: Add failing Android parser assertions**

In `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`, update `parseItems_parsesBackendWatchlistJson` JSON to include:

```java
+ "\"ownedServiceAvailability\":[\"Amazon Prime Video\",\"Max\"],"
```

Place it after `vodRegions` if that field exists, otherwise after `availabilityStatus`.

Add assertions:

```java
assertEquals("Amazon Prime Video", item.ownedServiceAvailability().get(0));
assertEquals("Max", item.ownedServiceAvailability().get(1));
```

Add a second assertion to the existing backward-compatibility test `parseItems_whenVodFieldsMissing_defaultsToNotReleasedOnVod`, or create this test if it does not exist:

```java
assertEquals(0, item.ownedServiceAvailability().size());
```

- [ ] **Step 2: Run Android parser test and verify RED**

Run:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.WatchlistApiClientTest --no-daemon
```

Expected: compile failure because `WatchlistItem` has no `ownedServiceAvailability()` method, or assertion failure because parsing defaults to empty.

- [ ] **Step 3: Add Android item model field**

In `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`, add a field:

```java
private final List<String> ownedServiceAvailability;
```

In the compatibility constructor, pass an empty list:

```java
List.of(),
```

In the main constructor, add parameter after `vodRegions` if present, otherwise after `availabilityStatus`:

```java
List<String> ownedServiceAvailability,
```

Set the field defensively:

```java
this.ownedServiceAvailability = Collections.unmodifiableList(new ArrayList<>(ownedServiceAvailability));
```

Add getter:

```java
public List<String> ownedServiceAvailability() {
    return ownedServiceAvailability;
}
```

- [ ] **Step 4: Parse JSON field**

In `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`, update the `new WatchlistItem(...)` call to pass:

```java
parseStringArray(item.optJSONArray("ownedServiceAvailability")),
```

Place it after `parseStringArray(item.optJSONArray("vodRegions"))` if that argument exists. Reuse the existing `parseStringArray(JSONArray array)` helper. If the helper does not exist, add:

```java
private static List<String> parseStringArray(JSONArray array) throws JSONException {
    List<String> values = new ArrayList<>();
    if (array == null) {
        return values;
    }

    for (int index = 0; index < array.length(); index++) {
        values.add(array.getString(index));
    }

    return values;
}
```

- [ ] **Step 5: Run Android parser tests and verify GREEN**

Run:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.WatchlistApiClientTest --no-daemon
```

Expected: `BUILD SUCCESSFUL`.

- [ ] **Step 6: Commit Android parsing slice**

Run:

```powershell
git add android\app\src\main\java\com\watchlist\tv\WatchlistItem.java android\app\src\main\java\com\watchlist\tv\WatchlistApiClient.java android\app\src\test\java\com\watchlist\tv\WatchlistApiClientTest.java
git commit -m "feat: parse owned provider availability on android"
```

---

### Task 4: Add Provider Badge Formatting And Color

**Files:**
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`
- Test: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`

- [ ] **Step 1: Add failing provider badge priority tests**

In `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`, add:

```java
@Test
public void formatAvailability_whenMovieIsOnOwnedProvider_returnsProviderBadge() {
    WatchlistItem item = new WatchlistItem(
            "movie-prime",
            "movie",
            "letterboxd",
            "letterboxd-prime",
            "Prime Movie",
            2025,
            null,
            null,
            null,
            "released",
            "not_on_plex",
            true,
            true,
            List.of("PL"),
            List.of("Amazon Prime Video"),
            "2026-05-24T10:00:00+02:00",
            "2026-05-25T10:00:00+02:00");

    assertEquals("Prime", MainActivity.formatAvailability(item));
}

@Test
public void formatAvailability_whenMovieIsOnPlexAndOwnedProvider_returnsPlexBadge() {
    WatchlistItem item = new WatchlistItem(
            "movie-plex",
            "movie",
            "letterboxd",
            "letterboxd-plex",
            "Plex Movie",
            2025,
            null,
            null,
            null,
            "released",
            "available_on_plex",
            true,
            true,
            List.of("PL"),
            List.of("Amazon Prime Video"),
            "2026-05-24T10:00:00+02:00",
            "2026-05-25T10:00:00+02:00");

    assertEquals("On Plex", MainActivity.formatAvailability(item));
}

@Test
public void formatAvailability_whenMovieHasMultipleOwnedProviders_returnsCompactProviderBadge() {
    WatchlistItem item = new WatchlistItem(
            "movie-multi",
            "movie",
            "letterboxd",
            "letterboxd-multi",
            "Multi Provider Movie",
            2025,
            null,
            null,
            null,
            "released",
            "not_on_plex",
            true,
            true,
            List.of("PL"),
            List.of("HBO Max", "Amazon Prime Video"),
            "2026-05-24T10:00:00+02:00",
            "2026-05-25T10:00:00+02:00");

    assertEquals("Max +1", MainActivity.formatAvailability(item));
}
```

If the current `WatchlistItem` constructor has a different argument order because VOD fields are not present, adapt the calls to the actual constructor while keeping `ownedServiceAvailability` as a list argument.

- [ ] **Step 2: Run Android focused tests and verify RED**

Run:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.WatchlistApiClientTest --no-daemon
```

Expected: provider badge tests fail because `formatAvailability` still returns `Unavailable` or `Not released`.

- [ ] **Step 3: Implement provider label helpers**

In `android/app/src/main/java/com/watchlist/tv/MainActivity.java`, add helpers near `formatAvailability`:

```java
private static String formatOwnedProviderBadge(WatchlistItem item) {
    if (item.ownedServiceAvailability().isEmpty()) {
        return null;
    }

    String first = shortProviderName(item.ownedServiceAvailability().get(0));
    int count = item.ownedServiceAvailability().size();
    if (count == 1) {
        return first;
    }

    if (count == 2 && first.length() <= 10) {
        return first + " +1";
    }

    return count + " services";
}

private static String shortProviderName(String providerName) {
    if ("Amazon Prime Video".equalsIgnoreCase(providerName)
            || "Prime Video".equalsIgnoreCase(providerName)) {
        return "Prime";
    }
    if ("Max".equalsIgnoreCase(providerName)
            || "HBO Max".equalsIgnoreCase(providerName)) {
        return "Max";
    }
    if ("SkyShowtime".equalsIgnoreCase(providerName)) {
        return "SkyShowtime";
    }
    if ("Crunchyroll".equalsIgnoreCase(providerName)) {
        return "Crunchyroll";
    }

    return providerName;
}
```

If Java complains about returning `null` from `formatOwnedProviderBadge`, keep the method return type as `String` and use a null check at call sites; this project does not currently use nullability annotations.

- [ ] **Step 4: Update badge priority**

In `formatAvailability(WatchlistItem item)`, insert provider handling immediately after the Plex check:

```java
String providerBadge = formatOwnedProviderBadge(item);
if (providerBadge != null) {
    return providerBadge;
}
```

The method should keep this order:

```java
if (WatchlistFilters.AVAILABLE_ON_PLEX.equals(item.availabilityStatus())) {
    return "On Plex";
}
String providerBadge = formatOwnedProviderBadge(item);
if (providerBadge != null) {
    return providerBadge;
}
if (item.vodReleaseKnown() && !item.releasedOnVod()) {
    return "Not released";
}
if ("unreleased".equals(item.availabilityStatus())) {
    return "Unreleased";
}
if ("unknown_match".equals(item.availabilityStatus())) {
    return "Match uncertain";
}
return "Unavailable";
```

If the current code does not yet have `vodReleaseKnown`, keep the existing `Not released` logic and insert provider handling before it.

- [ ] **Step 5: Add provider badge background color**

In `badgeBackground(WatchlistItem item)`, keep Plex green highest priority and use a distinct blue/teal for provider badges:

```java
int color;
if (WatchlistFilters.AVAILABLE_ON_PLEX.equals(item.availabilityStatus())) {
    color = Color.rgb(20, 120, 80);
} else if (!item.ownedServiceAvailability().isEmpty()) {
    color = Color.rgb(14, 116, 144);
} else {
    color = Color.rgb(86, 99, 112);
}
drawable.setColor(color);
```

- [ ] **Step 6: Run Android focused tests and verify GREEN**

Run:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.WatchlistApiClientTest --no-daemon
```

Expected: `BUILD SUCCESSFUL`.

- [ ] **Step 7: Commit provider badge UI slice**

Run:

```powershell
git add android\app\src\main\java\com\watchlist\tv\MainActivity.java android\app\src\test\java\com\watchlist\tv\WatchlistApiClientTest.java
git commit -m "feat: show owned provider badges"
```

---

### Task 5: Update Documentation And Backlog

**Files:**
- Modify: `docs/api.md`
- Modify: `docs/android-tv.md`
- Modify: `docs/architecture.md`
- Modify: `docs/integrations.md`
- Modify: `docs/todo.md`

- [ ] **Step 1: Update API examples**

In `docs/api.md`, add `ownedServiceAvailability` to watchlist item examples:

```json
"ownedServiceAvailability": ["Amazon Prime Video"]
```

Add this contract note near the existing `releasedOnVod` explanation:

```markdown
`ownedServiceAvailability` contains subscribed streaming services where TMDB shows Poland flatrate availability. Android uses this list for provider badges when the item is not available on Plex.
```

- [ ] **Step 2: Update Android TV behavior docs**

In `docs/android-tv.md`, add:

```markdown
- Non-Plex movies with `ownedServiceAvailability` entries render a provider badge such as `Prime`, `Max`, `SkyShowtime`, `Crunchyroll`, or `Max +1`.
```

- [ ] **Step 3: Update architecture/integration notes**

In `docs/architecture.md`, extend the read model paragraph to mention provider badges:

```markdown
The watchlist read model also exposes `ownedServiceAvailability`, derived from cached TMDB Poland flatrate providers. Android uses it for the single card badge after Plex priority.
```

In `docs/integrations.md`, update the later-extension note so provider badges are no longer described as future work:

```markdown
TMDB watch-provider data is cached for movies. Android consumes `ownedServiceAvailability`, `vodReleaseKnown`, and `releasedOnVod` for card badges. Provider-ID refinement remains later work.
```

- [ ] **Step 4: Mark backlog item complete**

In `docs/todo.md`, change:

```markdown
- [ ] Add Android TV badges for TMDB provider availability.
```

to:

```markdown
- [x] Add Android TV badges for TMDB provider availability.
```

- [ ] **Step 5: Commit docs slice**

Run:

```powershell
git add docs\api.md docs\android-tv.md docs\architecture.md docs\integrations.md docs\todo.md
git commit -m "docs: document owned provider badges"
```

---

### Task 6: Full Verification

**Files:**
- No code changes expected.

- [ ] **Step 1: Run backend solution tests**

Run:

```powershell
dotnet test backend\Watchlist.sln --artifacts-path .artifacts\provider-badges-verify-backend
```

Expected:

```text
Passed! - Failed: 0
```

Both application and API test assemblies should pass.

- [ ] **Step 2: Run Android unit tests and build**

Run:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest :app:assembleDebug --no-daemon
```

Expected:

```text
BUILD SUCCESSFUL
```

Gradle deprecation warnings are acceptable if there are no test or build failures.

- [ ] **Step 3: Manual smoke test with local API**

Restart the backend:

```powershell
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5000
```

Confirm a provider-backed movie exposes the new field:

```powershell
Invoke-RestMethod http://localhost:5000/api/watchlist/movie-letterboxd-1297842
```

Expected: response includes `ownedServiceAvailability`. If the list is empty for that specific movie, inspect another enriched item or rerun:

```powershell
Invoke-RestMethod -Method Post http://localhost:5000/api/sync/tmdb/movies
```

Then reopen the Android TV app. Expected:

- Plex movies still show `On Plex`.
- Movies available on an owned PL flatrate provider show provider text such as `Prime`.
- Movies with no provider and confirmed no VOD still show `Not released`.

- [ ] **Step 4: Final status review**

Run:

```powershell
git status --short --branch
```

Expected: only intentional uncommitted files remain. Do not include `opencode.json` unless the user asks.
