---
type: Architecture
title: Sync Pipeline
description: Implemented movie flow from source ingestion through a guarded Radarr and Plex-watchlist plan.
tags:
  - sync
  - backend
  - worker
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
---

# Production Movie Flow

1. The worker may call authenticated `POST /api/sync/movies`.
2. The backend rejects empty, malformed, or duplicate Letterboxd snapshots
   before writes. A valid import publishes one immutable source manifest last.
3. TMDB enrichment and Plex inventory matching operate only on active IDs from
   the latest manifest.
4. The worker reads `GET /api/export/movies/sync-state`. Its
   `sourceSnapshotId` must equal the ID returned by the refresh. `movies` is the
   complete active set and `watchedMovies` is the complete published cleanup
   authorization set.
5. It independently collects live Radarr movies, exclusions, Plex watchlist,
   Plex library, SQLite ownership, and durable Radarr observations. A failed
   Radarr read never advances observations; the first successful read is a
   baseline only.
6. The pure planner emits ordinary desired-state decisions, exact watched
   cleanup decisions, and manual-Radarr-removal Plex cleanup decisions.
7. Policy blocks stale/failed/ambiguous plans, suspicious empty sources,
   disabled apply, excessive removals, malformed file-delete authorization,
   and watched file deletion while its dedicated gate is off.
8. The executor validates destructive authorization again, applies Radarr and
   Plex-watchlist actions independently, and never calls a Plex-library
   mutation.
9. JSON/Markdown reports, cleanup attempts, Radarr observations, run history,
   ownership, and the worker heartbeat persist under the worker data directory.

# Destination Behavior

- Radarr receives active backend rows marked `radarrEligible`. Ordinary
  worker-owned cleanup still preserves downloaded files and always uses
  `delete_files=false`.
- A published watched event authorizes removal of its exact TMDB match from
  Radarr with `delete_files=true`, even when the row predates worker ownership.
- Plex watchlist desired state is the union of source movies available on an
  owned service, completed Radarr downloads, and Plex library movies.
- The exact watched identity is removed from the Plex watchlist despite
  ownership or library membership. A durable manual Radarr disappearance
  authorizes only the matching Plex-watchlist removal.
- Plex library state is read-only. It contributes availability and desired
  watchlist state but is never mutated.
- Existing desired destination rows are adopted into worker ownership. Existing
  unrelated rows remain unmanaged and are preserved.

# Other Paths

`POST /api/sync/all` and the direct-source worker scripts remain compatibility
or development paths. They are not invoked by the production container. The
separate Phase 1 TV pipeline reads Trakt and TMDB, validates a complete
generation, and publishes it last; it does not call Plex, Sonarr, or a worker
apply path. Android TV work is deferred until explicitly requested.

# Links

- [Production Movie Sync](movie_sync_production.md)
- [Backend API](../apis/backend_api.md)
- [TV Sync Read Model](tv_sync_read_model.md)
- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
