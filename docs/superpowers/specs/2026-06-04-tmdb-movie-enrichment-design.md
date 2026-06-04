# TMDB Movie Enrichment Design

## Goal

Add a backend TMDB movie enrichment sync so imported Letterboxd movie records gain posters, backdrops, descriptions, release metadata, watch-provider data, owned-service availability, and VOD-release flags.

This makes the Android TV movie grid useful before Plex matching exists, while keeping Android clients read-only and dependent only on the backend API.

## Scope

In scope:

- Manual API trigger for all imported movies: `POST /api/sync/tmdb/movies`.
- Manual API trigger for one imported movie: `POST /api/sync/tmdb/movies/{id}`.
- Backend-only TMDB API client using a configured bearer token.
- Enrich existing Letterboxd movie records stored in MongoDB.
- Store TMDB metadata directly on `watchlist_items`.
- Store poster/backdrop URLs or paths, not image bytes.
- Store watch-provider data for Poland and the United States.
- Compute owned-service availability for Poland.
- Compute a separate `releasedOnVod` flag from Poland or United States provider data.
- Keep basic Letterboxd cards visible when TMDB enrichment is missing or fails for a movie.

Out of scope:

- Android provider/VOD badge rendering.
- Actual image-byte caching in MongoDB/GridFS.
- Plex availability matching.
- TMDB TV watchlist sync.
- Automatic scheduled sync.
- Streaming-service provider id refinement beyond this first stored model.

## TMDB Configuration

TMDB credentials must stay in backend configuration and must not be committed.

Local development uses:

```powershell
$env:TMDB__AccessToken = "<TMDB v4 API read access token>"
```

Backend configuration:

```json
{
  "Tmdb": {
    "AccessToken": "",
    "BaseUrl": "https://api.themoviedb.org/3",
    "ImageBaseUrl": "https://image.tmdb.org/t/p"
  }
}
```

`AccessToken` is required for TMDB sync endpoints. Missing or invalid credentials return a dependency-style error from the API.

## TMDB Lookup

The Letterboxd proxy currently gives movie rows like:

```json
{
  "id": 1297842,
  "imdb_id": "tt27613895",
  "title": "GOAT",
  "release_year": "2026",
  "clean_title": "/film/goat-2026/",
  "adult": false
}
```

The first lookup strategy is direct TMDB movie id:

- `SourceId` from Letterboxd proxy `id` is treated as the candidate TMDB movie id.
- For example, `1297842` maps to `https://www.themoviedb.org/movie/1297842`.

If direct lookup fails with not found or returns data that cannot be used:

- Use `imdb_id` as fallback when present through TMDB external-id lookup.
- If fallback also fails, keep the base Letterboxd record unchanged except for recording enrichment failure metadata.

## TMDB API Calls

For each movie being enriched:

1. Fetch movie details:

```http
GET /3/movie/{movie_id}
Authorization: Bearer <token>
```

2. Fetch watch providers:

```http
GET /3/movie/{movie_id}/watch/providers
Authorization: Bearer <token>
```

3. Fallback when direct TMDB id lookup fails and IMDb id exists:

```http
GET /3/find/{imdb_id}?external_source=imdb_id
Authorization: Bearer <token>
```

Only backend code calls TMDB. Android clients never call TMDB directly.

## Mongo Storage

TMDB metadata is stored directly on each `watchlist_items` movie document for this slice.

Add fields to `MongoWatchlistItemDocument`:

- `TmdbId`: nullable integer.
- `TmdbTitle`: nullable string.
- `OriginalTitle`: nullable string.
- `ReleaseDate`: nullable string in `yyyy-MM-dd` form.
- `Genres`: list of strings.
- `PosterPath`: nullable string.
- `BackdropPath`: nullable string.
- `PosterUrl`: existing display field, updated from TMDB image base URL.
- `BackdropUrl`: existing display field, updated from TMDB image base URL.
- `Overview`: existing display field, updated from TMDB.
- `WatchProviders`: normalized provider data for `PL` and `US`.
- `OwnedServiceAvailability`: normalized list for subscribed providers available in Poland.
- `ReleasedOnVod`: boolean.
- `VodRegions`: list containing `PL`, `US`, or both.
- `TmdbMetadataUpdatedAt`: nullable timestamp.
- `TmdbMetadataStatus`: string value `not_synced`, `completed`, `not_found`, or `failed`.
- `TmdbMetadataError`: nullable short error message for diagnostics.

Keep actual image bytes out of MongoDB for now. Later image-byte caching can use GridFS or a separate cache strategy.

## Provider Data

Store enough provider data to refine provider matching later without another immediate schema change.

For each region `PL` and `US`, store grouped providers:

- `flatrate`
- `rent`
- `buy`

Each provider entry stores:

- `ProviderId`
- `ProviderName`
- `LogoPath`
- `DisplayPriority`

