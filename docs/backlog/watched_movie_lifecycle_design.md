---
type: Backlog
title: Watched Movie Lifecycle Design
description: Approved design for recording Letterboxd removals as watched movies and cleaning Radarr files and Plex watchlist state.
tags:
  - movies
  - letterboxd
  - radarr
  - plex
  - lifecycle
timestamp: 2026-07-12T00:00:00Z
version: 0.1.0
---

# Status

This design is approved for implementation. It intentionally changes the
current production rule that downloaded Radarr files are never deleted. The
implemented behavior and standing OKF rules must not change until the code,
tests, rollout gates, and supervised production validation are complete.

# Goal

Treat disappearance from a successful Letterboxd watchlist snapshot as an
explicit watched event. Preserve that event in MongoDB, then remove the exact
movie from Radarr with its files and from the Plex watchlist.

Also observe Radarr movies that never belonged to the Letterboxd lifecycle. The
service must not delete those movies from Radarr. If the operator later removes
one manually from Radarr, the worker removes its exact identity from the Plex
watchlist while leaving the Plex library untouched.

# Confirmed Rules

- One successful Letterboxd snapshot is enough to confirm a watched
  transition.
- A successful Letterboxd snapshot must contain at least one valid movie. A
  zero-item result is a failed source sync and causes no persistence or
  destination mutation.
- A movie must previously have been present in Letterboxd and persisted by this
  application before its disappearance can authorize Radarr file deletion.
- A confirmed watched event authorizes cleanup of any exact-TMDB Radarr and
  Plex-watchlist match, including a destination row that predates worker
  ownership.
- A movie that never belonged to the Letterboxd lifecycle is never removed from
  Radarr by this service.
- When such a movie is removed manually from Radarr, its exact-TMDB Plex
  watchlist row is removed even if the movie still appears in the Plex library.
- Plex library media is never mutated directly.
- Re-adding a watched movie to Letterboxd reactivates normal Radarr and Plex
  desired-state behavior while retaining lifecycle history.

# Chosen Approach

Keep lifecycle state and history on the existing MongoDB watchlist document.
This keeps identity, metadata, active state, and watched history together and
avoids cross-collection move failures.

The worker keeps operational Radarr observations and cleanup outcomes in its
existing SQLite database. MongoDB remains the authority for Letterboxd
lifecycle transitions; SQLite remains the authority for worker-local live-state
observations and execution audit.

Rejected alternatives are:

- moving removed movies to a separate MongoDB collection, which adds
  cross-collection consistency and reactivation complexity;
- inferring watched state only in SQLite, which would lose the required durable
  backend history and make cleanup authorization dependent on one host-local
  database.

# MongoDB Lifecycle Model

Letterboxd movie documents gain these logical fields:

| Field | Meaning |
|---|---|
| `sourceLifecycleStatus` | Current derived `active` or `watched` state. |
| `lastSeenInSourceAt` | Last successful Letterboxd snapshot containing the movie. |
| `lastWatchedAt` | Most recent confirmed transition to `watched`. |
| `lifecycleVersion` | Monotonically increasing transition version. |
| `lifecycleEvents` | Ordered `added`, `watched`, and `reactivated` event history, tagged with its source snapshot ID. |

Each event contains a stable event ID, event type, lifecycle version, and UTC
occurrence time. Existing documents without lifecycle fields are treated as
`active` for backward compatibility.

Normal source refreshes update `lastSeenInSourceAt` without creating duplicate
events. Only a state transition increments `lifecycleVersion` and appends an
event.

`sourceLifecycleStatus` is derived from membership in the latest published
source snapshot. It is not authorized by an uncommitted per-document field.

# Published Source Snapshot

The production MongoDB setup does not require multi-document transactions, so
the backend uses a publish-after-complete source generation:

1. Serialize Letterboxd sync execution so two source snapshots cannot publish
   concurrently.
2. Validate the complete non-empty source response and allocate a unique source
   snapshot ID.
3. Upsert source metadata and append lifecycle events tagged with that snapshot
   ID.
4. After every required document write succeeds, insert one immutable source
   snapshot manifest containing the snapshot ID, completion time, and complete
   active source-ID set.
5. Treat insertion of that manifest as the publication point. A generation
   that fails before publication is ignored by active reads, lifecycle history,
   and worker cleanup authorization.

Browse, enrichment, matching, and export repositories derive active membership
from the newest published non-empty manifest. Lifecycle history includes only
events whose source snapshot was published. This prevents a partially failed
multi-document write from hiding active movies or authorizing file deletion.

