# Letterboxd Movie Sync Design

## Goal

Add a manual backend sync that imports the user's Letterboxd movie watchlist into MongoDB so the Android TV app can browse the real movie wanted list before Plex availability matching is implemented.

## Scope

This slice only syncs movies from Letterboxd into the backend read model.

In scope:

- Manual API trigger: `POST /api/sync/letterboxd`.
- Fetch JSON from `https://letterboxd-list-radarr.onrender.com/example-user/watchlist`.
- Upsert movie records into MongoDB `watchlist_items`.
- Remove existing Letterboxd movie records that are no longer present in the fetched watchlist.
- Preserve TV records and non-Letterboxd records.
- Preserve existing availability and metadata when updating an existing record.
- Record a sync status entry.

Out of scope:

- Plex availability matching.
- TMDB metadata enrichment.
- TMDB TV watchlist sync.
- Scheduled background sync.
- Android UI mutation flows.

## Source JSON

The Letterboxd proxy returns an array of movie objects:

```json
[
  {
    "id": 1418998,
    "imdb_id": "tt35450621",
    "title": "Karma",
    "release_year": "2026",
    "clean_title": "/film/karma-2026/",
    "adult": false
  }
]
```

Field handling:

- `id`: required source identifier.
- `imdb_id`: optional stable external identifier, stored for future matching when available.
- `title`: required display title.
- `release_year`: optional string; empty or non-numeric means unknown year.
- `clean_title`: optional Letterboxd path; store the value when present.
- `adult`: ignored for v1; records are imported even when this field is absent.

## Data Mapping

Each imported movie becomes a `WatchlistItem`:

- `Id`: `movie-letterboxd-{id}`.
- `MediaType`: `Movie`.
- `Source`: `Letterboxd`.
- `SourceId`: the numeric `id` converted to string.
- `Title`: JSON `title`.
- `Year`: parsed `release_year`, or `null` when missing.
- `Overview`: preserve existing value, otherwise `null`.
- `PosterUrl`: preserve existing value, otherwise `null`.
- `BackdropUrl`: preserve existing value, otherwise `null`.
- `ReleaseStatus`:
  - `Unknown` when `release_year` is missing or invalid.
  - `Unreleased` when parsed year is greater than the current UTC year.
  - `Released` otherwise.
- `AvailabilityStatus`:
  - Preserve existing value when updating an existing record.
  - `Unreleased` for newly imported future-year records.
  - `UnknownMatch` for newly imported unknown-year records.
  - `NotOnPlex` for newly imported released records.
- `AddedAt`: preserve existing value when updating; otherwise use sync start time.
- `UpdatedAt`: sync start time.

The Mongo document will include source trace fields for future integrations:

- `ImdbId`
- `LetterboxdPath`

These fields are backend-owned metadata. They are not part of the Android DTO yet.

## Deletion Semantics

Letterboxd is the source of truth for movie wanted items.

After a successful fetch and parse:

- Delete Mongo `watchlist_items` where `MediaType == Movie` and `Source == Letterboxd` and `SourceId` is not present in the fetched list.
- Do not delete TV records.
- Do not delete records from other sources.
- Do not perform deletions if fetching or parsing fails.

## API Behavior

`POST /api/sync/letterboxd` runs one sync immediately.

Successful response:

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

Failure response:

- Return `503 Service Unavailable` when the Letterboxd proxy cannot be reached or returns a non-success status.
- Return `502 Bad Gateway` when the Letterboxd proxy returns malformed JSON.
- Do not modify MongoDB on fetch or parse failure.

## Sync Status

On successful sync, insert a sync run document that allows `/api/sync/status` to reflect the latest successful backend sync.

For this slice, write status as `letterboxd_completed`. A later sync status model can split integration-specific status details.

## Configuration

Add configuration under the backend service:

```json
{
  "Letterboxd": {
    "WatchlistUrl": "https://letterboxd-list-radarr.onrender.com/example-user/watchlist"
  }
}
```

The URL must be overridable through environment variables for local testing.

No credentials are required for this Letterboxd proxy.

## Testing

Backend tests must use deterministic fake HTTP responses and fake repositories. No automated test may call the live Letterboxd proxy.

Required coverage:

- JSON parser maps the sample schema.
- Empty `release_year` maps to unknown year and `Unknown` release status.
- Future-year records map to `Unreleased`.
- Existing records preserve `AddedAt`, availability, and artwork/overview metadata.
- Removed Letterboxd movie records are deleted.
- TV and non-Letterboxd records are not deleted.
- Failed fetch does not change MongoDB.
- Malformed JSON does not change MongoDB.
- `POST /api/sync/letterboxd` returns the sync summary.

## Open Follow-Ups

- TMDB movie metadata enrichment for poster, backdrop, overview, and release dates.
- Plex library sync and matching by IMDb ID first, then title/year fallback.
- Better sync status history with per-integration success and failure details.
- Scheduled sync after the manual trigger has proven reliable.
