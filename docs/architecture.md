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

## Initial API Surface

The first Android TV API can stay small:

- `GET /api/watchlist?mediaType=movie|tv&filter=all|available`
- `GET /api/watchlist/{id}`
- `GET /api/sync/status`

The exact DTOs should be documented before implementation.
