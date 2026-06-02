# API Contract

This document describes the implemented backend API contract for the read-only watchlist backend.

## GET /api/watchlist

Returns watchlist items for a selected media type.

### Query Parameters

| Name | Values | Description |
| --- | --- | --- |
| `mediaType` | `movie`, `tv` | Selects movies or TV shows. |
| `filter` | `all`, `available` | Selects all wanted items or only items available on Plex. |

### Filter Behavior

- `filter=all` returns every wanted item for the selected `mediaType`.
- `filter=available` returns only items whose `availabilityStatus` is `available_on_plex`.

### Example

Request:

```http
GET /api/watchlist?mediaType=movie&filter=available
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
