# Integrations

## Letterboxd

Purpose: source of truth for movies the user wants to watch.

The backend imports movies from:

`https://letterboxd-list-radarr.onrender.com/example-user/watchlist`

The URL is configured with `Letterboxd:WatchlistUrl` and can be overridden with `Letterboxd__WatchlistUrl`.

The proxy returns Radarr-style JSON with `id`, `imdb_id`, `title`, `release_year`, `clean_title`, and `adult`.

Imported source trace fields:

- `id` maps to the backend `sourceId`.
- `imdb_id` is stored on the MongoDB document for later TMDB/Plex matching.
- `clean_title` is stored on the MongoDB document as the Letterboxd path.

## Export Endpoints

Purpose: provide cached watchlist lists to external import tools without making those tools call Letterboxd, TMDB, Plex, or MongoDB directly.

Implemented endpoints:

- `GET /api/export/radarr/movies`: returns Radarr/Letterboxd-style movie JSON for Letterboxd watchlist movies that are not already available on subscribed VOD services.
- `GET /api/export/sonarr/tv`: returns an empty array in v1 and reserves the TV export contract for later TMDB TV watchlist work.

The movie export endpoint uses cached MongoDB fields only. It excludes a movie when TMDB enrichment has stored at least one `OwnedServiceAvailability` value. If TMDB enrichment has not run or provider data is missing, the movie remains in the export because the backend has no cached evidence that the movie is available on a subscribed VOD service.

Plex availability does not filter the Radarr export endpoint.

## TMDB

Purpose: source of truth for TV watchlist and metadata provider for movies and TV.

Configuration:

- `Tmdb:AccessToken` / `TMDB__AccessToken`: TMDB v4 read token. Keep this out of committed files.
- `Tmdb:BaseUrl`: defaults to `https://api.themoviedb.org/3`.
- `Tmdb:ImageBaseUrl`: defaults to `https://image.tmdb.org/t/p`.

The backend starts without an access token so local read-only browsing still works. TMDB sync calls return a TMDB dependency error when the token is missing or invalid.

For local development without setting an environment variable every time, create:

`backend/src/Watchlist.Api/appsettings.Development.Local.json`

Use this shape:

```json
{
  "Tmdb": {
    "AccessToken": "put-local-token-here"
  }
}
```

The repository ignores `appsettings.*.Local.json`, so the real token file stays local. A placeholder example is committed as `appsettings.Development.Local.example.json`.

Implemented movie enrichment:

- For Letterboxd movies, use the Letterboxd proxy `id` as the first candidate TMDB movie ID.
- If direct `/movie/{id}` lookup returns missing, fallback through `/find/{imdbId}?external_source=imdb_id`.
- Fetch `/movie/{id}` for title, original title, IMDb ID, overview, release date, genres, poster path, and backdrop path.
- Build poster URL with `ImageBaseUrl + /w500 + poster_path`.
- Build backdrop URL with `ImageBaseUrl + /w1280 + backdrop_path`.
- Store TMDB image URLs in MongoDB, then expose them to Android through backend-relative `/api/images/tmdb/{size}/{fileName}` proxy URLs.
- Fetch `/movie/{id}/watch/providers` and store provider groups for each returned region.
- Store provider details in MongoDB under `WatchProviders` with `flatrate`, `rent`, and `buy` groups.
- Store metadata status on the movie document as `enriched`, `not_found`, or `failed`.
- Failure and not-found updates are status-only so a temporary TMDB problem does not erase previously enriched metadata.

Provider and VOD rules:

- Owned subscribed-service availability currently uses Poland (`PL`) flatrate providers only.
- Owned service names are matched case-insensitively against Max/HBO Max, SkyShowtime, Crunchyroll, Amazon Prime Video, and Prime Video.
- Rent and buy providers do not count as subscribed-service availability.
- `releasedOnVod` is true when Poland or the US has at least one flatrate, rent, or buy provider.
- `vodRegions` stores the matching regions, currently `PL` and/or `US`.

Still needed for TV watchlist:

- TMDB movie and TV IDs.
- Titles and original titles.
- Posters and backdrops.
- Overview.
- Genres.
- Release or first air dates.
- Release status.
- External IDs for matching when available.

The backend should cache TMDB metadata in MongoDB and avoid repeated live calls during normal Android browsing.

## Plex

Purpose: source of truth for availability on the user's Plex server.

Implemented Plex movie sync:

- Configuration: `Plex:BaseUrl` (e.g. `http://127.0.0.1:32400`) and `Plex:Token`.
- Discovers movie libraries through `/library/sections`, filtering by type `movie`.
- Scans every Plex movie section by fetching `/library/sections/{key}/all?type=1`.
- Reads nested `Guid` IDs from per-movie metadata (`/library/metadata/{ratingKey}`) for IMDb, TMDB, and TVDB references.
- Stores normalized movie inventory in MongoDB collection `plex_library_items` with sync timestamps.
- Matches watchlist movies by:
  1. IMDb ID match — highest confidence.
  2. TMDB ID match — requires TMDB enrichment to have run first.
  3. Normalized title + year match — punctuation-stripped, case-insensitive; produced only when exactly one Plex movie matches (otherwise `unknown_match`).
- Updates watchlist availability to `available_on_plex`, `not_on_plex`, `unreleased`, or `unknown_match`.
- Manual triggers: `POST /api/sync/plex/movies` and `POST /api/sync/all`.
- Dependency errors: `503` when Plex is unreachable or token is missing, `502` when XML is malformed.

## MongoDB

Purpose: persistent normalized read model and sync history.

For local development, start MongoDB from the repository root:

```powershell
docker compose up -d mongo
```

`compose.yaml` publishes MongoDB on `localhost:27017` and stores data in a named Docker volume. The backend bootstraps `watchlist_items` and `sync_runs` only when each collection is empty.

Initial collections:

- `watchlist_items`: normalized movie and TV records with metadata and availability.
- `plex_library_items`: latest Plex inventory snapshot.
- `sync_runs`: sync status, errors, timestamps, and counts.

MongoDB is not a client-facing dependency. Android clients read through the backend API only.

If MongoDB is unavailable, the backend returns `503 Service Unavailable` rather than falling back to seeded in-memory data.

## Later Extension: Streaming Services

TMDB watch-provider data is cached for movies. Android consumes `ownedServiceAvailability`, `vodReleaseKnown`, and `releasedOnVod` for card badges. Provider-ID refinement remains later work.
