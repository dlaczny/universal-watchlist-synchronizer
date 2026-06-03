# Watchlist Collection API Design

## Goal

Replace the current Android TV watchlist query contract with a backend-owned collection API that supports the enabled `All` tab, source-aware availability filtering, and real date-added sorting.

The app is still local-only and not deployed, so this can be a breaking API/client change.

## Endpoint

Use the existing endpoint with a breaking query contract:

```http
GET /api/watchlist
```

Query parameters:

| Name | Values | Default | Description |
| --- | --- | --- | --- |
| `collection` | `all`, `movie`, `tv` | `all` | Selects the combined collection, movies only, or TV shows only. |
| `availability` | comma-separated `plex`, `not_on_plex`, `unreleased`, `unknown_match` | all values | Selects availability states to include. |
| `sort` | `added_desc`, `title_asc` | `added_desc` | Selects backend ordering. |

Example:

```http
GET /api/watchlist?collection=all&availability=plex,not_on_plex,unknown_match&sort=added_desc
```

## Availability Mapping

API query values map to backend availability states as follows:

| Query value | DTO availability state |
| --- | --- |
| `plex` | `available_on_plex` |
| `not_on_plex` | `not_on_plex` |
| `unreleased` | `unreleased` |
| `unknown_match` | `unknown_match` |

The Android TV UI can keep one simple `Unavailable` checkbox by expanding it to `not_on_plex,unreleased,unknown_match` when checked.

## Sorting

`sort=added_desc` sorts by newest watchlist addition first. If multiple records have the same `AddedAt`, preserve repository order as the tie-breaker.

`sort=title_asc` sorts by title A-Z using a case-insensitive comparison.

## DTO

The response keeps the existing item fields and adds `addedAt`.

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

`addedAt` means when the item entered the wanted watchlist. `updatedAt` means when backend metadata, availability, or sync-derived record data last changed.

## Data Model

Add `AddedAt` to:

- Domain `WatchlistItem`.
- Application `WatchlistItemDto`.
- MongoDB `watchlist_items` document.
- Seed data.

MongoDB documents created before this change may not have `AddedAt`. During local development, map missing `AddedAt` to `UpdatedAt` so existing volumes keep working. Future sync jobs should write a real source-derived watchlist-added timestamp.

Seed records should use deterministic `AddedAt` values so tests and local UI ordering stay stable.

## Validation

Invalid query values return `400 Bad Request` with a concise error body.

Invalid cases:

- Unknown `collection`.
- Unknown `sort`.
- Unknown availability value.
- Empty `availability` list after parsing.

The endpoint should accept omitted query parameters and use defaults.

## Android TV Migration

The Android TV UI stays visually unchanged, but request construction moves to the new backend-owned query contract.

Mapping:

| Android UI state | Backend query |
| --- | --- |
| All | `collection=all` |
| Movies | `collection=movie` |
| TV Shows | `collection=tv` |
| Date added | `sort=added_desc` |
| A-Z | `sort=title_asc` |
| On Plex only | `availability=plex` |
| On Plex + Unavailable | `availability=plex,not_on_plex,unreleased,unknown_match` |

After migration, the `All` tab becomes enabled. Android no longer needs to perform primary collection filtering, availability filtering, or sorting locally.

## Out Of Scope

- Live Letterboxd import.
- Live TMDB account watchlist import.
- Plex inventory sync.
- Streaming-provider availability.
- Advanced Android UI for selecting individual unavailable reasons.

## Testing

Backend tests should cover:

- Default query returns all collection items ordered by `AddedAt` descending.
- `collection=movie`, `collection=tv`, and `collection=all`.
- Split availability values, including mixed sets.
- `sort=title_asc`.
- Invalid query values returning `400`.
- DTO contains both `addedAt` and `updatedAt`.
- MongoDB document mapping falls back from missing `AddedAt` to `UpdatedAt`.

Android tests should cover:

- API request URL construction for collection, sort, and availability states.
- The `All` tab is enabled through the new request model.
- Local organizer filtering/sorting is removed or no longer used for primary API-backed browsing.

