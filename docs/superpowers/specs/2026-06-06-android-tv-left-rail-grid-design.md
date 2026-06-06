# Android TV Left Rail Grid Design

## Goal

Redesign the Android TV watchlist home screen so collection and availability controls live in a persistent left rail instead of above the poster grid. The screen should feel similar to Plex library browsing, but simpler and focused on the read-only watchlist scope.

## Scope

This design changes the Android TV home screen layout only. It keeps the backend API contract, details screen behavior, and read-only v1 product scope unchanged.

The implementation includes:

- A persistent labeled left rail for primary collection and availability controls.
- A main content area with a compact header for title, count, and sort controls.
- A denser poster grid with column count configured at build time.
- Updated D-pad focus behavior for moving between the rail and the grid.
- Updated Android TV documentation and focused unit tests for configuration behavior.

The implementation does not include:

- In-app settings for density.
- Search behavior.
- Backend API changes.
- Phone UI work.
- Watchlist mutation flows.

## Layout

The screen becomes a two-pane layout:

1. Left rail: fixed-width navigation and filter controls.
2. Main content: compact header plus scrollable poster grid.

The left rail is always visible and text-labeled. It is inspired by Plex's side navigation but avoids Plex's broader library complexity. It contains:

- `All`
- `Movies`
- `TV Shows`
- `On Plex`
- `Unavailable`
- Disabled `Search`

`All`, `Movies`, and `TV Shows` remain mutually exclusive collection choices. `On Plex` is the baseline availability state and remains selected. `Unavailable` toggles whether unavailable, unreleased, and uncertain Plex-match items are included.

The main header shows the current collection title and item count, then `Date added` and `A-Z` sort controls. Sort stays out of the rail because it changes ordering inside the current collection rather than changing the browsing section.

## Grid Density

The grid column count is configured by build-time app config, not by in-app settings. Android exposes this as a `BuildConfig` value read through `WatchlistConfig`.

Default TV value:

```text
WATCHLIST_GRID_COLUMNS = 7
```

Rows are not configured directly. The number of visible rows depends on screen height, poster tile dimensions, and Android display scaling. Removing the top controls and reducing tile dimensions should allow more than one row to be visible on common TV viewports.

The configured column count must be clamped to a safe range so invalid local build values do not create an unusable layout. The supported range is 4 through 9 columns.

## Remote Navigation

Focus behavior should optimize for changing collection and availability from deep in the grid:

- `Up` and `Down` move within the rail when focus is in the rail.
- `Right` from a rail item returns to the last focused poster when available, otherwise the first poster.
- `Left` from the first poster column moves into the rail.
- `Left` inside later poster columns moves to the previous poster.
- `Right`, `Up`, and `Down` inside the grid move within the poster grid.
- `Up` from the first poster row moves to the main header sort controls when the target column is near the sort controls.
- `Down` from sort controls moves into the grid.
- `Back` keeps existing behavior: close any open overlay first, otherwise normal Activity handling.

When a rail action changes the collection or availability filter, the app reloads data and restores focus to the first visible poster unless a persisted focused item is still visible.

## State

The existing `BrowsingState` concepts remain sufficient:

- media type
- sort mode
- include unavailable
- focused item id

The app should continue storing these values in `SharedPreferences`. No new persisted setting is needed for grid density because density is a build-time configuration.

## Visual Treatment

The rail should use restrained dark styling with clear selected and focused states. Selected collection and availability items should be visually distinct from mere focus. Focus must remain obvious for TV remote use.

Poster tiles continue to show:

- poster artwork or fallback
- title
- availability badge

Tile dimensions should be reduced enough for a denser TV grid but still readable from a couch. Text must remain stable and should not resize or shift layout on focus.

## Files And Boundaries

`MainActivity` owns the screen layout and D-pad wiring. The redesign can remain in `MainActivity` for this slice because the current app builds UI programmatically there already.

`WatchlistConfig` owns build-config normalization and validation. It should expose grid column count alongside the existing API base URL.

`android/app/build.gradle` owns default build-time values.

`docs/android-tv.md` documents the current Android TV UX and manual remote test.

## Testing

Automated testing should cover:

- `WatchlistConfig.gridColumns` clamps invalid values to the supported range.
- Existing `BrowsingState` tests continue to pass.
- Existing collection and API tests continue to pass.

Manual Android TV verification should cover:

- Focus starts in a usable place.
- Moving left from the first poster column reaches the rail.
- Moving right from the rail returns to the grid.
- `All`, `Movies`, and `TV Shows` reload the correct collection.
- `Unavailable` toggles unavailable, unreleased, and uncertain-match items.
- Sort controls remain reachable and reload the current collection.
- More than one poster row is visible on a common TV viewport.
- Details navigation still opens from a focused poster and returns focus to the grid.

## Documentation

Update `docs/android-tv.md` to replace the old top-navigation description with the new left rail, configurable grid column count, and revised manual remote test.
