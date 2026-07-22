---
type: Architecture
title: TV Sync Read Model
description: Phase 1 Trakt-backed TV generations, Polish provider observations, and non-destructive read contracts.
tags:
  - tv
  - trakt
  - mongodb
  - read-model
timestamp: 2026-07-19T00:00:00Z
version: 1.0.0
---

# Phase 1 Boundary

Phase 1 is a backend-owned, non-destructive TV read model. Trakt supplies TV
watchlist membership, watched progress, episode schedules, and show status;
TMDB supplies exact-ID metadata and Poland (`PL`) provider observations. MongoDB
stores the protected Trakt connection and one immutable published generation.
Clients and the worker read that published generation only.

Plex episode history, Trakt history writes, Sonarr actions, Plex-watchlist
actions, cleanup authorizations, and every TV apply/adoption/delete path are
not implemented. The TV export declares that limitation with
`mutationCapable=false`, `plex_history_phase_not_implemented`, and
`worker_tv_mutation_disabled`. The six TV-related host switches are locked
false by the production Compose file as well as the environment examples.

# Publication Flow

```text
Trakt watchlist + watched progress + prior generation
  -> detailed schedules and exact-ID TMDB enrichment
  -> lifecycle reduction and validation
  -> staged MongoDB generation
  -> immutable manifest published last
  -> browse/detail/status/export readers
```

The source catalog is the union of current Trakt watchlist membership, watched
progress, and rows retained by the previous generation. A source, identity,
schedule, pagination, or activity-cursor failure stages and publishes nothing;
the previous published pointer remains readable. The Trakt activity cursor is
read before and after a full collection while a single per-account coordinator
lease is held. A change during collection rejects the candidate rather than
publishing a mixed snapshot.

`TvSyncHostedService` polls activity every five minutes. It performs a full
generation when no generation exists, when the hourly full-sync interval is
due, or when the activity cursor changed; it does not synthesize generations
while the connection is disconnected, revoked, or requires refresh.

# Lifecycle And Availability

The persistent lifecycle states are `active`, `caught_up`, `source_removed`,
`terminal_cleanup_pending`, and `retired_terminal`. In Phase 1 only the first
three may be published. Source removal requires two scheduled complete
confirmations; activity-triggered generations do not advance that confirmation.
`reactivated` is an immutable event, not a stored state.

TMDB observations are regional and use stable provider IDs. A successful PL
response becomes `available` or `confirmed_unavailable`. An upstream provider
failure is never represented as unavailable: it publishes `stale` where a
previous observation can be retained, otherwise `unknown`.

# Reader Contract

`GET /api/watchlist` and detail reads are served from the one published TV
generation. `collection=all` includes movies plus active TV rows only;
`collection=tv` defaults to active and accepts the TV lifecycle filters
documented in [Backend API](../apis/backend_api.md). The worker export is a
read-only envelope and is intentionally not a Sonarr command contract.

# Links

- [Trakt Integration](../integrations/trakt.md)
- [TV Show](../data_models/tv_show.md)
- [TV Sync Operations](../runbooks/tv_sync_operations.md)
- [TV Integration Rollout](../reports/tv_integration_rollout.md)
- [Approved TV Design](../superpowers/specs/2026-07-13-tv-show-integration-design.md)
