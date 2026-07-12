---
type: Data Model
title: Watchlist Item
description: Normalized active movie or TV item plus retained Letterboxd movie lifecycle state.
tags:
  - data-model
  - watchlist
  - api
timestamp: 2026-07-08T00:00:00Z
version: 0.2.0
---

# Overview

`WatchlistItem` is the normalized backend domain record for a wanted movie or TV
show. MongoDB persists this model in `watchlist_items`; backend DTOs expose it
to Android. Letterboxd movie documents are retained after they leave the active
watchlist so their watched authorization and event history remain auditable.

# Identity Fields

| Field | Meaning |
|---|---|
| `id` | Stable backend item ID. |
| `mediaType` | `movie` or `tv`. |
| `source` | `letterboxd`, `tmdb`, or `plex` for Plex-only API rows. |
| `sourceId` | ID from the source system. |

# Display Fields

| Field | Meaning |
|---|---|
| `title` | Display title. |
| `year` | Release year when known. |
| `overview` | Summary text. |
| `posterUrl` | Backend-relative or absolute poster URL. |
| `backdropUrl` | Backend-relative or absolute backdrop URL. |
| `genres` | Detail-view genres. |
| `runtimeMinutes` | Detail runtime. |
| `originalLanguage` | TMDB original language. |
| `tmdbVoteAverage`, `tmdbVoteCount` | TMDB rating metadata. |

# Availability Fields

| Field | Meaning |
|---|---|
| `releaseStatus` | `released`, `unreleased`, or `unknown`. |
| `availabilityStatus` | `available_on_plex`, `not_on_plex`, `unreleased`, or `unknown_match`. |
| `libraryMembership` | `watchlist`, `watchlist_and_plex`, or `plex_only`. |
| `vodReleaseKnown` | True when TMDB provider enrichment is known. |
| `releasedOnVod` | True when PL or US has stream, rent, or buy provider data. |
| `vodRegions` | Regions that contributed VOD release evidence. |
| `ownedServiceAvailability` | Subscribed service matches, for example Prime or Max. |

# Timing Fields

| Field | Meaning |
|---|---|
| `addedAt` | Source watchlist-added timestamp when known. |
| `updatedAt` | Last backend update timestamp. |

# Letterboxd Lifecycle Fields

MongoDB movie documents also store:

| Field | Meaning |
|---|---|
| `lastSeenInSourceAt` | Last published Letterboxd snapshot in which the movie was active. |
| `lastWatchedAt` | Time of the latest active-to-watched transition. |
| `lifecycleVersion` | Monotonic per-movie lifecycle version. |
| `lifecycleEvents` | Ordered immutable `added`, `watched`, and `reactivated` events with source snapshot IDs. |

Current active/watched membership is not trusted from a mutable document flag.
The latest `letterboxd_source_snapshots` manifest contains the complete active
source-ID set plus the current watched event references. Normal browse,
enrichment, matching, and compatibility export select only active IDs. The
worker watched export joins manifest references to retained documents.

Existing documents are treated as active when no manifest exists, allowing a
migration baseline. Disappearances that happened before the first published
lifecycle manifest are not backfilled as watched events.

# Mongo Trace Fields

MongoDB documents also store matching and integration trace fields such as
`ImdbId`, `LetterboxdPath`, `TmdbId`, `TvdbId`, `TmdbMetadataStatus`,
`TmdbMetadataError`, `PlexRatingKey`, `PlexMatchedAt`, `PlexMatchReason`, and
`PlexMatchConfidence`.

# Links

- API: [Backend API](../apis/backend_api.md)
- Availability states: [Availability States](availability_states.md)

