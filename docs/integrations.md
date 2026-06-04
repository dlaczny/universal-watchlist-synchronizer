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

## TMDB

Purpose: source of truth for TV watchlist and metadata provider for movies and TV.

Needed data:

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

Needed data:

- Library inventory.
- Media type.
- Title.
- Year.
- Plex rating key.
- GUIDs and external IDs where available.

Matching should prefer stable IDs. Title/year fallback should produce an explicit confidence level or `unknown_match` when ambiguous.

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

Streaming-provider availability is not part of version 1. It can be added later using TMDB watch provider data and a user-configured list of subscribed services.