The protected sync response returns the published source snapshot ID. The
worker's following snapshot read must return the same ID; a mismatch or missing
ID is a collection error that blocks all mutation.

# Backend State Transitions

Before writing anything, the Letterboxd sync validates that the fetched list is
non-empty, every required row is valid, and source IDs are unique. Failure
leaves both watchlist documents and sync-success history unchanged.

For a validated snapshot:

| Previous state | In snapshot | New state | Event |
|---|---:|---|---|
| none | yes | `active` | `added` |
| `active` | yes | `active` | none |
| `active` | no | `watched` | `watched` |
| `watched` | no | `watched` | none |
| `watched` | yes | `active` | `reactivated` |

Watched documents retain TMDB, IMDb, Letterboxd, title/year, artwork, provider,
and Plex-match metadata. Active browse, enrichment, Plex matching, Radarr
compatibility export, and normal worker desired state exclude watched
documents.

# Worker Snapshot Contract

`GET /api/export/movies/sync-state` retains `movies` for active desired movies
and adds the published source snapshot ID plus `watchedMovies` for durable
cleanup authorizations. A watched item contains:

- exact TMDB ID when known;
- IMDb ID, title, year, and source ID for diagnostics;
- watched time;
- lifecycle version and stable watched event ID.

An active and watched entry with the same TMDB ID is an invalid snapshot and
blocks mutation. A watched entry without a valid TMDB ID remains visible as
`watched_movie_missing_tmdb_identity` but cannot authorize a mutation.

# Watched Cleanup Planning

A watched event overrides normal destination ownership and protection only for
the same exact TMDB identity:

| Destination state | Decision |
|---|---|
| Exact movie exists in Radarr | `remove`, reason `watched_letterboxd_movie_remove_from_radarr`, `deleteFiles=true`. |
| Exact movie is absent from Radarr | `skip`, reason `watched_letterboxd_movie_absent_from_radarr`. |
| Exact movie exists on Plex watchlist | `remove`, reason `watched_letterboxd_movie_remove_from_plex_watchlist`. |
| Exact movie is absent from Plex watchlist | `skip`, reason `watched_letterboxd_movie_absent_from_plex_watchlist`. |

The Radarr and Plex actions execute independently. A failure in one does not
prevent the other action, but the run is partial and the failed desired state
is retried from fresh live data.

`deleteFiles` is an explicit decision field, not behavior inferred only from a
log message. Policy permits `deleteFiles=true` only for an exact-TMDB Radarr
remove carrying a valid watched event ID and the watched-file-deletion feature
gate.

# Manual Radarr Removal Observation

SQLite gains a migration-safe Radarr observation history. It records every
valid TMDB identity seen in a successful full Radarr collection, including
movies that are not in Letterboxd and are not worker-managed.

The first successful Radarr collection establishes a baseline and emits no
disappearance events. On later successful collections, a previously present
identity that is now absent creates a durable manual-removal event when the
identity is neither active in Letterboxd nor already covered by a watched
event.

That event authorizes only removal from the Plex watchlist, with reason
`manually_removed_radarr_movie_remove_from_plex_watchlist`. It overrides Plex
library and worker-ownership protection for the exact TMDB identity. It never
authorizes a Radarr or Plex-library deletion.

Worker-initiated watched removals update the Radarr observation record with
their watched event ID so the next collection does not misclassify them as
manual removals. Pending manual-removal events remain retryable until the Plex
watchlist target is absent.

# Reactivation

When a watched movie reappears in a successful Letterboxd snapshot, the backend
changes it to `active` and appends `reactivated`. It is no longer exported as a
watched cleanup authorization.

The next worker plan applies ordinary desired-state rules and may re-add the
movie to Radarr or the Plex watchlist. Previously completed cleanup history is
retained for audit but cannot authorize deletion of the new active lifecycle
version.

# Safety Policy

- Every destructive match uses exact TMDB identity. Title/year matching never
  authorizes removal.
- A zero-item or failed Letterboxd sync performs no lifecycle transitions.
- Only lifecycle events belonging to a published source snapshot can authorize
  cleanup.
- The source snapshot ID returned by the protected refresh must match the
  subsequent worker export.
- A failed backend, Radarr, Plex-watchlist, or Plex-library collection blocks
  all destination mutation for that run.
- Existing maximum removal count and percentage gates continue to cover
  watched and manual-removal actions.
