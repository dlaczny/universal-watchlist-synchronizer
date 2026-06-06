# Android TV Left Rail Grid Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Android TV top-control home screen with a Plex-like labeled left rail and a denser configurable poster grid.

**Architecture:** Keep the redesign inside the existing programmatic Android UI structure. `MainActivity` owns layout, rail interactions, and focus wiring; `WatchlistConfig` owns build-time configuration validation; docs describe the new remote-first flow.

**Tech Stack:** Java 17, Android SDK 36, Gradle Android plugin, JUnit 4, Android `BuildConfig`, `SharedPreferences`, programmatic Android views.

---

## File Structure

- Modify `android/app/build.gradle`
  - Add `WATCHLIST_GRID_COLUMNS` as a `BuildConfig` integer with default value `7`.
- Modify `android/app/src/main/java/com/watchlist/tv/WatchlistConfig.java`
  - Add `gridColumns()` and `clampGridColumns(int)` so UI code never consumes invalid build config.
- Modify `android/app/src/test/java/com/watchlist/tv/WatchlistConfigTest.java`
  - Add focused unit tests for grid column clamping.
- Modify `android/app/src/main/res/values/strings.xml`
  - Add rail/header strings if needed for title/count or content descriptions.
- Modify `android/app/src/main/java/com/watchlist/tv/MainActivity.java`
  - Replace the vertical top-navigation layout with a horizontal root: left rail plus main content.
  - Convert top nav and availability popup behavior into rail controls.
  - Keep sort controls in a compact main header.
  - Read `WatchlistConfig.gridColumns()` for `GridLayout` column count.
  - Rewire D-pad focus between rail, header, and poster grid.
- Modify `docs/android-tv.md`
  - Update current UX and manual remote test for the left rail and configurable density.

## Task 1: Add Grid Column Configuration

