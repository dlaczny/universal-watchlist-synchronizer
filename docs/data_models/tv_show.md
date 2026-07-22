---
type: Data Model
title: TV Show
description: Immutable Phase 1 TV generation show, episode progress, lifecycle, identity, and provider semantics.
tags:
  - tv
  - data-model
  - lifecycle
timestamp: 2026-07-19T00:00:00Z
version: 1.0.0
---

# Identity And Membership

The public TV item ID is `tv-trakt-{traktId}`. A positive Trakt show ID is the
canonical source identity; TVDB, TMDB, and IMDb are exact supporting identities.
`identityStatus` is `verified`, `missing`, `conflict`, or `legacy_unresolved`.
Legacy rows are migrated only when identity is exact; unresolved or conflicting
rows are quarantined and never become current read-model authority.

Each show belongs to exactly one immutable generation. Its manifest records
the source activity cursor, exact source pagination counts, membership/progress
hashes, lifecycle event references, and a publish-last pointer. Browsers and
exports resolve that one generation, never an in-progress staging set.

# Progress And Seasons

`airedEpisodes` and `completedEpisodes` are Trakt totals. Numbered seasons
contain ordered episode progress with Trakt episode ID, optional TVDB episode
ID, season/episode number, title, air time, watched flag, and watched time.
`lastWatchedEpisode` and `nextEpisode` are schedule-joined regular episodes;
they are nullable and are never inferred from title matching.

S00 entries are `specialEpisodeIdentities`: exact Trakt/optional TVDB episode
identities used only for a later Plex resolver. They do not contribute to
watched/aired totals, next/last episode, season progress, provider claims, or
any cleanup decision.

# Lifecycle

| Stored value | Phase 1 meaning |
|---|---|
| `active` | In the current Trakt watchlist or has unfinished aired progress. |
| `caught_up` | Current source row with no aired unwatched regular episode. |
| `source_removed` | Absent from source after two scheduled complete confirmations. |
| `terminal_cleanup_pending` | Parsed for future compatibility; never published in Phase 1. |
| `retired_terminal` | Parsed for future compatibility; never published in Phase 1. |

Events are `added`, `caught_up`, `source_removed`, or `reactivated`; each has a
stable ID and canonical predicate hash. `reactivated` is an event only.
`source_removed` means Trakt source absence, not that a show ended, was
cancelled, removed from Sonarr, or removed from Plex.

# Provider Availability

Provider observations are for region `PL` and have state `available`,
`confirmed_unavailable`, `unknown`, or `stale`. Offers carry stable TMDB
provider ID, provider name, category (`flatrate`, `free`, `ads`, `rent`, or
`buy`), and optional artwork. `unknown` and `stale` are uncertainty, never a
negative availability claim.

# Links

- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
- [Availability States](availability_states.md)
- [Trakt Integration](../integrations/trakt.md)
