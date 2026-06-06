# API Contract

This document describes the implemented backend API contract for the read-only watchlist backend.

## GET /api/watchlist

Returns watchlist items for the selected collection, availability states, and sort order.

### Query Parameters

| Name | Values | Description |
| --- | --- | --- |
| `collection` | `all`, `movie`, `tv` | Selects all items, only movies, or only TV shows. Defaults to `all`. |
| `availability` | `plex`, `not_on_plex`, `unreleased`, `unknown_match` | Comma-separated availability states. Defaults to all four states. |
| `sort` | `added_desc`, `title_asc` | Sorts by watchlist-added date descending or title ascending. Defaults to `added_desc`. |

### Availability Behavior

- `availability=plex` returns items whose API `availabilityStatus` is `available_on_plex`.
- `availability=not_on_plex` returns released items that are missing from Plex.
- `availability=unreleased` returns wanted items that are not released yet.
- `availability=unknown_match` returns items whose Plex match is uncertain.
- Multiple values are combined with commas, for example `availability=plex,unknown_match`.

### Validation Errors

Invalid query values return `400 Bad Request`:

```json
{ "error": "Invalid collection." }
```

```json
{ "error": "Invalid availability." }
```

```json
{ "error": "Invalid sort." }
```

### Example

Request:

```http
GET /api/watchlist?collection=all&availability=plex,not_on_plex,unreleased,unknown_match&sort=added_desc
```

Response:

```json
[
  {
    "id": "movie-dune-part-two",
    "mediaType": "movie",
    "source": "letterboxd",
    "sourceId": "letterboxd-dune-part-two",
    "title": "Dune: Part Two",
    "year": 2024,
    "overview": "Paul Atreides unites with Chani and the Fremen while seeking revenge against the conspirators who destroyed his family.",
    "posterUrl": "/api/images/tmdb/w500/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg",
    "backdropUrl": "/api/images/tmdb/w1280/xOMo8BRK7PfcJv9JCnx7s5hj0PX.jpg",
    "releaseStatus": "released",
    "availabilityStatus": "available_on_plex",
    "vodReleaseKnown": true,
    "releasedOnVod": true,
    "vodRegions": ["PL", "US"],
    "addedAt": "2026-05-20T10:00:00+02:00",
    "updatedAt": "2026-05-25T10:00:00+02:00"
  }
]
```

## GET /api/watchlist/{id}

Returns one watchlist item by backend item ID.

- `200 OK` when the item exists.
- `404 Not Found` when the item does not exist.

Example:

```http
GET /api/watchlist/movie-dune-part-two
```

Response:

```json
{
  "id": "movie-dune-part-two",
  "mediaType": "movie",
  "source": "letterboxd",
  "sourceId": "letterboxd-dune-part-two",
  "title": "Dune: Part Two",
  "year": 2024,
  "overview": "Paul Atreides unites with Chani and the Fremen while seeking revenge against the conspirators who destroyed his family.",
  "posterUrl": "/api/images/tmdb/w500/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg",
  "backdropUrl": "/api/images/tmdb/w1280/xOMo8BRK7PfcJv9JCnx7s5hj0PX.jpg",
  "releaseStatus": "released",
  "availabilityStatus": "available_on_plex",
  "vodReleaseKnown": true,
  "releasedOnVod": true,
  "vodRegions": ["PL", "US"],
  "addedAt": "2026-05-20T10:00:00+02:00",
  "updatedAt": "2026-05-25T10:00:00+02:00"
}
```

`vodReleaseKnown` is `true` after successful TMDB metadata enrichment. `releasedOnVod` is `true` when TMDB watch-provider data shows at least one stream, rent, or buy option in Poland or the US. Android uses `vodReleaseKnown=true` plus `releasedOnVod=false` on non-Plex items to show a `Not released` badge instead of a generic `Unavailable` badge.

## GET /api/export/radarr/movies

Returns a Radarr/Letterboxd-compatible movie list containing Letterboxd watchlist movies that are not already available on the user's subscribed VOD services.

This endpoint uses cached MongoDB data only. It does not call Letterboxd or TMDB while handling the request.

Response:

```json
[
  {
    "id": 1297842,
    "imdb_id": "tt27613895",
    "title": "GOAT",
    "release_year": "2026",
    "clean_title": "/film/goat-2026/",
    "adult": false
  }
]
```

Filtering:

- Includes Letterboxd movie watchlist items with no cached subscribed-service availability.
- Excludes movies whose cached TMDB enrichment has `OwnedServiceAvailability` entries.
- Missing TMDB enrichment does not exclude a movie.
- Plex availability does not affect this endpoint.

## GET /api/export/sonarr/tv

Returns TV shows that are not already available on subscribed VOD services.

Version 1 reserves the endpoint and returns an empty array until TMDB TV watchlist sync and a Sonarr-compatible TV export shape are implemented.

Response:

```json
[]
```

## GET /api/images/tmdb/{size}/{fileName}

Proxies TMDB artwork through the backend. Android clients should use the `posterUrl` and `backdropUrl` values returned by watchlist endpoints and should not call TMDB image hosts directly.

- `200 OK` with image bytes when TMDB returns the image.
- `400 Bad Request` for unsupported image sizes.
- `404 Not Found` when TMDB has no image for the path.
- `502 Bad Gateway` when the TMDB image host cannot be reached or returns a non-success response.

Supported image sizes are currently `w500` and `w1280`.

## GET /api/sync/status

Returns the current backend sync status.