**Files:**
- Modify: `android/app/build.gradle`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistConfig.java`
- Test: `android/app/src/test/java/com/watchlist/tv/WatchlistConfigTest.java`

- [ ] **Step 1: Write the failing tests**

Add these tests to `WatchlistConfigTest`:

```java
@Test
public void clampGridColumns_keepsValuesInsideSupportedRange() {
    assertEquals(4, WatchlistConfig.clampGridColumns(1));
    assertEquals(4, WatchlistConfig.clampGridColumns(4));
    assertEquals(7, WatchlistConfig.clampGridColumns(7));
    assertEquals(9, WatchlistConfig.clampGridColumns(9));
    assertEquals(9, WatchlistConfig.clampGridColumns(14));
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.WatchlistConfigTest --no-daemon
```

Expected: `FAILED` because `WatchlistConfig.clampGridColumns(int)` does not exist.

- [ ] **Step 3: Add the build config field**

In `android/app/build.gradle`, inside `defaultConfig`, add:

```groovy
buildConfigField "int", "WATCHLIST_GRID_COLUMNS", "7"
```

Keep the existing `WATCHLIST_API_BASE_URL` line unchanged.

- [ ] **Step 4: Implement config access and clamping**

Replace `WatchlistConfig.java` with:

```java
package com.watchlist.tv;

public final class WatchlistConfig {
    private static final int MIN_GRID_COLUMNS = 4;
    private static final int MAX_GRID_COLUMNS = 9;

    private WatchlistConfig() {
    }

    public static String apiBaseUrl() {
        return normalizeBaseUrl(BuildConfig.WATCHLIST_API_BASE_URL);
    }

    public static String normalizeBaseUrl(String value) {
        if (value.endsWith("/")) {
            return value.substring(0, value.length() - 1);
        }

        return value;
    }

    public static int gridColumns() {
        return clampGridColumns(BuildConfig.WATCHLIST_GRID_COLUMNS);
    }

    static int clampGridColumns(int value) {
        if (value < MIN_GRID_COLUMNS) {
            return MIN_GRID_COLUMNS;
        }
        if (value > MAX_GRID_COLUMNS) {
            return MAX_GRID_COLUMNS;
        }
        return value;
    }
}
```

- [ ] **Step 5: Run the focused test and verify it passes**

Run:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --tests com.watchlist.tv.WatchlistConfigTest --no-daemon
```

Expected: `BUILD SUCCESSFUL`.

- [ ] **Step 6: Commit**

```powershell
git add android/app/build.gradle android/app/src/main/java/com/watchlist/tv/WatchlistConfig.java android/app/src/test/java/com/watchlist/tv/WatchlistConfigTest.java
git commit -m "feat: configure android tv grid density"
```

## Task 2: Restructure The Home Screen Into Left Rail Plus Main Content

**Files:**
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`
- Modify: `android/app/src/main/res/values/strings.xml`

- [ ] **Step 1: Add any needed strings**

Add these strings to `strings.xml`:

```xml
<string name="rail_on_plex">On Plex</string>
<string name="rail_unavailable">Unavailable</string>
<string name="content_title_all">All Watchlist</string>
<string name="content_title_movies">Movies</string>
<string name="content_title_tv">TV Shows</string>
<string name="content_count">%1$d items</string>
```

- [ ] **Step 2: Introduce rail/header fields in `MainActivity`**

In `MainActivity`, replace the fixed grid column constant:

```java
private static final int GRID_COLUMNS = 5;
```

with:

```java
private int gridColumns;
```

Add fields near the existing button fields:

```java
private Button onPlexButton;
private Button unavailableButton;
private TextView contentTitleView;
private TextView contentCountView;
private View lastRailFocus;
```

- [ ] **Step 3: Initialize configured columns**

In `onCreate`, before `setContentView(createContentView());`, add:

```java
gridColumns = WatchlistConfig.gridColumns();
```

- [ ] **Step 4: Replace `createContentView` with a two-pane root**

Rewrite `createContentView()` to build a horizontal root:

```java
private View createContentView() {
    LinearLayout root = new LinearLayout(this);
    root.setOrientation(LinearLayout.HORIZONTAL);
    root.setBackgroundColor(Color.rgb(15, 20, 25));

    root.addView(createLeftRail(), new LinearLayout.LayoutParams(dp(176), LinearLayout.LayoutParams.MATCH_PARENT));

    LinearLayout main = new LinearLayout(this);
    main.setOrientation(LinearLayout.VERTICAL);
    main.setPadding(dp(28), dp(26), dp(34), dp(22));

    main.addView(createMainHeader());

    messageView = new TextView(this);
    messageView.setTextColor(Color.rgb(203, 213, 225));
    messageView.setTextSize(17);
    messageView.setPadding(0, dp(10), 0, dp(8));
    main.addView(messageView);

    progressBar = new ProgressBar(this);
    progressBar.setVisibility(View.GONE);
    main.addView(progressBar);

    ScrollView gridScrollView = new ScrollView(this);
    gridScrollView.setFillViewport(true);
    gridScrollView.setClipToPadding(false);
    gridScrollView.setFocusable(false);

    posterGrid = new GridLayout(this);
    posterGrid.setColumnCount(gridColumns);
    posterGrid.setAlignmentMode(GridLayout.ALIGN_BOUNDS);
    posterGrid.setPadding(0, dp(8), 0, dp(18));
    gridScrollView.addView(posterGrid, new ScrollView.LayoutParams(
            ScrollView.LayoutParams.MATCH_PARENT,
            ScrollView.LayoutParams.WRAP_CONTENT));
    main.addView(gridScrollView, new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            0,
            1));

    root.addView(main, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.MATCH_PARENT, 1));
    return root;
}
```

- [ ] **Step 5: Add `createLeftRail`**

Add:

```java
private LinearLayout createLeftRail() {
    LinearLayout rail = new LinearLayout(this);
    rail.setOrientation(LinearLayout.VERTICAL);
    rail.setPadding(dp(10), dp(18), dp(10), dp(18));
    rail.setBackgroundColor(Color.rgb(18, 24, 31));

    TextView heading = new TextView(this);
    heading.setText(R.string.app_name);
    heading.setTextColor(Color.WHITE);
    heading.setTextSize(19);
    heading.setTypeface(Typeface.DEFAULT_BOLD);
    heading.setPadding(dp(8), 0, dp(8), dp(16));
    rail.addView(heading);

    allButton = railButton(getString(R.string.nav_all));
    allButton.setOnClickListener(view -> selectMediaType(BrowsingState.MEDIA_ALL));
    rail.addView(allButton);

    moviesButton = railButton(getString(R.string.nav_movies));
    moviesButton.setOnClickListener(view -> selectMediaType(BrowsingState.MEDIA_MOVIES));
    rail.addView(moviesButton);

    tvButton = railButton(getString(R.string.nav_tv_shows));
    tvButton.setOnClickListener(view -> selectMediaType(BrowsingState.MEDIA_TV));
    rail.addView(tvButton);

    rail.addView(spacer(1, dp(18)));

    onPlexButton = railButton(getString(R.string.rail_on_plex));
    onPlexButton.setEnabled(false);
    rail.addView(onPlexButton);

    unavailableButton = railButton(getString(R.string.rail_unavailable));
    unavailableButton.setOnClickListener(view -> toggleUnavailable());
    rail.addView(unavailableButton);

    rail.addView(spacer(1, 0), new LinearLayout.LayoutParams(1, 0, 1));

    ImageButton searchButton = iconButton(R.drawable.ic_search, getString(R.string.action_search));
    searchButton.setEnabled(false);
    rail.addView(searchButton);

    wireRailFocusLinks(searchButton);
    return rail;
}
```

- [ ] **Step 6: Add `createMainHeader`**

Add:

```java
private LinearLayout createMainHeader() {
    LinearLayout header = new LinearLayout(this);
    header.setOrientation(LinearLayout.HORIZONTAL);
    header.setGravity(Gravity.CENTER_VERTICAL);

    LinearLayout titleBlock = new LinearLayout(this);
    titleBlock.setOrientation(LinearLayout.VERTICAL);

    contentTitleView = new TextView(this);
    contentTitleView.setTextColor(Color.WHITE);
    contentTitleView.setTextSize(24);
    contentTitleView.setTypeface(Typeface.DEFAULT_BOLD);
    titleBlock.addView(contentTitleView);

    contentCountView = new TextView(this);
    contentCountView.setTextColor(Color.rgb(148, 163, 184));
    contentCountView.setTextSize(13);
    titleBlock.addView(contentCountView);

    header.addView(titleBlock, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));

    dateAddedButton = textButton(getString(R.string.sort_date_added));
    dateAddedButton.setOnClickListener(view -> selectSortMode(CollectionOrganizer.SORT_DATE_ADDED));
    header.addView(dateAddedButton);

    alphabeticalButton = textButton(getString(R.string.sort_alphabetical));
    alphabeticalButton.setOnClickListener(view -> selectSortMode(CollectionOrganizer.SORT_ALPHABETICAL));
    header.addView(alphabeticalButton);

    dateAddedButton.setNextFocusRightId(alphabeticalButton.getId());
    alphabeticalButton.setNextFocusLeftId(dateAddedButton.getId());
    return header;
}
```

- [ ] **Step 7: Run tests after the layout compile changes**

Run:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
```

