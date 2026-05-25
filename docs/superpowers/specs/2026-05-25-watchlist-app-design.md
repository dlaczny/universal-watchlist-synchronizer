# Watchlist App Design

## Summary

Create a monorepo for a personal watchlist application. Version 1 targets Android TV and is read-only. It shows movies and TV shows the user wants to watch and marks which items are available on the user's Plex server.

## Product Scope

The Android TV app uses a Plex-like Featured Detail Row layout. Users can switch between Movies and TV Shows, and between All and Available. The focused item area shows artwork, metadata, summary, release state, and Plex availability.

Version 1 does not support editing the watchlist from the app.

## Sources Of Truth

- Movies watchlist: Letterboxd.
- TV watchlist: TMDB account watchlist.
- Metadata and artwork: TMDB.
- Availability: Plex server.

Letterboxd and TMDB define what the user wants to watch. Plex defines what is available now.

## Architecture

Use a monorepo:

- `backend/`: .NET service.
- `android/`: Android clients, with Android TV first.
- `docs/`: product, architecture, integration, API, and decision documentation.

The Android app calls only the backend. The backend owns all external integrations, credentials, sync jobs, caching, matching, and MongoDB persistence.

## Backend Design

The backend syncs Letterboxd, TMDB, and Plex into MongoDB. It exposes a read-only API for Android TV.

Initial endpoints:

- `GET /api/watchlist?mediaType=movie|tv&filter=all|available`
- `GET /api/watchlist/{id}`
- `GET /api/sync/status`

## Data Model

Initial MongoDB collections:

- `watchlist_items`: normalized movie/show records, source IDs, media type, metadata snapshot, release state, Plex availability, and sync timestamps.
- `plex_library_items`: latest Plex library inventory with Plex IDs, titles, years, GUIDs, and external IDs.
- `sync_runs`: sync history, status, errors, timestamps, and item counts.

Availability states:

- `available_on_plex`
- `not_on_plex`
- `unreleased`
- `unknown_match`

## Sync Flow

1. Fetch Letterboxd movie watchlist.
2. Fetch TMDB TV watchlist.
3. Enrich both with TMDB metadata.
4. Fetch Plex inventory.
5. Match watchlist items to Plex items by stable IDs first, then title/year fallback.
6. Store normalized data and sync status in MongoDB.
7. Serve the latest successful data to Android clients.

## Android TV Design

The first UI direction is Featured Detail Row. It prioritizes remote-friendly navigation and a focused item preview. The app is read-only and should provide clear loading, empty, stale-data, and backend-offline states.

## Risks And Open Decisions

- Letterboxd API access may require approval. The fallback must be chosen explicitly before implementation.
- Plex matching quality depends on available external IDs.
- Exact backend DTOs need to be specified before Android implementation.
- Android technology stack is not selected yet.

## Initial Repository Files

The repository setup includes:

- `AGENTS.md`
- `.gitignore`
- `docs/product-brief.md`
- `docs/architecture.md`
- `docs/integrations.md`
- `docs/superpowers/specs/2026-05-25-watchlist-app-design.md`