The latest sync status is read from MongoDB. The initial bootstrap response is:

```json
{
  "status": "seeded",
  "lastSuccessfulSyncAt": "2026-05-25T10:00:00+02:00"
}
```

## POST /api/sync/letterboxd

Runs a manual import of the configured Letterboxd movie watchlist.

Response:

```json
{
  "status": "completed",
  "startedAt": "2026-06-03T12:00:00Z",
  "finishedAt": "2026-06-03T12:00:01Z",
  "itemsFetched": 27,
  "itemsUpserted": 27,
  "itemsDeleted": 3
}
```

Dependency errors:

- `503 Service Unavailable` when the Letterboxd proxy is unavailable.
- `502 Bad Gateway` when the Letterboxd proxy returns malformed JSON.

## POST /api/sync/tmdb/movies

Runs manual TMDB enrichment for all existing Letterboxd movie records.

The batch sync records per-movie not-found or dependency failures on the movie metadata status and continues processing the rest of the list. A dependency failure for one movie therefore returns a `partial` result rather than a top-level `503`.

Response:

```json
{
  "status": "completed",
  "startedAt": "2026-06-04T12:00:00Z",
  "finishedAt": "2026-06-04T12:00:01Z",
  "itemsMatched": 27,
  "itemsEnriched": 26,
  "itemsNotFound": 1,
  "itemsFailed": 0
}
```

## POST /api/sync/tmdb/movies/{id}

Runs manual TMDB enrichment for one existing Letterboxd movie record.

- `200 OK` when the movie exists and sync returns a result.
- `404 Not Found` when the backend item ID does not exist or is not a Letterboxd movie.
- `503 Service Unavailable` when TMDB is unavailable.

Example:

```http
POST /api/sync/tmdb/movies/movie-letterboxd-1297842
```

Response:

```json
{
  "status": "enriched",
  "id": "movie-letterboxd-1297842",
  "tmdbId": 1297842
}
```

When TMDB cannot find the movie, the endpoint records `not_found` metadata status and returns:

```json
{
  "status": "not_found",
  "id": "movie-letterboxd-1297842",
  "tmdbId": null
}
```

## POST /api/sync/plex/movies

Runs a manual Plex movie inventory sync and updates watchlist movie availability.

Response:

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

Dependency errors:

- `503 Service Unavailable` with `{ "error": "Plex is unavailable." }`
- `502 Bad Gateway` with `{ "error": "Plex returned malformed XML." }`

## POST /api/sync/availability/refresh

Runs the app-open availability refresh. The backend checks the latest successful Plex movie sync and only runs Plex sync when the cached availability is missing or stale.

Freshness window: 15 minutes.

Skipped response:

```json
{
  "status": "skipped",
  "ranPlexSync": false,
  "reason": "fresh",
  "startedAt": "2026-06-05T12:00:00Z",
  "finishedAt": "2026-06-05T12:00:00Z",
  "plex": null
}
```

Completed response:

```json
{
  "status": "completed",
  "ranPlexSync": true,
  "reason": "stale",
  "startedAt": "2026-06-05T12:00:00Z",
  "finishedAt": "2026-06-05T12:00:05Z",
  "plex": {
    "status": "completed",
    "sectionsScanned": 1,
    "itemsFetched": 500,
    "itemsUpserted": 500,
    "itemsDeleted": 2,
    "watchlistItemsMatched": 40,
    "watchlistItemsNotMatched": 220,
    "watchlistItemsUnknown": 3
  }
}
```

`reason` values:

- `fresh`: latest successful Plex movie sync is inside the freshness window.
- `stale`: latest successful Plex movie sync is older than the freshness window.
- `missing`: no previous successful Plex movie sync is known.

Dependency errors match `POST /api/sync/plex/movies`.

## POST /api/sync/all

Runs Letterboxd movie sync, TMDB movie enrichment, and Plex movie sync in order.

Response:

```json
{
  "status": "completed",
  "startedAt": "2026-06-05T12:00:00Z",
  "finishedAt": "2026-06-05T12:00:03Z",
  "letterboxd": {
    "status": "completed",
    "startedAt": "2026-06-05T12:00:00Z",
    "finishedAt": "2026-06-05T12:00:01Z",
    "itemsFetched": 27,
    "itemsUpserted": 27,
    "itemsDeleted": 3
  },
  "tmdbMovies": {
    "status": "completed",
    "startedAt": "2026-06-05T12:00:01Z",
    "finishedAt": "2026-06-05T12:00:02Z",
    "itemsMatched": 27,
    "itemsEnriched": 26,
    "itemsNotFound": 1,
    "itemsFailed": 0
  },
  "plexMovies": {
    "status": "completed",
    "startedAt": "2026-06-05T12:00:02Z",
    "finishedAt": "2026-06-05T12:00:03Z",
    "sectionsScanned": 1,
    "itemsFetched": 500,
    "itemsUpserted": 500,
    "itemsDeleted": 2,
    "watchlistItemsMatched": 40,
    "watchlistItemsNotMatched": 220,
    "watchlistItemsUnknown": 3
  }
}
```

Dependency errors from any sub-sync propagate to the combined endpoint response.

## Dependency Errors

When MongoDB is unavailable, MongoDB-backed endpoints return:

```http
503 Service Unavailable
```

```json
{
  "error": "MongoDB is unavailable."
}
```

When TMDB is unavailable for a single-movie sync, the backend returns:

```http
503 Service Unavailable
```

```json
{
  "error": "TMDB is unavailable."
}
```