Expected: `BUILD SUCCESSFUL`.

- [ ] **Step 8: Commit**

```powershell
git add android/app/src/main/java/com/watchlist/tv/MainActivity.java android/app/src/main/res/values/strings.xml
git commit -m "feat: add android tv left rail layout"
```

## Task 3: Convert Availability Filtering To Rail Controls

**Files:**
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`

- [ ] **Step 1: Remove popup-only availability fields and behavior**

Remove the `filterButton` field, `filterPopup` field, `createToolbar()`, and `showFilterPopup()` from `MainActivity`. Remove `filterPopup` handling from `dispatchKeyEvent` and `onDestroy`.

- [ ] **Step 2: Add rail toggle behavior**

Add:

```java
private void toggleUnavailable() {
    browsingState = browsingState
            .withIncludeUnavailable(!browsingState.includeUnavailable())
            .withFocusedItemId(null);
    persistBrowsingState();
    updateControlStyles();
    loadItems(false);
}
```

- [ ] **Step 3: Update control styling**

Update `updateControlStyles()` so it styles rail controls and sort controls:

```java
private void updateControlStyles() {
    styleTextButton(allButton, BrowsingState.MEDIA_ALL.equals(browsingState.mediaType()));
    styleTextButton(moviesButton, BrowsingState.MEDIA_MOVIES.equals(browsingState.mediaType()));
    styleTextButton(tvButton, BrowsingState.MEDIA_TV.equals(browsingState.mediaType()));
    styleTextButton(onPlexButton, true);
    styleTextButton(unavailableButton, browsingState.includeUnavailable());
    styleTextButton(dateAddedButton, CollectionOrganizer.SORT_DATE_ADDED.equals(browsingState.sortMode()));
    styleTextButton(alphabeticalButton, CollectionOrganizer.SORT_ALPHABETICAL.equals(browsingState.sortMode()));
    updateHeaderText();
}
```

Add:

```java
private void updateHeaderText() {
    if (contentTitleView == null || contentCountView == null) {
        return;
    }

    if (BrowsingState.MEDIA_MOVIES.equals(browsingState.mediaType())) {
        contentTitleView.setText(R.string.content_title_movies);
    } else if (BrowsingState.MEDIA_TV.equals(browsingState.mediaType())) {
        contentTitleView.setText(R.string.content_title_tv);
    } else {
        contentTitleView.setText(R.string.content_title_all);
    }

    contentCountView.setText(getString(R.string.content_count, loadedItems.size()));
}
```

- [ ] **Step 4: Update header count after rendering**

In `renderItems`, after `loadedItems = items;` and before rendering tiles, call:

```java
updateHeaderText();
```

Also call `updateHeaderText();` at the end of `showLoading()` and `showError(Exception exception)` so the title remains current during non-content states.

- [ ] **Step 5: Run tests and assemble**

Run:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

Expected: both commands end with `BUILD SUCCESSFUL`.

- [ ] **Step 6: Commit**

```powershell
git add android/app/src/main/java/com/watchlist/tv/MainActivity.java
git commit -m "feat: move availability filters into tv rail"
```

## Task 4: Rewire D-Pad Focus For Rail, Header, And Grid

**Files:**
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`

