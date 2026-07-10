---
type: System
title: Backend Service
description: .NET backend that owns integrations, sync orchestration, MongoDB persistence, Plex matching, and read-only API endpoints.
tags:
  - backend
  - dotnet
  - mongodb
  - api
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

The backend lives under `backend/`. It is an ASP.NET Core service targeting
`.NET 10` and organized into Domain, Application, Infrastructure, and API
projects.

# Projects

| Project | Responsibility |
|---|---|
| `Watchlist.Domain` | Core enums and `WatchlistItem` domain record. |
| `Watchlist.Application` | Query services, sync services, DTOs, matching logic, interfaces. |
| `Watchlist.Infrastructure` | MongoDB repositories, integration clients, retry helpers, options, seed data. |
| `Watchlist.Api` | Minimal API endpoints, image proxies, exception handling, dependency wiring. |

# Key Application Services

| Service | Role |
|---|---|
| `WatchlistQueryService` | Filters, sorts, and maps watchlist items for browse and detail APIs. Also adds Plex-only movies when Plex availability is selected. |
| `WatchlistExportService` | Builds worker export lists from cached data. |
| `LetterboxdMovieSyncService` | Imports Letterboxd movie watchlist entries. |
| `TmdbMovieEnrichmentService` | Enriches Letterboxd movies with TMDB details and provider data. |
| `TmdbTvWatchlistSyncService` | Imports and enriches TMDB account TV watchlist entries. |
| `PlexMovieSyncService` | Imports Plex movie inventory and applies Plex match updates. |
| `PlexMovieMatcher` | Matches by IMDb ID, TMDB ID, then unique normalized title/year fallback. |
| `AvailabilityRefreshService` | Runs stale-aware Plex availability refresh for Android startup. |
| `CombinedSyncService` | Runs the sync chain in order and returns combined or partial status. |

# Infrastructure

- `MongoWatchlistReadRepository`, `MongoWatchlistWriteRepository`, and
  `MongoWatchlistExportRepository` own `watchlist_items`.
- `MongoPlexMovieInventoryRepository` owns `plex_library_items`.
- `MongoSyncStatusReadRepository` reads `sync_runs`.
- `LetterboxdWatchlistClient`, `TmdbMovieClient`, `TmdbTvWatchlistClient`,
  `TmdbTvMetadataClient`, and `PlexLibraryClient` wrap external HTTP calls.
- `HttpRetryPolicy`, `DefaultHttpRetryDelay`, and `IHttpRetryDelay` provide
  retry/backoff behavior for supported integration clients.
- `MongoBootstrapHostedService` inserts deterministic sample data only when
  MongoDB collections are empty.

# Configuration

| Section | Purpose |
|---|---|
| `MongoDb` | Connection string, database, and collection names. |
| `Letterboxd` | Watchlist proxy URL. |
| `Tmdb` | TMDB access token, base URL, image base URL, account ID, session ID, language. |
| `Plex` | Plex base URL and token. |

Local secrets can be stored in
`backend/src/Watchlist.Api/appsettings.Development.Local.json`; that file is
ignored by git.

# Error Handling

- MongoDB outages surface as `503 Service Unavailable`.
- Missing or unavailable Plex/TMDB/Letterboxd dependencies surface through the
  sync endpoint contracts.
- Backend endpoints do not silently switch to process-local data when MongoDB is
  unavailable.

# Links

- API: [Backend API](../apis/backend_api.md)
- Data model: [Watchlist Item](../data_models/watchlist_item.md)
- MongoDB: [MongoDB Integration](../integrations/mongodb.md)
- Validation: [Validation](../runbooks/validation.md)

