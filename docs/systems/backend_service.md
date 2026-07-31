---
type: System
title: Backend Service
description: .NET service that owns movie sync plus protected Trakt TV generations, metadata, MongoDB persistence, HTTP contracts, and exports.
tags:
  - backend
  - dotnet
  - mongodb
  - api
timestamp: 2026-07-31T00:00:00Z
version: 0.5.0
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

`CombinedSyncService` runs the established movie stages and then one scheduled
full TV generation when TV is wired. The former TMDB TV watchlist route is
retired (`410 Gone`). `TvSyncService` owns complete Trakt TV generations and
`TvSyncHostedService` polls activity every five minutes, with scheduled full
generations due every six hours; neither adds a destination mutation path.

# Trakt Connection

The backend owns the singleton Trakt device-authorization state, encrypts its
device and OAuth credentials with the persistent ASP.NET Data Protection key
ring, and advances pending authorization from a hosted poller. Successful
credential responses are saved with a bounded token independent of request or
host cancellation. Malformed successful device grants become non-pollable;
malformed successful refresh grants become `refresh_required` so a consumed or
rotated credential is not retried indefinitely.

`TvSyncService` holds one per-account coordinator lease from the current
published-generation read through both retention passes and publication. After
the current generation is loaded and validated, required retention runs before
token acquisition, Trakt/TMDB source collection, or staging. It then reads the
complete Trakt watchlist/progress union, prior retained rows, detailed
schedules, and exact-ID TMDB enrichment. It stages every candidate and advances
the Mongo pointer only after validation. Source, pagination, schedule, identity,
or cursor-race failure leaves the old published generation intact. TMDB
provider failure produces `unknown` or `stale`, not unavailable.

After durable publication, retention runs again as best effort. A failure or
cancellation in that pass is logged as `tv_generation_retention_deferred`,
does not change the successful sync response, and is retried by the required
pass before the next staging operation. Required failures use
`tv_generation_retention_failed`; API and hosted-service mappings keep that
stable code, and retention logs include only stable codes, counts, modes, and
exception types rather than inner messages or credentials.

# Persistence

- `watchlist_items`: normalized movie records plus legacy-TV migration input;
  published TV reads do not use legacy rows.
- `plex_library_items`: latest Plex movie inventory.
- `sync_runs`: source and integration status used for freshness.
- `letterboxd_source_snapshots`: immutable active and watched lifecycle
  manifests; written last and read once per worker export.
- `trakt_connections`: one protected device/OAuth connection state document.
- `tv_shows`, `tv_sync_manifests`, `tv_lifecycle_events`: immutable Phase 1 TV
  generations, publish pointer, and lifecycle history. Retention affects only
  noncurrent generation documents in these three collections: generation show
  rows, lifecycle-event children, and generation manifests.

TV generation retention is bounded independently of movie persistence. It does
not delete from `watchlist_items`, `letterboxd_source_snapshots`, `sync_runs`,
index definitions, protected connection state, or any other collection.
Within `tv_shows`, legacy rows are preserved. Malformed or uncertain physical
generation identities are preserved, and a malformed manifest or published
pointer fails closed. The current published generation is always protected.
For expired generations, acknowledged deletes run show rows first, lifecycle
events second, and manifests last.

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
| `Sync:ApiKey` | Shared key for every sync mutation and every `/api/integrations/*` route. Required at Production startup. |
| `MongoDb` | Connection string, database, and collection names including `LetterboxdSourceSnapshotsCollectionName`. |
| `Letterboxd` | Watchlist proxy URL. |
| `Tmdb` | Access token, base/image URLs, PL provider region, and stable owned provider IDs. |
| `Plex` | Base URL and token. |
| `Trakt` | Client ID, client secret, API base URL, redirect URI, token refresh skew, five-minute activity polling, and a six-hour scheduled full-sync interval. |
| `TvGenerationRetention` | Seven-day inclusive maximum age, 48 total retained generations including current, and a 24-hour orphan grace period. |
| `DataProtection` | Persistent key-ring path and application name used to decrypt Trakt state after restart. |

ASP.NET environment overrides use double underscores, for example
`Sync__ApiKey` and `MongoDb__ConnectionString`. Local secrets belong in the
ignored `appsettings.Development.Local.json`; host secrets belong in
`/opt/watchlist-prod/config/backend.env`.

The production key-ring is mounted at `/var/lib/watchlist/keyring` from the
private backend data directory. It must persist across container restarts; a
new empty directory makes existing Trakt ciphertext unreadable.

# Container Contract

`backend/src/Watchlist.Api/Dockerfile` publishes a non-root `app` image with a
`curl` healthcheck against `/healthz`. The root filesystem is read-only in
production Compose, with only a bounded `/tmp` tmpfs.

# Links

- [Backend API](../apis/backend_api.md)
- [Export Endpoints](../apis/export_endpoints.md)
- [MongoDB](../integrations/mongodb.md)
- [Validation](../runbooks/validation.md)
- [TV Sync Operations](../runbooks/tv_sync_operations.md)