- [ ] **Step 1: Add rail focus helpers**

Add:

```java
private Button railButton(String text) {
    Button button = textButton(text);
    button.setGravity(Gravity.CENTER_VERTICAL);
    button.setMinWidth(0);
    button.setPadding(dp(12), 0, dp(12), 0);
    button.setOnFocusChangeListener((view, hasFocus) -> {
        if (hasFocus) {
            lastRailFocus = view;
        }
        updateControlStyles();
    });
    return button;
}
```

Add:

```java
private void wireRailFocusLinks(View searchButton) {
    allButton.setNextFocusDownId(moviesButton.getId());
    moviesButton.setNextFocusUpId(allButton.getId());
    moviesButton.setNextFocusDownId(tvButton.getId());
    tvButton.setNextFocusUpId(moviesButton.getId());
    tvButton.setNextFocusDownId(onPlexButton.getId());
    onPlexButton.setNextFocusUpId(tvButton.getId());
    onPlexButton.setNextFocusDownId(unavailableButton.getId());
    unavailableButton.setNextFocusUpId(onPlexButton.getId());
    unavailableButton.setNextFocusDownId(searchButton.getId());
    searchButton.setNextFocusUpId(unavailableButton.getId());
}
```

- [ ] **Step 2: Replace `wirePosterFocusLinks` column math**

