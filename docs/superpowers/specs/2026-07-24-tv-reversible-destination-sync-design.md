---
type: Design
title: TV Reversible Sonarr And Plex Destination Sync
description: Approved next TV release: Polish-provider-aware sequential Sonarr season sync and Plex Watchlist reconciliation, with no history writes or deletions.
tags:
  - tv
  - sonarr
  - plex
  - trakt
  - rollout
timestamp: 2026-07-24T00:00:00Z
version: 1.0.0
---

# Status

This design supersedes the ordering and assumptions of the historic Phase 3
plan for the next executable TV release. It follows the deployed Phase 1 Trakt
read model directly. Plex-history-to-Trakt writes remain a later independent
phase; Sonarr file, season, and series deletion remain blocked later phases.
Android TV work remains deferred by the explicit gate in
[Android TV Integration Backlog](../../backlog/android_tv_tv_integration.md).

# Goal

Make TV synchronization behave like the existing movie worker where it is safe
to do so: a worker consumes one complete backend generation, collects live
destination state, produces a deterministic report, and only then applies
reversible Sonarr and Plex Watchlist actions.

For a Trakt-watchlisted show, the worker selects one sequential season. It
adds and monitors that season in Sonarr only when that season is not available
on a tracked Polish streaming provider. It adds a show to Plex Watchlist when
the selected season is available on a tracked Polish provider or Plex has at
least one episode of the show in the configured TV library. It removes the
show from Plex Watchlist when neither condition is true.

# Scope

## Included

- A versioned backend worker export that marks whether a complete published TV
  generation is eligible for reversible destination planning without claiming
  that deletion or Trakt-history mutation is allowed.
- A TV worker workflow beside, but isolated from, the existing movie workflow.
- Exact TVDB Sonarr lookup, report-only adoption, add, selected-season
  monitoring, and search of aired-and-unwatched episodes.
- Exact external-ID Plex TV-library and universal-watchlist collection and
  reversible watchlist add/remove.
- SQLite run lease, ownership/origin, action audit, redacted reports, heartbeat,
  report-only rollout, and separately armed apply rollout.

## Explicitly Excluded

- Plex episode-history ingestion and every Trakt history write.
- Sonarr episode-file, season, or whole-series deletion; no Sonarr `DELETE`
  request is present in this release.
- Plex library mutation.
- Movie behavior changes, including movie Plex Watchlist cleanup.
- Android work.

# Identity And Source Rules

The backend remains the only Trakt/TMDB client and the only source of desired
TV state. The worker never calls Trakt or MongoDB directly.

TVDB is the canonical Sonarr identity. Sonarr's exact lookup accepts a
`tvdb:<id>` term, and the returned/addable series carries that identity. Trakt
provides external IDs including TVDB, but the field is optional upstream. A
missing, nonpositive, or conflicting TVDB ID therefore produces a stable
`identity_missing_tvdb` or `identity_conflict` report blocker and no Sonarr or
Plex mutation. No title/year fallback may authorize a destination mutation.

The deployed initial production generation contains a positive TVDB ID for all
251 published shows. This is rollout evidence, not a reason to weaken the
fail-closed rule for later generations.

Plex identification begins with an exact TVDB GUID. Exact TMDB or IMDb GUIDs
may be used only when the backend's verified identity maps them to the same
TVDB show. Unknown or ambiguous Plex rows are reported and left unchanged.

# Desired-State Rules

## Selected Season

The worker considers only regular numbered seasons; specials never influence
destination selection.

- An unstarted show selects Season 1.
- Otherwise it selects the season containing Trakt's next episode. If Trakt
  has no next episode, it selects the next numbered season after the latest
  fully watched season only when that season already has an aired episode.
- If no such season exists, the worker keeps an already-present Sonarr series
  monitored and makes no search/add request for an unknown future season.

The selected season is evaluated independently for Poland availability. A
provider observation for Season 1 never makes Season 2 unavailable or vice
versa. `unknown` and `stale` provider observations are not proof of
unavailability and block a new Sonarr add/search for that season.

## Sonarr

When the selected season is confirmed unavailable in Poland, the worker:

