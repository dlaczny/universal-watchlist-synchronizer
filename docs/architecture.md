# Architecture

## Overview

The project is a monorepo with a .NET backend, Android clients, and documentation. The backend is the integration and data owner. Android clients are read-only consumers of backend APIs.

```text
Letterboxd      TMDB Account/API      Plex Server
    \                 |                  /
     \                |                 /
      +---------- .NET Backend --------+
                    |
                  MongoDB
                    |
              Read-only API
                    |
             Android TV Client
```

## Repository Structure

- `backend/`: .NET service for sync, persistence, matching, and API.
- `android/`: Android TV app first, Android phone later.
- `docs/`: Product, architecture, integration, API, and decision documentation.

## Backend Responsibilities

- Read movie watchlist from Letterboxd.
- Read TV watchlist from TMDB.
- Enrich movies and shows with TMDB metadata.
- Read Plex library inventory.
- Match watchlist items to Plex library items.
- Store normalized data and sync history in MongoDB.
- Expose read-only endpoints for clients.

## Android TV Responsibilities

- Present a Plex-like Featured Detail Row browsing UI.
- Support Movies vs TV Shows and All vs Available.
- Render metadata, artwork, availability, and sync/error states from backend data.
- Avoid storing integration credentials.
- Avoid direct calls to external services.

## Data Ownership

Letterboxd and TMDB own the wanted list. Plex owns current availability. MongoDB stores the backend's normalized view of both so Android browsing is fast and stable.

## Current Persistence Slice

The implemented backend now reads `watchlist_items` and `sync_runs` from MongoDB. During startup, a background bootstrap service inserts deterministic sample records only when those collections are empty. This keeps local development usable until live Letterboxd, TMDB, and Plex sync jobs are implemented.

MongoDB outages are exposed as `503 Service Unavailable` API responses. The backend does not silently switch to process-local data.

## Availability States

The backend should represent availability explicitly:

- `available_on_plex`: confident Plex match exists.
- `not_on_plex`: released item has no Plex match.
- `unreleased`: item is not released yet.
- `unknown_match`: matching data is incomplete or ambiguous.

## Sync Pipeline

1. Fetch Letterboxd movie watchlist.
2. Fetch TMDB TV watchlist.
3. Enrich all items with TMDB metadata.
4. Fetch Plex library inventory.
5. Match watchlist items to Plex items by stable IDs first, then title/year fallback.
6. Persist normalized records and sync run details in MongoDB.
7. Serve the latest successful read model to Android clients.

## API Surface

The backend exposes these endpoints:

- `GET /api/watchlist?collection=all|movie|tv&availability=plex,not_on_plex,unreleased,unknown_match&sort=added_desc|title_asc`
- `GET /api/watchlist/{id}`
- `GET /api/images/tmdb/{size}/{fileName}`
- `GET /api/sync/status`
- `POST /api/sync/letterboxd` — manual Letterboxd movie watchlist sync.
- `POST /api/sync/tmdb/movies` — batch TMDB enrichment for all Letterboxd movies.
- `POST /api/sync/tmdb/movies/{id}` — single TMDB enrichment by backend item ID.
- `POST /api/sync/plex/movies` — manual Plex movie inventory sync and availability update.
- `POST /api/sync/all` — combined Letterboxd → TMDB → Plex sync in order.

The list endpoint is backend-owned: clients send collection, availability, and sort controls instead of duplicating integration-aware filtering. Artwork is also backend-owned; clients consume backend image URLs instead of calling TMDB directly. Plex inventory is cached in MongoDB for matching.

## API Contract

The implemented backend API contract is documented in [api.md](api.md).
