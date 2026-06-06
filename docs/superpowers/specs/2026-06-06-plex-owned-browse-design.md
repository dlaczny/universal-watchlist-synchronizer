# Plex-Owned Browse Design

## Goal

Show Plex library items in the Android TV browse grid when the user browses `On Plex`, even when those items are not on the Letterboxd or TMDB watchlists.

The main list should contain:

- Watchlist movies and TV shows that are available in Plex.
- Plex movies and TV shows that are not on any watchlist.

Plex-only items must be clearly identified on the details page as available in Plex but not part of the watchlist.

## Product Rules

The watchlist sources of truth do not change:

- Movies the user wants to watch still come from Letterboxd.
- TV shows the user wants to watch still come from the TMDB account watchlist.
- Plex defines owned or locally available content.

Plex-only items are browseable library items, not synthetic watchlist entries. They must not be exported to Radarr or Sonarr, must not affect watchlist sync, and must not be treated as things the user explicitly wants to watch.

Plex-only items appear only when the client asks for the Plex availability view. The default mixed watchlist view remains watchlist-only.

## Scope

Included:

- Extend Plex inventory sync to store both movie and TV library items.
- Add backend browse results that combine watchlist items and Plex-only inventory items for `availability=plex`.
- Add explicit item membership metadata so clients can distinguish watchlist items from Plex-only items.
- Update Android TV parsing, grid rendering, and details rendering to show Plex-only items.
- Show a details-page message for Plex-only items: not on the watchlist, only in Plex.
- Use Plex-provided title, year, poster, and summary metadata for Plex-only items in v1.

Excluded:

- Adding Plex-only items to Letterboxd or TMDB watchlists.
- Create, edit, delete, reorder, or watchlist mutation flows.
- TMDB enrichment for Plex-only items.
- Streaming provider availability.
- Phone UI work.
- Exporting Plex-only items to Radarr or Sonarr.

## Backend Data Model

The existing `plex_library_items` collection should become media-type neutral.

Each document stores:

- stable backend `id`, such as `plex-movie-{ratingKey}` or `plex-tv-{ratingKey}`
- `ratingKey`
- `mediaType`: `movie` or `tv`
- `title`
- `year`
- `summary`
- `posterUrl`
- `backdropUrl`
- `librarySectionKey`
- `librarySectionTitle`
- `plexGuid`
- optional external IDs: `imdbId`, `tmdbId`, `tvdbId`
- `lastSeenAt`

For Plex TV shows, the synced unit is the show, not seasons or episodes. Android TV v1 browse is poster-grid oriented, so show-level visibility is sufficient.

Stale handling stays section-scoped. When a Plex sync scans movie and TV sections, inventory documents for scanned sections that are missing from the latest scan are removed.

## Backend Browse Contract

Keep `GET /api/watchlist` as the Android browse endpoint, but its result is no longer strictly a list of watchlist records when `availability=plex`.

Add response fields:

```json
{
  "id": "plex-tv-12345",
  "mediaType": "tv",
  "source": "plex",
  "sourceId": "12345",
  "title": "Example Show",
  "year": 2024,
  "overview": "Plex summary when available.",
  "posterUrl": "/api/images/plex/12345/poster",
  "backdropUrl": "/api/images/plex/12345/backdrop",
  "releaseStatus": "unknown",
  "availabilityStatus": "available_on_plex",
  "libraryMembership": "plex_only",
  "addedAt": "2026-06-06T10:00:00Z",
  "updatedAt": "2026-06-06T10:00:00Z"
}
```

`libraryMembership` values:

- `watchlist`: item is from the watchlist and may or may not be on Plex.
- `watchlist_and_plex`: item is from the watchlist and has a confident Plex match.
- `plex_only`: item is in Plex and has no matching watchlist item.

Existing watchlist items use the same DTO fields as today plus `libraryMembership`. Plex-only items use Plex metadata and set:

- `source = "plex"`
- `sourceId = ratingKey`
- `availabilityStatus = "available_on_plex"`
- `releaseStatus = "unknown"` unless Plex exposes enough status data later
- `addedAt = lastSeenAt`
- `updatedAt = lastSeenAt`
- `vodReleaseKnown = false`
- `releasedOnVod = false`
- `vodRegions = []`
- `ownedServiceAvailability = ["plex"]`

## Query Behavior

For omitted availability, `GET /api/watchlist` remains watchlist-only and returns all watchlist availability states.

When `availability=plex`, the backend returns:

1. Watchlist items whose `availabilityStatus` is `available_on_plex`.
2. Plex inventory items not matched to any watchlist item, filtered by `collection`.

When `availability` includes `plex` plus other states, Plex-only items are included because the Plex state is selected, and non-Plex watchlist items are included according to the other selected states.

Collection filtering applies to both watchlist and Plex-only items:

- `collection=all`: movies and TV shows
- `collection=movie`: movies only
- `collection=tv`: TV shows only

Sorting applies to the combined list:

- `title_asc`: case-insensitive title sort across watchlist and Plex-only items.
- `added_desc`: watchlist items sort by watchlist-added date; Plex-only items sort by Plex `lastSeenAt`. This is acceptable for v1 because Plex does not represent watchlist intent.

To avoid duplicates, a Plex inventory item is excluded from Plex-only browse results when it has a confident match to any watchlist item. Matching should use the same IDs and conservative title/year fallback rules as availability matching.

## Details Contract

`GET /api/watchlist/{id}` should support both watchlist IDs and Plex-only IDs returned by the browse endpoint.

For watchlist items, details stay as they are today with `libraryMembership` added.

For Plex-only items, details are built from Plex inventory metadata. The Android details page should show a concise status line:

```text
In Plex library. Not on your watchlist.
```

Plex-only details should disable watchlist-oriented primary actions. The primary action can remain unavailable in v1 because playback integration is not in scope.

If a Plex-only item disappears after a later Plex sync, the details endpoint returns `404`.

## Plex Sync

Extend Plex sync beyond movie libraries:

- Discover movie and TV sections from `/library/sections`.
- Sync movie entries from movie sections.
- Sync show entries from TV sections.
- Parse nested GUIDs for both media types where Plex exposes them.
- Store enough Plex metadata for list and details rendering.
- Apply matching against watchlist movies and TV shows.

Movie matching keeps the current rules. TV matching should use:

1. TMDB ID when available.
2. TVDB ID when available.
3. IMDb ID when available.
4. Exact normalized title plus exact year.
5. Ambiguous fallback produces `unknown_match`.

TV availability should not depend on season or episode completeness in v1. A show existing in Plex means the show is available on Plex.

## Android TV UX

The existing left rail remains the control surface:

- `All`
- `Movies`
- `TV Shows`
- `On Plex`
- `Unavailable`

When `On Plex` is selected, the grid can contain both watchlist-and-Plex items and Plex-only items. Plex-only tiles should use the same poster layout and availability badge. If space allows, use a subtle secondary label such as `Plex only`; if not, reserve the stronger explanation for the details page.

Details page behavior:

- Watchlist item available on Plex: show existing availability messaging.
- Plex-only item: show `In Plex library. Not on your watchlist.`
- Plex-only item with missing poster or backdrop: use existing image fallback behavior.

Remote navigation, sorting controls, and read-only behavior remain unchanged.

## Error Handling

If Plex sync fails, existing watchlist browsing should still work from the latest stored read model.

If MongoDB is unavailable, existing `503 Service Unavailable` behavior remains.

If Plex metadata for an image is missing or unreachable, Android should show existing poster/backdrop fallbacks. Backend image proxy failures should not make the item itself disappear.

If a Plex-only item has too little metadata, the backend may still return it with title and media type. Android should render it with fallback artwork.

## Testing

Backend application tests:

- `availability=plex` returns matched watchlist items and unmatched Plex-only movies.
- `availability=plex&collection=tv` returns matched watchlist TV and unmatched Plex-only TV shows.
- Default query remains watchlist-only.
- Mixed availability queries include Plex-only items only when the Plex state is selected.
- Plex-only items are not returned when they match a watchlist item.
- Details endpoint returns Plex-only details and `libraryMembership=plex_only`.
- Plex-only details return `404` after inventory removal.
- Plex TV matching covers TMDB, TVDB, IMDb, exact title/year, no match, and ambiguous match.

Infrastructure tests:

- Plex client parses TV sections and show entries.
- Mongo Plex inventory repository stores movie and TV documents.
- Mongo Plex inventory repository deletes stale items only for scanned sections.
- Plex image paths or URLs map into client-safe image URLs.

API tests:

- `GET /api/watchlist?availability=plex` returns combined browse DTOs.
- `GET /api/watchlist/{plex-only-id}` returns Plex-only details.
- Existing watchlist detail routes continue to work.
- Export endpoints continue excluding Plex-only items.

Android tests:

- API parser accepts `libraryMembership`.
- Unknown or missing `libraryMembership` defaults to `watchlist` for compatibility.
- Collection request construction still uses backend-owned filters.
- Details screen exposes the Plex-only status text for `libraryMembership=plex_only`.
- Existing grid organization and focus tests continue to pass.

Manual Android TV verification:

- Select `On Plex` and confirm watchlist-and-Plex items and Plex-only items appear together.
- Select `Movies` and `TV Shows` and confirm Plex-only items respect collection filtering.
- Open a Plex-only item and confirm the details page says it is not on the watchlist.
- Toggle `Unavailable` and confirm non-Plex watchlist states can be included without hiding Plex-only items.
- Confirm no create, edit, delete, or watchlist mutation controls are introduced.

## Documentation

Update:

- `docs/architecture.md` to describe the unified browse read model and Plex-only membership.
- `docs/integrations.md` to document Plex TV show sync assumptions.
- `docs/android-tv.md` to document that `On Plex` includes both watchlist matches and Plex-only library items.