1. resolves an exact TVDB Sonarr series;
2. adds it with the configured root folder and quality profile if absent;
3. marks the selected season monitored;
4. leaves the series monitored so Sonarr can acquire future episodes; and
5. searches only selected-season episodes that are aired and not marked watched
   by the published Trakt generation.

When the selected season is available in Poland, the worker does not add or
search that season in Sonarr. It does not unmonitor an existing series or
season in this release.

An existing exact-TVDB Sonarr series is reported as an adoption candidate.
After the operator reviews a report-only run, an explicit adoption gate records
it as `manual` origin. Manual origin never authorizes a later Sonarr deletion.
Worker-created rows have `worker` origin. This release does not delete either
origin.

## Plex Watchlist

A show is desired on Plex Watchlist when either condition is true:

- the selected season has a confirmed Polish provider observation; or
- the configured Plex TV library contains at least one exactly identified
  episode of the show.

Otherwise the show is not desired and its exact Plex Watchlist row is removed.
This reconciliation intentionally applies to manually added Plex Watchlist
rows as well as worker-created rows. Plex library media is never modified.

# Architecture And Contracts

The backend adds a destination-specific capability envelope to a new TV worker
export contract version. It retains the Phase 1 `mutationCapable=false` field
for compatibility and does not reinterpret it as permission for destructive
cleanup. The new envelope is `destinationSyncCapable` plus stable blockers. It
is true only for a complete, current published generation with valid identity,
regular-season, and provider data required by the worker; it is false for any
source, identity, availability, or publication uncertainty.

The Python worker has a separate `sync_tv.py` composition root and TV-specific
SQLite tables. It does not modify movie tables, movie collectors, movie policy,
or movie executor code. Its stages mirror the movie worker:

```text
published TV export + Sonarr + Plex TV library/watchlist + SQLite state
  -> strict validator
  -> deterministic TV destination plan
  -> policy gates
  -> report-only or reversible apply
  -> immediate action audit, report, and heartbeat
```

Every apply action has a deterministic id built from the generation ID,
destination, TVDB ID, selected season where relevant, and desired transition.
The executor records each success before attempting the next action. A retry
collects fresh state and recomputes the plan rather than replaying stale work.

# Safety And Rollout

The checked-in defaults remain false. `TV_SYNC_ENABLED` permits collection and
reporting only. Effective apply requires both `TV_SYNC_APPLY=true` on the host
and an explicit per-run `--apply` request. Adoption additionally requires
`TV_SYNC_ADOPT_EXISTING_DESTINATIONS=true` for one supervised apply run.

The policy blocks all destination writes when the backend envelope is incapable,
the generation is stale, a required collection is incomplete, identity is not
exact, provider evidence is unknown/stale for a planned Sonarr action, a plan
contains duplicates, or action-count/percentage limits are exceeded. A Plex
row lacking an exact verified identity is never removed.

Rollout is ordered:

1. deploy with TV sync disabled and confirm movies remain unaffected;
2. enable TV report-only mode and review one complete plan;
3. enable adoption for one supervised run and inspect the recorded origins;
4. enable reversible apply, inspect Sonarr/Plex results, then run a second
   convergence pass; and
5. keep all deletion and Trakt-history flags false.

# Acceptance Criteria

- The same published generation and live destination state always produce the
  same report-only plan.
- A provider-available selected season never causes a Sonarr add/search.
- An unavailable unstarted show selects Season 1; completing a season advances
  selection to the next aired season.
- A selected Sonarr season remains monitored while only aired/unwatched
  episodes are searched.
- Plex Watchlist adds require provider availability or at least one exact Plex
  library episode; removals require an exact identity and neither desired fact.
- Existing Sonarr rows are adopted only after review and are never deleted.
- The TV workflow cannot alter movie state, Plex library media, Trakt history,
  or Sonarr files/series.
- Report-only and blocked runs create no destination mutation; apply converges
  on a second run.

# Plan And Documentation Cleanup

The next implementation plan will replace the historic Phase 3 plan as the
active destination-sync plan. The historic Phase 2 Plex-history plan remains
backlog work after this release. Phase 4/5 cleanup plans remain blocked by their
independent deletion gates. The roadmap and rollout ledger must be corrected to
record the real Phase 1 production connection and published generation before
the new worker is rolled out.
