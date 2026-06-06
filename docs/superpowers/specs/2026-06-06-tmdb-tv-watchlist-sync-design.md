# TMDB TV Watchlist Sync Design

## Goal

Import the user's TMDB account TV watchlist into the backend read model so the existing Android TV `TV Shows` and `All` collections show real TV records from MongoDB.

## External API Assumptions

- TMDB exposes account TV watchlist data at `GET /3/account/{account_id}/watchlist/tv`, with `language`, `page`, `session_id`, and `sort_by` query parameters. The endpoint returns paginated TV results with `id`, `name`, `original_name`, `overview`, `first_air_date`, `poster_path`, `backdrop_path`, `genre_ids`, `original_language`, `vote_average`, and `vote_count`.
- TMDB user authentication for account calls is controlled with a `session_id` query parameter.
- TMDB exposes TV show details at `GET /3/tv/{series_id}`. Use this per show to enrich genre names and status when the watchlist payload does not contain enough normalized detail data.
- TMDB exposes TV external IDs at `GET /3/tv/{series_id}/external_ids`. This sync will store TVDB and IMDb IDs when present for later Plex TV matching.

Sources checked on June 6, 2026:

- https://developer.themoviedb.org/reference/account-watchlist-tv
- https://developer.themoviedb.org/docs/authentication-user
- https://developer.themoviedb.org/reference/tv-series-details
- https://developer.themoviedb.org/reference/tv-series-external-ids

## Scope

In scope:

- Backend-only TMDB TV watchlist sync.
- New local configuration for `Tmdb:AccountId`, `Tmdb:SessionId`, and optional `Tmdb:Language`.
- A TMDB TV client that reads all watchlist pages and fetches per-show details plus external IDs.
- A TV sync application service that normalizes TV shows into `watchlist_items`.
- Mongo persistence for TMDB TV records, including deletion of removed TMDB TV watchlist items.
- Manual API trigger `POST /api/sync/tmdb/tv`.
- Inclusion of TV sync in `POST /api/sync/all`, before Plex movie sync.
- Focused unit/API/repository tests and docs updates.

Out of scope:

- Plex TV inventory sync or TV availability matching.
- Filling `GET /api/export/sonarr/tv`.
- Android UI redesign.
- Watchlist mutation flows.
- Streaming-provider badges for TV shows.
- Local image-byte caching.

## Chosen Approach

Use one TV sync service that imports and enriches TV records in a single pass.

The TMDB TV watchlist endpoint is the source of truth for which shows are wanted. For each watchlist result, the backend fetches TV details and external IDs to build a normalized record with the same fields Android already understands: title, year, overview, poster/backdrop URLs, release status, genres, original language, vote data, and TMDB metadata status.

This keeps TV watchlist behavior parallel to the existing movie pipeline without forcing the same two-step Letterboxd import then TMDB enrichment split. Movies need two stages because Letterboxd is the wanted-list source and TMDB is only enrichment. TV shows come from TMDB already, so a single sync service is simpler and still testable behind interfaces.

## Alternatives Considered

### Approach A: Use Watchlist Payload Only

This would fetch only `account/{id}/watchlist/tv` and write records directly. It is faster and uses fewer TMDB requests, but it loses genre names, official status, runtime, and external IDs that are useful for details and future Plex matching.

### Approach B: Separate TV Import And TV Enrichment

This would mirror the movie split with a raw TV watchlist import followed by a separate enrichment endpoint. It is consistent with movie code, but it adds extra endpoints and intermediate states that do not buy much because TMDB owns both the TV watchlist and the TV metadata.

### Approach C: Single Import And Enrichment Service

This is the selected approach. It keeps the user-facing operation simple, produces fully useful TV records immediately, and still separates concerns internally with `ITmdbTvWatchlistClient`, `ITmdbTvMetadataClient`, and repository boundaries.

## Data Model

TV watchlist records use the existing `watchlist_items` collection.

Normalized domain fields:

- `Id`: `tv-tmdb-{tmdbId}`.
- `MediaType`: `TvShow`.
- `Source`: `Tmdb`.
- `SourceId`: TMDB TV series ID as a string.
- `Title`: TMDB `name`.
- `Year`: parsed year from `first_air_date`, when available.
- `Overview`: TMDB overview.
- `PosterUrl`: `Tmdb:ImageBaseUrl + /w500 + poster_path`, when available.
- `BackdropUrl`: `Tmdb:ImageBaseUrl + /w1280 + backdrop_path`, when available.
- `ReleaseStatus`: derived from TV details status and first air date.
- `AvailabilityStatus`: initially `NotOnPlex` for released/returning/ended shows, `Unreleased` for future first air dates, and `UnknownMatch` when release state cannot be determined.
- `AddedAt`: preserved from an existing stored TV record, otherwise sync time.
- `UpdatedAt`: current sync time.

Mongo-only trace/enrichment fields:

- `TmdbId`.
- `TmdbTitle`.
- `OriginalTitle`.
- `ReleaseDate`: first air date string.
- `Genres`.
- `OriginalLanguage`.
- `TmdbVoteAverage`.
- `TmdbVoteCount`.
- `PosterPath`.
- `BackdropPath`.
- `TmdbMetadataUpdatedAt`.
- `TmdbMetadataStatus`: `enriched`, `not_found`, or `failed`.
- `TmdbMetadataError`.
- `TvdbId`.
- `ImdbId`.