This mirrors TMDB watch-provider categories while keeping the backend DTO independent from TMDB's raw JSON shape.

## Availability Rules

Owned services for Poland:

- Max / HBO Max
- SkyShowtime
- Crunchyroll
- Amazon Prime Video

Rules:

- Owned-service availability is based only on Poland `flatrate` providers.
- Amazon Prime Video counts as available only under `flatrate`.
- Amazon rent/buy entries do not count as owned-service availability.
- Rent/buy entries contribute only to VOD-release information.

Because TMDB provider names and ids can change by region and branding, the first implementation stores provider ids and names. Owned-service matching in this slice uses provider-name matching with a case-insensitive allowlist for Max/HBO Max, SkyShowtime, Crunchyroll, and Amazon Prime Video. Provider-id matching is a later refinement after inspecting real `PL` provider responses.

## VOD Release Rules

`ReleasedOnVod` is separate from "available to me."

Set `ReleasedOnVod = true` when either `PL` or `US` has at least one provider in any of:

- `flatrate`
- `rent`
- `buy`

Set `VodRegions` to the regions that have any of those provider categories.

VOD release does not change the existing `AvailabilityStatus` to available. It is separate badge/info for later UI.

## Backend API

### POST /api/sync/tmdb/movies

Enriches all existing Letterboxd movie records.

Response:

```json
{
  "status": "completed",
  "startedAt": "2026-06-04T12:00:00Z",
  "finishedAt": "2026-06-04T12:01:00Z",
  "itemsMatched": 276,
  "itemsEnriched": 270,
  "itemsNotFound": 4,
  "itemsFailed": 2
}
```

Per-movie failures do not fail the whole batch. They are recorded on the movie document and counted in the response.

### POST /api/sync/tmdb/movies/{id}

Enriches one existing movie by backend watchlist id, for example:

```http
POST /api/sync/tmdb/movies/movie-letterboxd-1297842
```

Response:

```json
{
  "status": "completed",
  "id": "movie-letterboxd-1297842",
  "tmdbId": 1297842
}
```

If the id does not exist or is not a Letterboxd movie, return `404`.

## API Read Model

The existing `GET /api/watchlist` response already includes:

- `posterUrl`
- `backdropUrl`
- `overview`

Those fields become populated after enrichment without requiring Android client contract changes.

Provider and VOD fields stay backend-only in this slice. The existing API DTO display fields (`posterUrl`, `backdropUrl`, `overview`) are enough for Android to show enriched cards. Exposing provider/VOD fields in the API is part of the later Android badge slice.

## Error Handling

TMDB dependency errors:

- Missing/invalid token: return `503 Service Unavailable` from TMDB sync endpoint with `{ "error": "TMDB is unavailable." }`.
- TMDB API unavailable: return `503 Service Unavailable` from single-item sync with `{ "error": "TMDB is unavailable." }`.
- Batch sync records per-item failures and continues where possible.
- Rate limiting stops the batch and returns a partial result with completed counts and `status` set to `partial`.

Letterboxd records remain usable when TMDB enrichment fails.

## Android Behavior

Android continues to call only backend endpoints.

Current Android TV grid should keep showing basic cards:

- title
- year when available
- `Artwork unavailable` placeholder when poster is missing

After TMDB enrichment, existing fields populate:

- poster image
- backdrop image
- overview/details where the UI already uses them

Provider badges, VOD badge, and richer details are a later Android slice.

## TV Sorting Rule

Movies are the current product focus.

In the later Android TV sorting follow-up, the `All` collection must render movies before TV shows. That UI follow-up is not part of the backend TMDB movie enrichment slice.

## Testing

Backend tests:

- TMDB client parses movie details and provider responses from deterministic fixtures.
- TMDB client maps unavailable, unauthorized, malformed, and not-found responses.
- Enrichment service updates movie metadata and leaves non-Letterboxd records alone.
- Direct TMDB id lookup is attempted before IMDb fallback.
- Provider rules distinguish `flatrate` from `rent`/`buy`.
- `ReleasedOnVod` becomes true for any PL/US flatrate/rent/buy provider.
- Single-movie endpoint returns `404` for missing or non-Letterboxd ids.
- Batch endpoint counts enriched, not-found, and failed items.

Smoke checks:

- Use local `TMDB__AccessToken`.
- Run `POST /api/sync/letterboxd`.
- Run `POST /api/sync/tmdb/movies`.
- Verify `GET /api/watchlist` returns real poster/overview data for at least one known imported movie.

## Later Work

- Android provider/VOD badges.
- Provider id verification for Max/HBO Max, SkyShowtime, Crunchyroll, and Prime Video in Poland.
- Store actual poster/backdrop image bytes in MongoDB/GridFS.
- Auto-sync orchestration for Letterboxd and TMDB.
- Plex availability matching using TMDB and IMDb ids.
- TMDB TV watchlist sync.
