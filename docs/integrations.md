# Integrations

## Letterboxd

Purpose: source of truth for movies the user wants to watch.

The preferred integration is the official Letterboxd API if access is available. If API access is unavailable or limited, the project should document and choose a fallback before implementation. Do not build fragile scraping as a hidden assumption.

Needed data:

- Movie title.
- Release year.
- Letterboxd identifier or URL.
- External IDs if available.

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

Initial collections:

- `watchlist_items`: normalized movie and TV records with metadata and availability.
- `plex_library_items`: latest Plex inventory snapshot.
- `sync_runs`: sync status, errors, timestamps, and counts.

MongoDB is not a client-facing dependency. Android clients read through the backend API only.

## Later Extension: Streaming Services

Streaming-provider availability is not part of version 1. It can be added later using TMDB watch provider data and a user-configured list of subscribed services.
