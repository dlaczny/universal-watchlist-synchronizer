# Android TV Remote Grid Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Android TV featured-detail-row prototype with an Apple TV-inspired, remote-first poster grid while keeping the existing backend API stable.

**Architecture:** Add small pure-Java collection and browsing-state units so filtering, sorting, and persistence defaults are testable without Android UI instrumentation. Rebuild `MainActivity` as three predictable focus zones: top navigation, collection toolbar, and poster grid. Keep unsupported API-backed features visible but disabled and record their backend follow-ups in documentation.

**Tech Stack:** Android Java 17, Android SDK views, `SharedPreferences`, JUnit 4, Gradle.

---

### Task 1: Local Collection Filtering And Sorting

**Files:**
- Create: `android/app/src/main/java/com/watchlist/tv/CollectionOrganizer.java`
- Create: `android/app/src/test/java/com/watchlist/tv/CollectionOrganizerTest.java`

- [ ] Write failing unit tests for the local grid behaviors:

```java
@Test
public void organize_whenUnavailableDisabled_returnsOnlyPlexItems() {
    List<WatchlistItem> result = CollectionOrganizer.organize(
            items(), false, CollectionOrganizer.SORT_DATE_ADDED);
    assertEquals(Arrays.asList("Dune"), titles(result));
}

@Test
public void organize_whenUnavailableEnabled_returnsAllBackendOrderedItems() {
    List<WatchlistItem> result = CollectionOrganizer.organize(
            items(), true, CollectionOrganizer.SORT_DATE_ADDED);
    assertEquals(Arrays.asList("Future Movie", "Dune", "Arrival"), titles(result));
}

@Test
public void organize_whenAlphabetical_sortsIgnoringCase() {
    List<WatchlistItem> result = CollectionOrganizer.organize(
            items(), true, CollectionOrganizer.SORT_ALPHABETICAL);
    assertEquals(Arrays.asList("Arrival", "Dune", "Future Movie"), titles(result));
}
```

- [ ] Run `android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.CollectionOrganizerTest --no-daemon` and verify failure because `CollectionOrganizer` does not exist.
- [ ] Implement `CollectionOrganizer` with constants `SORT_DATE_ADDED` and `SORT_ALPHABETICAL`. Preserve backend order for date-added intent and filter unavailable items locally when disabled.
- [ ] Re-run the focused tests and verify they pass.
- [ ] Commit with `feat: add android tv collection organizer`.

### Task 2: Persisted Browsing State

**Files:**
- Create: `android/app/src/main/java/com/watchlist/tv/BrowsingState.java`
- Create: `android/app/src/test/java/com/watchlist/tv/BrowsingStateTest.java`

- [ ] Write failing unit tests for defaults and immutable state updates:

```java
@Test
public void defaults_selectMoviesDateAddedAndPlexOnly() {
    BrowsingState state = BrowsingState.defaults();
    assertEquals(BrowsingState.MEDIA_MOVIES, state.mediaType());
    assertEquals(CollectionOrganizer.SORT_DATE_ADDED, state.sortMode());
    assertFalse(state.includeUnavailable());
}

@Test
public void withFocusedItem_preservesFilters() {
    BrowsingState state = BrowsingState.defaults()
            .withMediaType(BrowsingState.MEDIA_TV)
            .withFocusedItemId("tv-example");
    assertEquals(BrowsingState.MEDIA_TV, state.mediaType());
    assertEquals("tv-example", state.focusedItemId());
    assertFalse(state.includeUnavailable());
}
```

- [ ] Run `android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.BrowsingStateTest --no-daemon` and verify failure because `BrowsingState` does not exist.
- [ ] Implement `BrowsingState` as an immutable pure-Java value object with:
  - `MEDIA_ALL`, `MEDIA_MOVIES`, and `MEDIA_TV` constants.
  - Defaults of movies, date-added intent, Plex-only results, and no focused item.
  - `withMediaType`, `withSortMode`, `withIncludeUnavailable`, and `withFocusedItemId`.
- [ ] Re-run the focused tests and verify they pass.
- [ ] Commit with `feat: add persisted android tv browsing state model`.

### Task 3: Remote-First Android TV Grid

**Files:**
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`
- Modify: `android/app/src/main/res/values/styles.xml`
- Test: `android/app/src/test/java/com/watchlist/tv/CollectionOrganizerTest.java`
- Test: `android/app/src/test/java/com/watchlist/tv/BrowsingStateTest.java`

- [ ] Replace the current featured detail panel and horizontal row with:
  - Top navigation controls: disabled `All`, `Movies`, `TV Shows`, and disabled search icon.
  - Toolbar controls: `Date added`, `A-Z`, and filter icon.
  - A portrait poster `GridLayout`.
- [ ] Add a compact overlay next to the toolbar filter icon with `On Plex` and `Unavailable` checkboxes. Apply toggles immediately and close the overlay on `Back`, returning focus to the filter icon.
- [ ] Persist browsing state to `SharedPreferences`: media selection, sort mode, unavailable checkbox, and focused item ID.
- [ ] Restore the prior focused poster after refresh when it remains visible; otherwise focus the first visible poster.
- [ ] Set explicit directional focus movement between top navigation, toolbar, and grid. Use stable poster dimensions, bright focused borders, slight focus scale, readable title text, availability badges, and missing-artwork fallback.
- [ ] Run `android\gradlew.bat -p android :app:testDebugUnitTest :app:assembleDebug :app:lintDebug --no-daemon` and verify success.
- [ ] Launch the Android TV emulator and verify using only D-pad and Back:
  - Movies and TV Shows switch API-backed collections.
  - `All` and search are visible but disabled.
  - Sorting switches between backend order and A-Z.
  - Filter overlay opens, toggles unavailable items immediately, and closes with Back.
  - Focus moves predictably between zones and restores after refresh.
- [ ] Commit with `feat: redesign android tv collection grid`.

### Task 4: Documentation And Final Verification

**Files:**
- Modify: `docs/android-tv.md`
- Modify: `docs/todo.md`

- [ ] Update the Android TV run guide with the remote-first grid behavior and manual D-pad verification flow.
- [ ] Mark the implemented Android TV grid backlog item complete while leaving backend API follow-ups open.
- [ ] Run:

```powershell
dotnet test backend\Watchlist.sln
$env:JAVA_HOME='C:\Program Files\Android\Android Studio1\jbr'
$env:ANDROID_HOME='C:\Users\laczn\AppData\Local\Android\Sdk'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
android\gradlew.bat -p android :app:testDebugUnitTest :app:assembleDebug :app:lintDebug --no-daemon
git diff --check
```

- [ ] Verify the backend tests, Android unit tests, debug APK assembly, lint, and whitespace check pass.
- [ ] Commit with `docs: document android tv remote grid`.

