# Android TV Remote Grid Design

## Goal

Redesign the Android TV home screen into a remote-first collection browser inspired by Apple TV navigation. Keep the client read-only and keep this implementation slice compatible with the existing backend API.

## Scope

This slice replaces the current featured-detail-row layout with a poster grid and predictable D-pad navigation.

The implementation includes:

- Top navigation for `All`, `Movies`, `TV Shows`, and a disabled search icon.
- A collection toolbar with sorting and an availability filter icon.
- A compact availability overlay with `On Plex` and `Unavailable` checkboxes.
- Portrait poster tiles with title and availability badge.
- Persistent TV browsing state using Android `SharedPreferences`.
- Remote-only navigation verification.

The implementation does not include:

- Search behavior.
- Combined `All` API queries.
- Accurate watchlist-added sorting.
- Streaming-service providers.
- A featured carousel.

## Screen Structure

The screen has three vertical focus zones:

1. Top navigation: `All`, `Movies`, `TV Shows`, disabled search icon.
2. Collection toolbar: sort selector and filter icon.
3. Poster grid: portrait tiles for the current collection.

The poster grid is the first content surface. The current featured detail area is removed for this slice.

`All` remains visible but disabled until the backend supports combined movie and TV queries. `Movies` and `TV Shows` continue to use the current endpoint.

## Remote Navigation

Focus movement must be predictable:

- `Left` and `Right` move within the current zone.
- `Down` moves from top navigation to the toolbar, then from the toolbar to the poster grid.
- `Up` moves from the grid to the toolbar, then from the toolbar to top navigation.
- `Back` closes the availability overlay before the Activity handles normal back navigation.

The app stores the previous top navigation selection, sort mode, availability checkbox state, and focused item ID in Android `SharedPreferences`. On launch or refresh, it restores that state when possible. If the stored item is no longer visible, focus moves to the first visible poster. While `All` is disabled, the fallback top-navigation selection is `Movies`.

## Collection Toolbar

The toolbar contains:

- A sorting control with `Date added` and `A-Z`.
- A filter icon that opens the availability overlay.

`Date added` is the default intent. The current backend does not expose a stable `addedAt` value, so accurate watchlist-added ordering requires a backend follow-up. Until then, the UI preserves backend order for `Date added`.

`A-Z` sorts visible items locally by title.

## Availability Overlay

Selecting the filter icon opens a compact overlay beside the toolbar. It contains:

- `On Plex`, checked by default.
- `Unavailable`, unchecked by default.

Changes apply immediately as checkboxes are toggled. `Back` closes the overlay and restores focus to the filter icon.

For this slice, `Unavailable` includes:

- `not_on_plex`
- `unreleased`
- `unknown_match`

Poster badges continue to distinguish those states. Future streaming services extend this overlay with provider-specific choices such as `On HBO`.

## Tile Design

Movies and TV shows use the same portrait tile format:

- Stable poster dimensions.
- Title below or within the lower tile area without layout shifts.
- Availability badge.
- Bright border and slight scale emphasis while focused.
- Readable fallback when artwork is missing.

Focus changes must not resize surrounding layout or create awkward text wrapping.

## API Impact

The current backend API stays stable during this UI slice. The following backend changes are follow-up work:

- Support combined movie and TV queries for the `All` top-navigation item.
- Add a stable `addedAt` field to MongoDB documents and API DTOs.
- Replace the single availability mode with source-aware multi-select filtering.
- Add subscribed streaming-service availability later.

## Future Idea

Add an Apple TV-style featured carousel above the collection for newly added items. This is intentionally separate from the initial remote-first grid redesign.

## Verification

Automated verification:

- Unit tests for local availability filtering and alphabetical sorting.
- Android unit tests.
- Android lint.
- Debug APK assembly.

Manual Android TV verification:

- Browse with D-pad only.
- Move predictably between top navigation, toolbar, and poster grid.
- Switch between `Movies` and `TV Shows`.
- Switch between sort modes.
- Open the filter overlay, toggle checkboxes, and close it with `Back`.
- Confirm clear focus indication and stable tile layout.
- Confirm state restoration after refresh or relaunch.