`TvdbId` is a new nullable Mongo document field. It does not need to be exposed in the Android DTO for this slice.

## Release Status Rules

Use deterministic backend rules:

- If `first_air_date` is in the future, set `ReleaseStatus.Unreleased`.
- If details status is `Ended`, `Returning Series`, `Canceled`, or `In Production` and first air date is not future, set `ReleaseStatus.Released`.
- If first air date is missing and status is missing or unrecognized, set `ReleaseStatus.Unknown`.

Availability initialization follows release status:

- `Unreleased` -> `AvailabilityStatus.Unreleased`.
- `Unknown` -> `AvailabilityStatus.UnknownMatch`.
- Otherwise -> `AvailabilityStatus.NotOnPlex`.

When an existing TV record is re-synced, preserve any existing Plex fields and availability status. This lets the later Plex TV matching slice update availability without being overwritten by a regular TMDB TV watchlist refresh.

## Sync Behavior

`ITmdbTvWatchlistClient.GetTvShowsAsync` fetches all pages with `sort_by=created_at.desc`. The service treats the returned set as authoritative.

For each TV result:

1. Fetch details with `ITmdbTvMetadataClient.GetTvMetadataAsync(tmdbId)`.
2. Fetch external IDs inside the same metadata call.
3. Convert metadata into `WatchlistItemWriteModel`.
4. Upsert the Mongo document.

After all source records are processed, delete TMDB TV records no longer present in the watchlist source set. Do not delete Letterboxd movie records, seeded movie records, or future non-TMDB TV sources.

Batch behavior:

- Missing details for one show increments `itemsNotFound` and continues. If an older stored TV record already exists for that TMDB ID, it is preserved because the source ID remains present in the authoritative watchlist set.
- Dependency failures for one show increment `itemsFailed` and continue. If an older stored TV record already exists for that TMDB ID, it is preserved for the same reason.
- The endpoint returns `completed` when there are no failures and `partial` when at least one item failed.
- Missing account configuration is a top-level TMDB dependency failure because the backend cannot enumerate the watchlist.

## API

Add:

```http
POST /api/sync/tmdb/tv
```

Response:

```json
{
  "status": "completed",
  "startedAt": "2026-06-06T12:00:00Z",
  "finishedAt": "2026-06-06T12:00:02Z",
  "itemsFetched": 14,
  "itemsUpserted": 14,
  "itemsDeleted": 2,
  "itemsEnriched": 14,
  "itemsNotFound": 0,
  "itemsFailed": 0
}
```

Update `POST /api/sync/all` to run:

1. Letterboxd movie sync.
2. TMDB movie enrichment.
3. TMDB TV watchlist sync.
4. Plex movie sync.

Plex movie sync remains last so movie availability reflects the latest movie records.

## Android Impact

No Android code is required for this slice. The existing `collection=tv` and `collection=all` API paths already flow through Android's current browse UI. TV records will render with existing poster, title, badge, and detail-screen behavior.

Until Plex TV matching exists, TV records will show `Unavailable`, `Not released`, or `Match uncertain` based on the initialized availability status.

## Testing

Backend application tests:

- TV sync maps TMDB metadata into `MediaType.TvShow` records.
- Existing TV records preserve `AddedAt` and availability fields.
- Removed TMDB TV watchlist items are deleted while movie and non-TMDB records remain.
- Per-item not-found and dependency failures are counted without blocking other items.
- Release status and initial availability rules are deterministic.

Infrastructure tests:

- TMDB TV watchlist client fetches all pages and sends `session_id`, `language`, `sort_by`, and page values.
- TMDB TV metadata client parses details and external IDs.
- Missing account/session configuration throws `TmdbUnavailableException`.
- Mongo repository upsert/delete logic stores TV trace fields and sync-run status.

API tests:

- `POST /api/sync/tmdb/tv` returns the expected result DTO.
- TMDB dependency errors return the existing dependency error behavior.
- `POST /api/sync/all` includes TV sync in order.
- `GET /api/watchlist?collection=tv` returns synced TV records through the existing read model.

Docs:

- `docs/api.md`: add the new endpoint and combined sync shape.
- `docs/integrations.md`: replace the "Still needed for TV watchlist" section with implemented behavior and remaining Plex TV matching gap.
- `docs/architecture.md`: update sync pipeline and API surface.
- `docs/todo.md`: if present locally, mark TMDB TV watchlist sync complete after implementation and keep Plex TV matching as the next follow-up.

## Success Criteria

- Running `POST /api/sync/tmdb/tv` with valid local TMDB account credentials writes real TV records to MongoDB.
- `GET /api/watchlist?collection=tv` returns those records without Android calling TMDB directly.
- Re-running the sync is idempotent and preserves stable `AddedAt` values.
- Removing a show from the TMDB account watchlist removes the corresponding backend TMDB TV record on the next sync.
- One failed TV metadata item does not prevent the rest of the TV watchlist from importing.