- Only watched Radarr decisions can set `deleteFiles=true`.
- Ordinary no-longer-desired Radarr decisions retain `deleteFiles=false`, and
  downloaded rows remain protected from that ordinary cleanup path.
- Plex library methods remain read-only in the production executor.
- Missing or already-absent destinations count as converged, making retries
  idempotent.
- Reports include event ID, reason, ownership, `deleteFiles`, execution status,
  and redacted error details.

# Configuration And Rollout

Add `MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION`, defaulting to `false`. Production
must explicitly enable it in the host-local worker environment. It must never
be stored in Git with credentials or other host configuration values.

Rollout sequence:

1. Deploy schema-compatible backend and worker code with watched file deletion
   disabled.
2. Run a fresh non-mutating reconciliation and inspect watched, Radarr
   observation, removal-volume, identity, and destination decisions.
3. Verify the initial Radarr observation is baseline-only.
4. Enable watched file deletion in the protected host environment.
5. Run one supervised apply and inspect MongoDB lifecycle state, SQLite audit,
   reports, Radarr, Plex watchlist, container health, and logs.
6. Run an immediate convergence pass and require only expected keep/skip or
   explained retry decisions.
7. Leave hourly apply and exact-SHA CI-gated deployment enabled only after the
   supervised pass succeeds.

The migration cannot reconstruct Letterboxd movies hard-deleted before this
feature exists. Existing documents become the initial active baseline, and
only later disappearances create watched events. The first Radarr observation
similarly creates a baseline without interpreting older absences.

# Failure Recovery

MongoDB lifecycle transitions are idempotent by document state and lifecycle
version. A repeated source snapshot does not append duplicate events.

External actions are not transactional. Successful actions are recorded
immediately; failed actions stay visible in reports and are recomputed from
fresh live state. If Radarr deletion succeeds but Plex removal fails, the next
run sees Radarr absent and retries only Plex. If Plex succeeds first, Radarr
remains eligible for its watched delete until absent.

Reactivation wins over an older cleanup event because only the current watched
lifecycle version appears in the backend snapshot.

# Validation Strategy

Backend tests cover:

- empty, malformed, and duplicate source snapshots causing no writes;
- `active` to `watched`, stable watched, and `watched` to `active` transitions;
- lifecycle history and version idempotency;
- backward-compatible treatment of existing documents;
- active-only browse, TMDB enrichment, Plex matching, and exports;
- watched snapshot identity and event contracts;
- MongoDB repository persistence and API serialization.

Worker tests cover:

- strict watched snapshot parsing and active/watched conflicts;
- exact-TMDB watched Radarr removal with `deleteFiles=true`;
- no file deletion for any other reason;
- watched Plex removal despite unmanaged ownership or Plex-library presence;
- missing identity and collection-failure behavior;
- Radarr baseline creation and later manual disappearance detection;
- suppression of manual classification after worker-initiated removal;
- durable retry after partial failure and reactivation cancellation;
- removal count/percentage and feature-gate policy;
- report fields, SQLite migration, CLI exit codes, and health heartbeat.

Full verification uses the existing backend, worker, deployment, OKF, Docker,
and redacted Git history/tree secret-scan commands before push and deployment.

# Acceptance Criteria

- A valid non-empty Letterboxd disappearance creates exactly one durable
  watched transition.
- An empty Letterboxd response changes no state and causes no destination call.
- A watched exact-TMDB Radarr movie is removed with its files when both global
  apply and the watched-file-deletion gate are enabled.
- The same watched identity is removed from the Plex watchlist without any
  Plex-library mutation.
- A never-Letterboxd Radarr movie is not removed by the service.
- Removing that movie manually from Radarr causes its exact Plex-watchlist row
  to be removed on a later successful worker run.
- Re-adding a watched movie restores active desired-state behavior and
  preserves prior lifecycle history.
- Partial failures retry safely and a converged run performs no repeated
  destination mutation.

# Non-Goals

- Reading Letterboxd diary or rating history.
- Deleting Plex library items directly.
- Automatically deleting Radarr movies that were never in Letterboxd.
- Reconstructing watched history from before this feature is deployed.
- TV, Sonarr, or Android TV behavior changes.

# Links

- [Production Movie Sync](../architecture/movie_sync_production.md)
- [Sync Correctness Priority](../decisions/sync_correctness_priority.md)
- [VOD Filter Worker](../systems/vod_filter_worker.md)
- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
- [Roadmap](roadmap.md)
