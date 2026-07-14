---
type: System
title: Backend Service
description: .NET service that owns source ingestion, metadata, Plex inventory, MongoDB persistence, HTTP contracts, and the complete movie snapshot.
tags:
  - backend
  - dotnet
  - mongodb
  - api
timestamp: 2026-07-14T00:00:00Z
version: 0.4.0
---

# Structure

The backend under `backend/` targets .NET 10.

| Project | Responsibility |
|---|---|
| `Watchlist.Domain` | Core media, source, availability, and watchlist records. |
| `Watchlist.Application` | Queries, DTOs, sync orchestration, matching, and ports. |
| `Watchlist.Infrastructure` | MongoDB repositories, external clients, retries, and options. |
| `Watchlist.Api` | Minimal API routes, auth filter, image proxies, errors, and dependency wiring. |

# Movie Services

| Service | Role |
|---|---|
| `LetterboxdMovieSyncService` | Validates a non-empty source, computes lifecycle transitions, and publishes a source snapshot. |
| `TmdbMovieEnrichmentService` | Resolves movie identity, metadata, and provider availability. |
| `PlexMovieSyncService` | Stores Plex movie inventory and updates availability matches. |
| `MovieSyncService` | Runs the three movie stages in order and returns completed or partial status. |
| `WatchlistExportService` | Produces one coherent active/watched worker snapshot and compatibility exports. |
| `PlexMovieMatcher` | Matches IMDb, then TMDB, then unique normalized title/year. |

`CombinedSyncService` and TMDB TV synchronization remain available but are not
used by the production movie worker.

# Trakt Connection

The backend owns the singleton Trakt device-authorization state, encrypts its
device and OAuth credentials with the persistent ASP.NET Data Protection key
ring, and advances pending authorization from a hosted poller. Successful
credential responses are saved with a bounded token independent of request or
host cancellation. Malformed successful device grants become non-pollable;
malformed successful refresh grants become `refresh_required` so a consumed or
rotated credential is not retried indefinitely.

# Persistence

- `watchlist_items`: normalized movie and TV records.
- `plex_library_items`: latest Plex movie inventory.
- `sync_runs`: source and integration status used for freshness.
- `letterboxd_source_snapshots`: immutable active and watched lifecycle
  manifests; written last and read once per worker export.
- `trakt_connections`: one protected device/OAuth connection state document.

`watchlist_items` retains watched Letterboxd movie documents and their event
history. Active-only repository filters use the latest manifest for browse,
TMDB enrichment, Plex matching, and compatibility export. If no manifest exists
during migration, existing Letterboxd documents are treated as the active
baseline. Before its first document update, the lifecycle writer publishes a
bootstrap manifest for that baseline with no watched authorizations. A failed
first write therefore cannot expose partially updated documents as current
source authority.

MongoDB failures are not replaced with process-local fallback data. Seed data
is inserted only when configured collections are empty.

# Configuration

| Section | Required production content |
|---|---|
| `Sync:ApiKey` | Shared key for all sync mutations. Required at Production startup. |
| `MongoDb` | Connection string, database, and collection names including `LetterboxdSourceSnapshotsCollectionName`. |
| `Letterboxd` | Watchlist proxy URL. |
| `Tmdb` | Access token, base URL, image base URL; TV account fields are optional for movie-only operation. |
| `Plex` | Base URL and token. |
| `Trakt` | Client ID, client secret, API base URL, redirect URI, token refresh skew, and device-poll settings. |
| `DataProtection` | Persistent key-ring path and application name used to decrypt Trakt state after restart. |

ASP.NET environment overrides use double underscores, for example
`Sync__ApiKey` and `MongoDb__ConnectionString`. Local secrets belong in the
ignored `appsettings.Development.Local.json`; host secrets belong in
`/opt/watchlist-prod/config/backend.env`.

# Container Contract

`backend/src/Watchlist.Api/Dockerfile` publishes a non-root `app` image with a
`curl` healthcheck against `/healthz`. The root filesystem is read-only in
production Compose, with only a bounded `/tmp` tmpfs.

# Links

- [Backend API](../apis/backend_api.md)
- [Export Endpoints](../apis/export_endpoints.md)
- [MongoDB](../integrations/mongodb.md)
- [Validation](../runbooks/validation.md)
