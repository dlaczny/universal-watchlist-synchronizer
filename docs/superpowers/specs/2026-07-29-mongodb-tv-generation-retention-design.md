---
type: Design
title: MongoDB TV Generation Retention
description: Bound immutable TV-generation storage while preserving the published pointer, coherent reads, and fail-closed publication.
tags:
  - mongodb
  - tv
  - retention
  - storage
  - operations
timestamp: 2026-07-29T00:00:00Z
version: 1.0.0
---

# Status

Approved for implementation planning on 2026-07-29.

This design adds bounded retention to the immutable Trakt TV read model. It
does not change movie behavior, destination behavior, TV identity rules, or
the published API/export contracts.

# Goal

Keep the MongoDB TV read model comfortably within the Atlas Free cluster's
512 MiB data-plus-index limit without weakening publish-last consistency.

The system will:

- run scheduled full TV synchronization every six hours;
- retain no more than seven days of TV generations;
- retain no more than 48 total TV generations, including the published one;
- always preserve the generation named by the published pointer;
- remove safely identifiable abandoned staging rows after 24 hours; and
- fail closed before staging when mandatory pre-sync retention cannot complete.

# Production Evidence

A read-only production inspection on 2026-07-29 found:

| Area | Logical data plus indexes |
|---|---:|
| Entire Atlas cluster | 363.6 MiB |
| `tv_shows` | 350.4 MiB |
| `watchlist_items` | 9.8 MiB |
| `letterboxd_source_snapshots` | 2.3 MiB |
| All remaining collections | about 1.1 MiB |

`tv_shows` therefore accounted for 96.4 percent of measured cluster usage.
The database contained 124 immutable TV generations and 31,150 `tv_shows`
documents. A generation contained 251 or 252 show documents and averaged
2.81 MiB.

The published generation contained 9,774 embedded episodes. Its season and
episode payload accounted for 82.8 percent of its BSON size. Of the 124
generations, 123 were scheduled full generations and one was activity
triggered. The observed cadence was about 19.9 generations and 56 MiB of
logical growth per day.

Reducing the retained set from 124 to 48 generations is expected to free about
215 MiB and leave about 149 MiB in use. At four scheduled generations per day,
seven-day steady state is expected to remain near 90-100 MiB unless unusual
Trakt activity creates enough generations to reach the 48-generation cap.

# Scope

## Included

- Change `Trakt:FullSyncInterval` and its default from one hour to six hours.
- Add validated TV-generation retention settings:
  - maximum age: seven days;
  - maximum total generations: 48;
  - orphan grace period: 24 hours.
- Add a pure deterministic retention planner.
- Add MongoDB retention snapshot and apply operations.
- Run mandatory retention before staging and best-effort retention after
  successful publication.
- Emit redacted retention counts and stable failure codes.
- Add unit, Mongo integration, scheduler, configuration, and regression tests.
- Update the active OKF architecture, system, operations, and validation
  documents when implementation changes behavior.

## Explicitly Excluded

- Index creation, deletion, or redesign. TV indexes consumed only about
  2.3 MiB and are not material to the urgent storage problem.
- Retention for `letterboxd_source_snapshots`, `sync_runs`, movie lifecycle
  events, Plex inventory, provider catalogs, or Trakt connection state.
- Changing the nested TV document schema or replacing immutable generations
  with deltas or content-addressed documents.
- Any Sonarr, Plex, Trakt, Radarr, or filesystem behavior change.
- Any direct production cleanup during development or test execution.

# Configuration

The checked-in defaults become:

```text
Trakt:FullSyncInterval = 06:00:00
TvGenerationRetention:MaxAge = 7.00:00:00
TvGenerationRetention:MaxGenerations = 48
TvGenerationRetention:OrphanGracePeriod = 1.00:00:00
```

Startup validation rejects a nonpositive maximum age, a generation cap below
one, or an orphan grace period shorter than one hour. The retention behavior is
required rather than hidden behind a default-off feature flag.

The six-hour scheduled interval does not change the five-minute Trakt activity
poll. A real activity cursor change can still publish an activity-full
generation promptly. The lifecycle rule requiring absence from two scheduled
full generations can now take up to 12 hours instead of two hours.

# Architecture

Retention has three focused components:

1. `TvGenerationRetentionPlanner` is a pure application component. It receives
   the current generation ID, stored manifest summaries, safely dated orphan
   summaries, the current UTC time, and the validated policy. It returns an
   immutable keep/delete plan with stable reasons.
2. A retention repository reads only generation metadata and applies one
   validated plan to MongoDB. It does not decide policy.
3. A retention service re-reads the published pointer, builds and validates the
   plan, applies it, and returns redacted counts to the sync orchestration.

`TvSyncService` invokes retention while holding the existing per-account TV
operation coordinator:

```text
acquire TV coordinator
  -> load and validate published generation
  -> mandatory pre-sync retention
  -> collect and validate Trakt/TMDB state
  -> stage generation rows and lifecycle events
  -> publish manifest and advance pointer
  -> best-effort post-publication retention
  -> return the successful sync result
```

The pre-sync pass performs the initial production reduction and prevents a
failed cleanup from being followed by more staging writes. The
post-publication pass immediately returns the database to the age/count bound
after the new pointer is durable.

# Deterministic Retention Rules

The planner orders valid manifest summaries by `PublishedAt` descending and
then by generation ID using ordinal descending order.

It constructs the keep set as follows:

1. Add the published generation first, regardless of its age.
2. Consider remaining manifests newest first.
3. Keep a manifest only when its publication time is no older than seven days
   and the resulting total keep set does not exceed 48.
4. Mark every other noncurrent manifest generation for deletion.

The count of 48 includes the published generation. The age rule can therefore
retain fewer than 48 generations, while the count rule bounds activity bursts
within the seven-day window.

