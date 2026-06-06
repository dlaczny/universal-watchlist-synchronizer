# Movie And TV Details View Design

## Goal

Add an Android TV details screen for movies and TV shows. Selecting a focused poster with the center remote button opens a Plex-like detail page with basic metadata, description, availability state, and a primary action slot.

The client remains read-only. The Android app still calls only the backend API. TMDB metadata is fetched during backend sync and stored in MongoDB so the details page does not depend on live third-party calls.

## Scope

In scope:

- Open a separate Android detail screen from a focused poster.
- Render immediately from the grid item data passed by `MainActivity`.
- Fetch richer detail data from `GET /api/watchlist/{id}` after the screen opens.
- Return to the grid with Back and preserve the previously focused poster.
- Add a richer backend detail DTO for `GET /api/watchlist/{id}`.
- Store and expose movie detail fields from TMDB sync: runtime, original language, vote average, and vote count.
- Expose genres on the detail endpoint.
- Show a state-aware primary action button.

Out of scope:

- Watchlist create, edit, delete, reorder, or mutation flows.
- Cast, crew, reviews, ratings stars, trailers, and similar full Plex-detail features.
- Real Plex, HBO, Max, or other provider deep-link launching.
- Provider button rows in Android.
- Android phone UI.
- TV-show TMDB account watchlist sync, beyond keeping the detail DTO ready for TV fields.

## User Flow

The poster grid remains the browsing entry point. When a poster tile has focus, pressing center/Select opens a separate detail screen for that item.

`MainActivity` passes the selected grid item to the detail screen so the page can render title, poster, backdrop, overview, and availability immediately. The detail screen then calls `GET /api/watchlist/{id}` and updates itself with the richer detail model when the request succeeds.

Back exits the detail screen and returns to the grid. The existing browsing state keeps focus on the selected poster where possible.

The grid does not show an extra `View details` hint. Focused poster plus Select is the Android TV convention.

## Detail Screen Layout

The visual direction is Plex-like:

- darkened backdrop fills the screen when available
- poster sits on the left
- title, metadata, description, and action sit to the right
- primary action button is the first focus target
- optional metadata is hidden when missing

The metadata row can include:

- year or release date
- runtime
- original language
- genres
- TMDB score

The TMDB score is shown as source-clear text, for example `7.7 TMDB`. It is shown only when `tmdbVoteCount >= 10`. No star rating UI is included in this slice.

If backdrop loading fails or no backdrop exists, the screen uses a neutral dark background. If poster loading fails, it uses the same clear missing-artwork treatment as the grid.

## Primary Action

The detail screen has one primary action slot. Launching external apps is not implemented in this slice.

Action states:

- `available_on_plex`: show enabled-looking `Open in Plex`, but keep it a dummy/no-op for now.
- `not_on_plex`: show disabled `Unavailable`.
- `unreleased`: show disabled `Not released`.
- `unknown_match`: show disabled `Match uncertain`.

Provider-specific actions such as HBO, Max, or SkyShowtime are deferred. TMDB watch-provider data can remain stored by the backend, but Android should not render provider launch buttons until deep-link behavior and subscription semantics are designed.

## Backend API

`GET /api/watchlist` stays focused on the grid and keeps returning the existing lean `WatchlistItemDto` shape.

`GET /api/watchlist/{id}` returns a richer shared detail DTO for movies and TV shows. The shape uses nullable fields for media-specific data so Android can render one detail screen for both types.

Initial detail fields:

- `id`
- `mediaType`
- `source`
- `sourceId`
- `title`
- `year`
- `overview`
- `posterUrl`
- `backdropUrl`
- `releaseStatus`
- `availabilityStatus`
- `vodReleaseKnown`
- `releasedOnVod`
- `vodRegions`
- `addedAt`
- `updatedAt`
- `genres`
- `runtimeMinutes`
- `originalLanguage`
- `tmdbVoteAverage`
- `tmdbVoteCount`
- `primaryActionLabel`
- `primaryActionEnabled`
- `primaryActionTarget`

`primaryActionTarget` is `null` in this slice. It reserves a contract slot for future app-launch/deep-link work without pretending the behavior is solved.

## Backend Data Model

TMDB movie enrichment should fetch and store:

- runtime in minutes
- original language
- vote average
- vote count

Genres are already stored on MongoDB watchlist documents, but they are not currently exposed through the domain or API DTO. This slice should make them available to the detail DTO without forcing them into the grid DTO.

The backend should continue storing normalized read-model data in MongoDB. Detail requests must use cached MongoDB data only and must not call TMDB, Plex, Letterboxd, or provider services while handling the request.

## Android Model

Android should add a detail model that can parse the richer detail endpoint while tolerating missing optional fields.

The initial screen render can use the existing `WatchlistItem` grid model. After the detail fetch succeeds, the screen replaces or augments visible fields with the richer detail model.

If the detail fetch fails, the detail screen keeps the initial grid data visible and shows a compact backend error message. It should not immediately eject the user back to the grid.

## Error Handling

Backend:

- Existing item returns `200 OK` with the detail DTO.
- Missing item returns `404 Not Found`.
- MongoDB dependency failure keeps returning the existing dependency-style `503 Service Unavailable`.
- Missing TMDB enrichment leaves optional detail fields null instead of failing the item response.

Android:

- Hide optional metadata fields when values are null, empty, or not meaningful.
- Always show explicit availability state.
- Keep initial grid data visible when the richer detail request fails.
- Use neutral visual fallbacks for missing or failed artwork.

## Testing

Backend tests:

- Detail endpoint returns richer DTO for an existing item.
- Detail endpoint returns `404` for a missing item.
- List endpoint remains compatible with the existing lean response.
- TMDB movie client parses runtime, original language, vote average, and vote count from deterministic fixtures.
- TMDB enrichment persists the new fields.
- Mongo detail mapping preserves genres and the new TMDB fields.
- Primary action state maps correctly from availability status.

Android tests:

- Detail JSON parsing handles present and missing optional fields.
- Poster Select starts the detail screen with the selected item id.
- Primary action label and enabled state are correct for each availability status.
- Missing optional metadata is omitted from formatted display text.

Manual Android TV verification:

1. Launch the app and focus a poster.
2. Press center/Select and confirm a detail screen opens.
3. Confirm the screen renders immediately from existing grid data.
4. Confirm richer metadata appears after the backend detail response when available.
5. Confirm the first focus target is the primary action button.
6. Confirm Back returns to the grid and focus returns to the selected poster.
7. Confirm missing artwork and missing optional metadata do not produce broken layout or placeholder noise.

## Later Work

- Real Plex deep-link launch behavior.
- Provider-specific launch actions after app deep links and subscription semantics are designed.
- Cast, crew, trailers, reviews, or richer Plex-like sections.
- TV-specific TMDB sync fields such as first air date, episode runtime, season count, and show status.
- Shared-poster or richer screen transition animation.