Inside `wirePosterFocusLinks`, replace `GRID_COLUMNS` references with `gridColumns`.

For each poster tile, set first-column left focus to the last rail item:

```java
View railTarget = lastRailFocus != null ? lastRailFocus : allButton;
tile.setNextFocusLeftId(column > 0 && previous >= 0
        ? posterTiles.get(previous).getId()
        : railTarget.getId());
```

Update the key listener's left branch:

```java
if (keyCode == KeyEvent.KEYCODE_DPAD_LEFT) {
    target = column > 0 ? posterTiles.get(previous) : railTarget;
}
```

- [ ] **Step 3: Wire rail right focus into the grid**

At the end of `wirePosterFocusLinks`, when `posterTiles` is not empty, add:

```java
View gridTarget = focusedPosterOrFirst();
allButton.setNextFocusRightId(gridTarget.getId());
moviesButton.setNextFocusRightId(gridTarget.getId());
tvButton.setNextFocusRightId(gridTarget.getId());
onPlexButton.setNextFocusRightId(gridTarget.getId());
unavailableButton.setNextFocusRightId(gridTarget.getId());
```

Add:

```java
private View focusedPosterOrFirst() {
    String focusedItemId = browsingState.focusedItemId();
    if (focusedItemId != null) {
        for (int index = 0; index < loadedItems.size() && index < posterTiles.size(); index++) {
            if (focusedItemId.equals(loadedItems.get(index).id())) {
                return posterTiles.get(index);
            }
        }
    }
    return posterTiles.get(0);
}
```

- [ ] **Step 4: Wire header down focus into the grid**

When posters are present, set:

```java
dateAddedButton.setNextFocusDownId(posterTiles.get(0).getId());
alphabeticalButton.setNextFocusDownId(posterTiles.get(Math.min(gridColumns - 1, posterTiles.size() - 1)).getId());
```

Set first-row up targets so early columns go to `dateAddedButton` and later columns go to `alphabeticalButton`:

```java
View headerTarget = column < Math.max(1, gridColumns - 2) ? dateAddedButton : alphabeticalButton;
tile.setNextFocusUpId(above >= 0 ? posterTiles.get(above).getId() : headerTarget.getId());
```

Use the same `headerTarget` in the key listener's up branch.

- [ ] **Step 5: Clear stale focus links**

Update `clearPosterGrid()` to clear only controls that still exist:

```java
dateAddedButton.setNextFocusDownId(View.NO_ID);
alphabeticalButton.setNextFocusDownId(View.NO_ID);
allButton.setNextFocusRightId(View.NO_ID);
moviesButton.setNextFocusRightId(View.NO_ID);
tvButton.setNextFocusRightId(View.NO_ID);
onPlexButton.setNextFocusRightId(View.NO_ID);
unavailableButton.setNextFocusRightId(View.NO_ID);
```

- [ ] **Step 6: Run tests and assemble**

Run:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

Expected: both commands end with `BUILD SUCCESSFUL`.

- [ ] **Step 7: Commit**

```powershell
git add android/app/src/main/java/com/watchlist/tv/MainActivity.java
git commit -m "feat: wire tv rail focus navigation"
```

## Task 5: Tune Poster Tile Density

**Files:**
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`

- [ ] **Step 1: Reduce poster tile dimensions**

In `createPosterTile`, change artwork and text widths from `dp(132)` to `dp(118)`, artwork height from `dp(188)` to `dp(168)`, title height from `dp(40)` to `dp(36)`, tile width from `dp(142)` to `dp(128)`, and tile height from `dp(264)` to `dp(236)`.

Use these layout values:

```java
tile.addView(artworkFrame, new LinearLayout.LayoutParams(dp(118), dp(168)));
tile.addView(title, new LinearLayout.LayoutParams(dp(118), dp(36)));
tile.addView(badge, new LinearLayout.LayoutParams(dp(118), dp(24)));

