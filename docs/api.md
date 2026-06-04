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
    "posterUrl": "https://image.tmdb.org/t/p/w500/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg",
    "backdropUrl": "https://image.tmdb.org/t/p/w1280/xOMo8BRK7PfcJv9JCnx7s5hj0PX.jpg",
    "releaseStatus": "released",
    "availabilityStatus": "available_on_plex",
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
  "posterUrl": "https://image.tmdb.org/t/p/w500/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg",
  "backdropUrl": "https://image.tmdb.org/t/p/w1280/xOMo8BRK7PfcJv9JCnx7s5hj0PX.jpg",
  "releaseStatus": "released",
  "availabilityStatus": "available_on_plex",
  "addedAt": "2026-05-20T10:00:00+02:00",
  "updatedAt": "2026-05-25T10:00:00+02:00"
}
```

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
