---
type: Architecture
title: TV Sync Read Model
description: Trakt-backed TV generations, per-season Polish provider observations, and the schema-v2 reversible destination contract.
tags:
  - tv
  - trakt
  - mongodb
  - read-model
timestamp: 2026-07-31T00:00:00Z
version: 1.2.0
---

# Source And Publication Boundary

The backend-owned TV read model remains non-destructive. Trakt supplies TV
watchlist membership, watched progress, episode schedules, and show status;
TMDB supplies exact-ID metadata and Poland (`PL`) provider observations for
the show and each regular season. MongoDB stores the protected Trakt connection,
bounded immutable TV generations, and a pointer to the current published
generation. Clients and the worker read that published generation only.

The legacy `mutationCapable=false` field remains false for compatibility; it is
not a destination permission or a cleanup permission. Schema version 2 adds a
separate `destinationSync` envelope for the reversible TV worker. Its `capable`
value is true only for a valid, published generation with no manifest validation
failures. Stable envelope blockers are `tv_generation_not_valid`,
`tv_generation_unpublished`, and `tv_generation_validation_failed`.

Plex episode history, Trakt history writes, Plex library mutation, and Sonarr
file/season/series removal remain prohibited. Internal TV generation retention
is a storage-lifecycle boundary only: it grants no destination, history, or
cleanup permission and changes none of those switches. Destination collection
is disabled by default; a real destination write additionally needs both the
host apply gate and a per-run `--apply` request.

# Publication Flow

```text
current published generation validation
  -> mandatory generation retention
  -> Trakt watchlist + watched progress + prior generation
  -> detailed schedules and exact-ID TMDB enrichment
  -> lifecycle reduction and validation
  -> staged MongoDB generation
  -> immutable manifest published last
  -> best-effort generation retention
  -> browse/detail/status/export readers
```

The TV coordinator lease covers both retention passes and publication. The
mandatory pass runs after the repository has loaded and validated the current
published generation, but before token acquisition, Trakt or TMDB source
collection, or staging. Failure therefore publishes and stages nothing. After
durable publication, a best-effort pass reclaims the generation that may have
just crossed a bound; its failure or cancellation is deferred and cannot turn
the successful publication into a failed result. The next sync retries
retention in its mandatory pre-stage pass.

The source catalog is the union of current Trakt watchlist membership, watched
progress, and rows retained by the previous generation. A source, identity,
schedule, pagination, or activity-cursor failure stages and publishes nothing;
the previous published pointer remains readable. The Trakt activity cursor is
read before and after a full collection while a single per-account coordinator
lease is held. A change during collection rejects the candidate rather than
publishing a mixed snapshot.

`TvSyncHostedService` polls activity every five minutes. It performs a full
generation when no generation exists, when the six-hour full-sync interval is
due, or when the activity cursor changed; it does not synthesize generations
while the connection is disconnected, revoked, or requires refresh.

# Generation Retention

The current published generation is always protected, even if it is older than
the age bound or would otherwise exceed the count bound. Noncurrent
`scheduled_full` and `activity_full` generations share one inclusive seven-day
age window and one limit of 48 total retained generations, including current.
Within that window, retention is deterministic by newest `publishedAt` and
then generation ID. A noncurrent manifest at exactly the seven-day boundary is
retained when capacity remains.

For an expired manifested generation, deletion is acknowledged and child-first:
`tv_shows` generation rows, then `tv_lifecycle_events`, then its
`tv_sync_manifests` manifest. The published pointer is re-read before any
delete, and a changed pointer, an overlapping retention plan, or an
unacknowledged delete fails closed. Only orphan generation IDs matching the
exact production shape `^tv-[0-9]{17}-[0-9a-f]{32}$` can be reclaimed, and only
after the inclusive 24-hour grace boundary. Orphan show rows and lifecycle
events are deleted; there is no orphan manifest to remove.

Legacy `tv_shows` rows and malformed or uncertain physical generation
identities are never auto-deleted. A malformed manifest or published pointer
fails the mandatory pass closed before deletion. These safeguards keep internal
storage retention separate from every destination, history, and content-cleanup
permission.

# Lifecycle, Identity, And Availability

The persistent lifecycle states are `active`, `caught_up`, `source_removed`,
`terminal_cleanup_pending`, and `retired_terminal`. In Phase 1 only the first
three may be published. Source removal requires two scheduled complete
confirmations; activity-triggered generations do not advance that confirmation.
`reactivated` is an immutable event, not a stored state.

TMDB observations are regional and use stable provider IDs. A successful PL
response becomes `available` or `confirmed_unavailable`. An upstream provider
failure is never represented as unavailable: it publishes `stale` where a
previous observation can be retained, otherwise `unknown`.

TVDB is the canonical Sonarr identity. A missing, nonpositive, or conflicting
TVDB ID is a fail-closed per-show blocker (`identity_missing_tvdb` or
`identity_conflict`); title/year matching may not authorize a Sonarr or Plex
Watchlist action. Plex may use an exact TMDB or IMDb GUID only after the
backend-verified identity maps it to the same TVDB show.

# Reversible Destination Semantics

The backend publishes desired state; the worker independently collects live
Sonarr, Plex Watchlist, Plex TV-library, and SQLite ownership state before it
plans or applies anything. The worker considers regular numbered seasons only:

- An unstarted show selects Season 1.
- Otherwise it selects Trakt's `nextEpisode` season, or the next numbered
  season after the latest fully watched season if that season already has an
  aired episode.
- If no selected season exists, an existing Sonarr series is retained as-is;
  no unknown future season is added or searched.

The selected season's own PL observation is authoritative. Only a selected
season confirmed unavailable permits Sonarr add, selected-season monitor, and
aired/unwatched episode search. `unknown` and `stale` are not proof of
unavailability and block a new Sonarr action. A selected season confirmed
available, or at least one exactly identified episode in the configured Plex
TV library, makes its show desired on Plex Watchlist. When neither fact is
true, the worker can remove only an exactly identified Plex Watchlist row.

An existing exact-TVDB Sonarr row is an adoption candidate, not automatically
worker-owned. After a report review, a separately armed adoption run records
`manual` origin without changing monitoring. Worker-created rows have
`worker` origin. Neither origin authorizes Sonarr removal in this release.

# Reader And Worker Contract

`GET /api/watchlist` and detail reads are served from the one published TV
generation. `collection=all` includes movies plus active TV rows only;
`collection=tv` defaults to active and accepts the TV lifecycle filters
documented in [Backend API](../apis/backend_api.md).

`GET /api/export/tv/sync-state` is schema version 2 and is still read-only: it
is an immutable input to a worker plan, never a Sonarr command stream. The
worker rejects an incapable, stale, malformed, duplicate, or credential-shaped
payload and reports without destination writes. It records each successful
reversible action immediately, then re-collects live state on a later run
rather than replaying an old plan.

# Links

- [Trakt Integration](../integrations/trakt.md)
- [TV Show](../data_models/tv_show.md)
- [TV Sync Operations](../runbooks/tv_sync_operations.md)
- [TV Integration Rollout](../reports/tv_integration_rollout.md)
- [Approved TV Design](../superpowers/specs/2026-07-13-tv-show-integration-design.md)
