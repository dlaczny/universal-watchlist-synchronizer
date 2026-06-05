# Plex Sync V1 Design

## Goal

Add backend Plex movie sync so the watchlist can show whether each wanted movie is available on the user's Plex server. This slice is backend-only for Android: the existing Android TV grid, filters, and badges should start showing real Plex availability without new UI controls.

## Scope

Included:

- Read Plex server configuration from backend local settings or environment variables.
- Discover Plex movie libraries from the configured server.
- Sync Plex movie inventory into MongoDB.
- Match existing watchlist movies to Plex movies.
- Update watchlist `availabilityStatus` from Plex matching.
- Add manual sync endpoints for Plex-only and combined refresh.
- Keep sync idempotent and repeatable.

Excluded:

- Android UI changes.
- A separate Android "Plex Library" tab.
- Plex TV library matching.
- Plex cloud discovery and account login flow.
- Mutating Plex or the watchlist from the app.

## Configuration

Backend configuration:

```json
{
  "Plex": {
    "BaseUrl": "http://127.0.0.1:32400",
    "Token": "local-token"
  }
}
```

Local secrets belong in `backend/src/Watchlist.Api/appsettings.Development.Local.json`, which is ignored by git. Environment variable equivalents should use `Plex__BaseUrl` and `Plex__Token`.

The configured `BaseUrl` is the Plex API root, not the browser UI URL. For the current server, use:

```text
http://127.0.0.1:32400
```

## Plex API Assumptions

The backend calls Plex directly. Android never calls Plex.

Observed local Plex libraries:

- Movie library: `Filmy`, section key `1`
- TV library: `Seriale`, section key `2`

V1 should discover all sections through:

```http
GET /library/sections?X-Plex-Token=...
```

Then scan sections where `type == movie`. On the current server this means section key `1`, but the implementation should not hard-code that key.

Movie metadata is available through:

```http
GET /library/sections/{sectionKey}/all?type=1&X-Plex-Token=...
GET /library/metadata/{ratingKey}?X-Plex-Token=...
```

Plex exposes external IDs in nested `Guid` entries. Example:

```text
imdb://tt0147800
tmdb://4951
tvdb://836
```

The top-level Plex GUID is not enough for matching, so V1 must parse nested GUIDs.

## Data Model

Add a MongoDB collection:

```text
plex_library_items
```

Each Plex movie inventory document should store:

- `id`: stable backend ID, for example `plex-movie-{ratingKey}`.
- `ratingKey`: Plex rating key.
- `mediaType`: `movie`.
- `title`.
- `year`.
- `librarySectionKey`.
- `librarySectionTitle`.
- `plexGuid`.
- `imdbId`, when present.
- `tmdbId`, when present.
- `tvdbId`, when present.
- `lastSeenAt`.

The collection should support replacing stale inventory. If a Plex movie disappears from the current scan, it should be removed or marked stale. Recommendation for V1: delete stale movie documents for scanned movie sections so the inventory mirrors Plex.

Watchlist movie documents should gain match trace fields:

- `plexRatingKey`.
- `plexMatchedAt`.
- `plexMatchReason`: `imdb`, `tmdb`, `title_year`, `none`, or `ambiguous`.
- `plexMatchConfidence`: `exact`, `none`, or `ambiguous`.

These fields are backend diagnostics for V1. They do not need to be exposed to Android yet.

## Matching Rules

Run matching after inventory sync.

For each watchlist movie:

1. Match by IMDb ID if both sides have an IMDb ID.
2. Match by TMDB ID if both sides have a TMDB ID.
3. If no ID match exists, fallback to exact normalized title plus exact same year.
4. If a fallback finds exactly one Plex movie, treat it as available.
5. If fallback finds multiple candidates, mark the watchlist item as `unknown_match`.
6. If no match exists, mark the movie as `not_on_plex`, unless it is unreleased and there is no Plex match.
7. If a confident Plex match exists, mark the movie as `available_on_plex`, even when release metadata says `unreleased`.

Normalization for fallback matching:

- Case-insensitive.
- Trim whitespace.
- Remove punctuation.
- Collapse repeated whitespace.
- Do not do fuzzy distance matching in V1.