GridLayout.LayoutParams layoutParams = new GridLayout.LayoutParams();
layoutParams.width = dp(128);
layoutParams.height = dp(236);
layoutParams.setMargins(0, 0, dp(10), dp(10));
tile.setLayoutParams(layoutParams);
```

- [ ] **Step 2: Keep title and badge stable**

Verify these title and badge properties remain:

```java
title.setMaxLines(2);
title.setEllipsize(TextUtils.TruncateAt.END);
badge.setMaxLines(1);
badge.setGravity(Gravity.CENTER);
```

- [ ] **Step 3: Run assemble**

Run:

```powershell
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

Expected: `BUILD SUCCESSFUL`.

- [ ] **Step 4: Commit**

```powershell
git add android/app/src/main/java/com/watchlist/tv/MainActivity.java
git commit -m "feat: densify android tv poster grid"
```

## Task 6: Update Android TV Documentation

**Files:**
- Modify: `docs/android-tv.md`

- [ ] **Step 1: Update current UX**

Replace the old top-navigation and toolbar bullets under `Current UX` with:

```markdown
- Persistent Plex-inspired left rail with `All`, `Movies`, `TV Shows`, `On Plex`, `Unavailable`, and disabled search.
- Main content header with the active collection title, item count, and `Date added` / `A-Z` sort controls.
- Configurable poster grid density through `WATCHLIST_GRID_COLUMNS` in `android/app/build.gradle`; the default TV value is `7`.
- `SharedPreferences` restore for the selected collection, sort mode, unavailable filter, and the last focused item where possible.
- Predictable D-pad focus movement between the left rail, sort controls, and poster grid.
```

- [ ] **Step 2: Update manual remote test**

Replace manual test steps 1 through 7 with:

```markdown
1. Confirm focus starts in a usable location and moves predictably through the left rail, sort controls, and poster grid.
2. Confirm `All`, `Movies`, and `TV Shows` change the collection from the left rail.
3. Confirm `Date added` and `A-Z` reload the collection from the backend with the selected sort.
4. Confirm `On Plex` remains selected in the rail and `Unavailable` toggles unavailable, unreleased, and uncertain-match items.
5. From a poster in the first grid column, press Left and confirm focus moves into the rail.
6. From the rail, press Right and confirm focus returns to the last focused poster where possible.
7. Confirm more than one poster row is visible on a common TV viewport with the default grid density.
```

Keep the existing details screen and backend-offline manual test coverage.

- [ ] **Step 3: Commit**

```powershell
git add docs/android-tv.md
git commit -m "docs: describe android tv left rail grid"
```

## Task 7: Final Verification

**Files:**
- No edits expected.

- [ ] **Step 1: Run all Android unit tests**

Run:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
```

Expected: `BUILD SUCCESSFUL`.

- [ ] **Step 2: Build the debug APK**

Run:

```powershell
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

Expected: `BUILD SUCCESSFUL`.

- [ ] **Step 3: Check git status**

Run:

```powershell
git status --short
```

Expected: only intentional committed changes, or a clean working tree if all task commits were made.

- [ ] **Step 4: Manual TV verification**

Run the backend and launch the debug APK on an Android TV emulator or device. Verify:

- Left rail is always visible.
- Grid shows more than one row on the target TV viewport.
- D-pad `Left` from first poster column reaches the rail.
- D-pad `Right` from rail returns to the grid.
- `Unavailable` rail item toggles unavailable, unreleased, and uncertain-match items.
- Sort controls remain reachable.
- Poster details still open and Back returns to the grid with focus restored.