Legacy rows with `documentKind=legacy` never participate in retention.
The singleton pointer document shares `tv_sync_manifests` with manifests but
is never a manifest candidate.

# Abandoned Staging Rows

A failed or interrupted sync can leave `tv_shows` or `tv_lifecycle_events`
rows whose generation has no manifest. Retention may delete such rows only
when all of these conditions hold:

- the generation is not the published generation;
- no manifest exists for the generation;
- its ID matches the production generation-ID format and yields an
  unambiguous UTC creation time; and
- that time is at least 24 hours old.

Unknown, malformed, legacy, or unparseable identities remain untouched and are
reported by count for manual review. Retention never infers an orphan's age
from a show's lifecycle `UpdatedAt` value.

If the database contains generation data but no valid published pointer,
retention performs no destructive manifest cleanup. Existing publication
validation remains responsible for failing closed on an invalid pointer or
current generation.

# Apply Order And Pointer Protection

For every expired manifest generation, apply deletes:

1. matching generation-scoped `tv_shows` rows;
2. matching `tv_lifecycle_events` rows; and
3. the manifest document.

Orphan rows have no manifest step.

Immediately before applying a plan, the repository re-reads the pointer. If it
differs from the pointer used to build the plan, apply aborts without deleting
anything. Every delete filter also excludes the current generation ID, and
manifest deletion additionally requires `documentKind=manifest`.

Deletes are acknowledged and idempotent. A retry can finish an interrupted
child-first cleanup without depending on exact prior delete counts. No MongoDB
transaction or unsupported Atlas maintenance command is required.

# Failure Behavior

Mandatory pre-sync retention failures use a typed failure with stable code
`tv_generation_retention_failed`. They prevent staging and pointer changes.
The previously published generation remains readable.

After a pointer has advanced successfully, retention cannot retroactively make
that publication a failed sync. A post-publication cleanup failure logs
`tv_generation_retention_deferred`, leaves the successful generation
published, and is retried as mandatory pre-sync work before the next staging
attempt.

Invalid plans, pointer changes, unacknowledged MongoDB writes, and MongoDB
availability failures are all failures. Unparseable orphan IDs are preserved
findings rather than failures.

# Observability

Retention logs contain no show title, integration credential, external token,
or document payload. They record:

- mode (`pre_sync` or `post_publish`);
- protected current-generation presence;
- manifests kept by age/count;
- generations selected for deletion;
- show, lifecycle-event, and manifest delete counts;
- safely deleted orphan generation and document counts;
- preserved uncertain orphan counts; and
- the stable completion, failure, or deferred code.

The implementation does not claim exact bytes freed from delete counts. Atlas
`atlasSize` remains the operational source for total data-plus-index usage.

# Testing

## Pure Planner Tests

- The current generation is always retained, even when older than seven days.
- Newest noncurrent generations are retained deterministically.
- The total keep set never exceeds 48.
- The seven-day boundary is inclusive and older rows expire.
- Activity bursts within seven days are bounded by count.
- Equal publication timestamps use ordinal generation-ID ordering.
- Legacy, malformed, unparseable, and younger-than-24-hour orphans are kept.
- Safely dated orphans older than 24 hours are selected.
- A plan can be applied repeatedly without changing its semantic result.

## Mongo Integration Tests

- Retention removes expired show rows, lifecycle events, and manifests.
- The pointer document and its complete referenced generation remain coherent.
- `documentKind=legacy` rows remain unchanged.
- A pointer change between snapshot and apply causes no deletion.
- Child-first partial cleanup converges on retry.
- The manifest filter cannot delete the pointer document.
- An invalid or missing pointer fails closed when generation data exists.
- A successful cleanup leaves at most 48 manifests and no retained
  noncurrent manifest older than seven days.

## Sync And Regression Tests

- The default full-sync interval is six hours.
- Activity changes still request an activity-full generation before six hours.
- Scheduled absence confirmation now follows the six-hour interval.
- Pre-sync retention failure produces no staged rows or pointer advancement.
- Post-publication retention failure preserves the new published generation
  and emits the deferred code.
- Existing publish-last, lifecycle, browse, status, and export tests continue
  to pass.

# Production Rollout

Development and CI use only test databases. Before the first
retention-enabled production deployment:

1. record a fresh read-only `atlasSize` result and collection counts;
2. because Atlas backups are inactive, take a local logical dump of
   `tv_shows`, `tv_sync_manifests`, and `tv_lifecycle_events`;
3. deploy the validated backend release;
4. invoke or wait for one TV sync and verify mandatory pre-sync retention;
5. confirm the pointer and TV browse/export endpoints remain coherent;
6. confirm the retained manifest count is at most 48;
7. allow Atlas metrics to refresh and require total usage below 180 MiB; and
8. observe at least 24 hours to confirm approximately four scheduled full
   generations per day plus genuine activity-triggered generations.

Production deletion is authorized only through the deployed, tested retention
path. No ad hoc `deleteMany`, TTL conversion, index drop, or `compact` command
is part of rollout.

# Acceptance Criteria

- Scheduled full TV sync defaults to six hours.
- The five-minute activity poll remains unchanged.
- Successful retention keeps at most 48 total generations and no noncurrent
  generation older than seven days.
- The published generation is never selected or matched for deletion.
- Safely identifiable orphan staging rows expire after 24 hours; uncertain rows
  are preserved.
- Mandatory cleanup failure prevents new staging.
- Deferred post-publication cleanup does not invalidate a durable publication.
- Legacy TV rows and every non-TV collection remain unchanged.
- Production usage falls below 180 MiB after initial retention and remains
  bounded during the 24-hour observation.
- The complete backend validation suite and OKF validation pass.