The fallback is intentionally conservative. Weak matches should not be hidden as available.

## Sync Flow

### POST /api/sync/plex/movies

Manual Plex movie sync.

Flow:

1. Validate `Plex:BaseUrl` and `Plex:Token`.
2. Fetch Plex sections.
3. Select movie sections.
4. Fetch all movie entries for each movie section.
5. Fetch per-movie metadata where nested GUIDs are needed.
6. Upsert current Plex movie inventory and delete stale movie inventory for scanned sections.
7. Match all watchlist movies against the current Plex movie inventory.
8. Update watchlist availability and Plex match trace fields.
9. Insert a sync run status such as `plex_movies_completed`.

Response shape:

```json
{
  "status": "completed",
  "startedAt": "2026-06-05T12:00:00Z",
  "finishedAt": "2026-06-05T12:00:05Z",
  "sectionsScanned": 1,
  "itemsFetched": 500,
  "itemsUpserted": 500,
  "itemsDeleted": 2,
  "watchlistItemsMatched": 40,
  "watchlistItemsNotMatched": 220,
  "watchlistItemsUnknown": 3
}
```

### POST /api/sync/all

Manual combined refresh.

Flow:

1. Run Letterboxd movie sync.
2. Run TMDB movie enrichment.
3. Run Plex movie sync and matching.

This endpoint should return a combined summary with the sub-results. If one step fails, the endpoint should return the appropriate dependency error and should not run later dependent steps.

## Error Handling

Plex dependency errors:

- Missing Plex config: `503 Service Unavailable`.
- Plex unreachable: `503 Service Unavailable`.
- Plex unauthorized: `503 Service Unavailable`.
- Plex malformed XML or unexpected response shape: `502 Bad Gateway`.

MongoDB dependency errors should keep the existing `503 Service Unavailable` behavior.

Plex sync must be read-only toward Plex. It must not mutate Plex libraries, metadata, collections, or watched state.

## API And Android Impact

Existing watchlist endpoints keep their current shape. Android receives the same `availabilityStatus` values as today:

- `available_on_plex`.
- `not_on_plex`.
- `unreleased`.
- `unknown_match`.

No Android implementation is required for V1. The existing Android TV availability filter should become useful once backend matching updates the stored status.

A future Plex library tab can use the `plex_library_items` collection through a new endpoint, for example:

```http
GET /api/plex/movies
```

That endpoint is not part of V1.

## Testing Strategy

Application tests:

- Plex matching by IMDb ID.
- Plex matching by TMDB ID.
- Conservative exact title/year fallback.
- Ambiguous title/year fallback returns `unknown_match`.
- No match returns `not_on_plex` for released movies.
- No match preserves `unreleased` for unreleased movies.
- Confident Plex match wins over `unreleased`.

Infrastructure tests:

- Plex XML client parses library sections.
- Plex XML client parses movie entries.
- Plex XML client parses nested external GUIDs.
- Mongo Plex inventory repository upserts current inventory.
- Mongo Plex inventory repository deletes stale scanned-section inventory.
- Mongo watchlist repository applies Plex match updates without erasing Letterboxd or TMDB metadata.

API tests:

- `POST /api/sync/plex/movies` returns a summary.
- Missing Plex config returns `503`.
- Plex malformed XML returns `502`.
- `POST /api/sync/all` runs sub-services in order.

Live smoke test:

```powershell
Invoke-RestMethod -Method Post http://localhost:5000/api/sync/plex/movies
Invoke-RestMethod 'http://localhost:5000/api/watchlist?collection=movie&availability=plex&sort=title_asc'
```

The second command should return at least known Plex-owned watchlist movies, such as `10 Things I Hate About You`, if present in both Letterboxd and Plex.

## Open Follow-Ups

- Add Android "Plex Library" tab that shows all Plex movies regardless of watchlist membership.
- Add Plex TV matching after TMDB TV watchlist sync exists.
- Expose Plex match diagnostics in a debug endpoint if mismatches are hard to inspect.
- Add scheduled or startup combined sync after manual sync behavior is stable.
