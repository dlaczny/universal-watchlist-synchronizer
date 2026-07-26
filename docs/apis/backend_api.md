---
type: API
title: Backend API
description: HTTP contracts for health, browsing, source synchronization, images, status, and worker exports.
tags:
  - api
  - backend
  - worker
timestamp: 2026-07-14T00:00:00Z
version: 0.5.0
---

# Boundary

The backend serves cached MongoDB data to clients and complete desired movie
state to the production worker. On the trusted LAN, browse/detail routes,
read-only exports, and `GET /api/sync/status` require no API key. Every
`POST /api/sync/*` route and **every** `/api/integrations/*` route (including
`GET /api/integrations/trakt/status`) requires `X-Watchlist-Sync-Key` when
`Sync:ApiKey` is configured; Production startup fails when that key is missing.

# Health And Browse

| Endpoint | Contract |
|---|---|
| `GET /healthz` | Lightweight process health; does not prove integration or MongoDB health. |
| `GET /api/watchlist` | Cached browse rows filtered by `collection`, `availability`, `sort`, and TV-only `state`. |
| `GET /api/watchlist/{id}` | Cached detail row or `404`. |
| `GET /api/sync/status` | Latest persisted backend sync status or `404`; MongoDB outage returns `503`. |

`GET /api/watchlist` accepts:

| Query | Values | Default |
|---|---|---|
| `collection` | `all`, `movie`, `tv` | `all` |
| `availability` | comma-separated `plex`, `not_on_plex`, `unreleased`, `unknown_match` | all |
| `sort` | `added_desc`, `title_asc` | `added_desc` |
| `state` | TV-only `active`, `caught_up`, `retired` | `active` when `collection=tv`; active TV only when `collection=all` |

Invalid query values return `400`. Selecting Plex availability may include
Plex-only movies with `source=plex` and `libraryMembership=plex_only`.
`state` with `collection=all` or `collection=movie` returns `400`. TV browse
and detail rows keep the existing common movie fields and add a nullable `tv`
object. The JSON field names for its version-1 public contract are exact:

```text
GET /api/watchlist?collection=tv
  items[].tv: {
    contractVersion, lifecycleState, lastLifecycleEvent, traktStatus,
    inWatchlist, identityStatus, airedEpisodes, completedEpisodes,
    nextEpisode, seasonCleanupPending, plexAvailability, availability,
    relevantSeasonNumber, relevantSeasonAvailability
  }

GET /api/watchlist/{id}
  item.tv: {
    contractVersion, lifecycleState, lastLifecycleEvent, traktStatus,
    inWatchlist, identityStatus, airedEpisodes, completedEpisodes,
    lastWatchedEpisode, nextEpisode, availability, destinations, seasons
  }
  seasons[]: { seasonNumber, airedEpisodes, completedEpisodes,
    hasKnownFutureEpisode, cleanupState, availability, episodes }
  episodes[]: { seasonNumber, episodeNumber, title, airedAt, watched, watchedAt }
```

`inWatchlist` is the public browse/detail membership field. `airedEpisodes` and
`completedEpisodes` are public totals; they are deliberately not abbreviated.
`GET /api/watchlist/{id}` returns `404` when no cached item exists. These
objects expose no credential, authorization, mutation, or apply field.

# Movie Sync

`POST /api/sync/movies` is the production source-refresh operation. It runs:

1. Letterboxd movie import.
2. TMDB movie enrichment.
3. Plex movie inventory and availability sync.

It does not run TV sync. The response includes `startedAt`, `finishedAt`, each
stage result, and overall `status=completed|partial`. The nested Letterboxd
result includes `itemsFetched`, `itemsUpserted`, `itemsMarkedWatched`, and the
published `sourceSnapshotId`. TMDB not-found or failed items make the result
partial.

An empty Letterboxd result is not a successful empty watchlist. Empty,
duplicate, malformed, or nonpositive source identities are rejected before any
repository read or write. The API returns a dependency error and publishes no
lifecycle transition or source snapshot.

# Other Sync Operations

| Endpoint | Purpose |
|---|---|
| `POST /api/sync/letterboxd` | Import Letterboxd movies. |
| `POST /api/sync/tmdb/movies` | Enrich all imported movies. |
| `POST /api/sync/tmdb/movies/{id}` | Enrich one movie or return `404`. |
| `POST /api/sync/plex/movies` | Refresh Plex movie inventory and matching. |
| `POST /api/sync/availability/refresh` | Run stale-aware Plex availability refresh. |
| `POST /api/sync/tmdb/tv` | Returns `410 Gone` with `code=legacy_tv_sync_disabled`; the TMDB-account TV route is retired. |
| `POST /api/sync/tv` | Runs one protected scheduled full Trakt TV read generation. Response includes source counts, provider failure count, generation ID/kind, and `mutationCapable=false`; destination eligibility is published only in the schema-v2 export. |
| `POST /api/sync/all` | Compatibility combined movie sequence; TV is reported disabled and is not run. |

TV source/validation/race failures publish no candidate and preserve the
previous TV generation. Typed dependency failures use stable error responses;
in particular, the server resumes a Trakt HTTP 429 with a positive
`Retry-After` delay up to six times before returning `503` with
`code=trakt_rate_limited`. That response retains the `Retry-After` header when
Trakt supplied a positive delay, so callers can retry later without treating
the source as invalid.
the endpoint never converts a failed provider observation into unavailable.

The successful manual TV sync result has this exact JSON shape:

```text
{
  status, startedAt, finishedAt, generationId, kind,
  watchlistItemsFetched, progressItemsFetched, showsPublished,
  providerFailures, mutationCapable, healthReasons
}
```

`generationId` identifies the newly published immutable generation and `kind`
is the generation kind (for this endpoint, `scheduled_full`).
`mutationCapable` remains `false`; it is a compatibility field and never
authorizes a destination, history, or cleanup operation. A failed request has
no new `generationId` and preserves the previous published generation.

# Trakt Connection Management

The Trakt management routes require the same `X-Watchlist-Sync-Key` as sync
mutations. The backend returns the one-time user code only from the start
response; status responses never expose device codes, user codes, access
tokens, refresh tokens, client secrets, or protected values.

| Endpoint | Contract |
|---|---|
| `POST /api/integrations/trakt/device/start` | Starts device authorization and returns the verification URL, one-time user code, expiry, and polling interval. Repeating an unexpired start returns `409` with `code=trakt_connection_pending`. Trakt dependency failure returns `503` with `code=trakt_unavailable`; a critical connection-state persistence timeout returns `503` with `code=trakt_persistence_unavailable`. |
| `GET /api/integrations/trakt/status` | Returns only the public connection status, connected/expiry timestamps, and stable last-error code. |
| `DELETE /api/integrations/trakt/connection` | Removes the singleton Trakt connection and returns `status=disconnected`. |

A successful device-token response that cannot be parsed or represented is
treated as consumed: the pending authorization becomes non-pollable before the
fixed parse error is reported. An unusable successful refresh similarly moves
the connection to `refresh_required` while retaining only its protected stored
credentials for explicit reconnection.

# Image Proxies

| Endpoint | Contract |
|---|---|
| `GET /api/images/tmdb/{size}/{fileName}` | Proxies `w500` or `w1280` TMDB artwork. |
| `GET /api/images/plex/{ratingKey}/{kind}` | Proxies `poster` or `backdrop` for a stored Plex movie. |

Image errors use `400` for invalid shape, `404` for missing content, `502` for
upstream failure, and `503` when required Plex proxy configuration is missing.

# Exports

Worker contracts are documented separately in [Export Endpoints](export_endpoints.md).
Browse, detail, compatibility export, TMDB enrichment, and Plex matching expose
only active Letterboxd IDs from the latest published manifest. Watched rows are
retained for audit and cleanup authorization but are absent from normal reads.

`GET /api/export/tv/sync-state` is read-only and returns `404` until one TV
generation has been published. Schema version `2` adds
`destinationSync: { capable, blockers }` and each regular season's
`polandAvailability`; it preserves `mutationCapable=false`, the legacy health
reasons, incapable Plex-history block, and empty cleanup authorization list.
`destinationSync.capable` means only that the immutable generation is valid,
published, and has no manifest validation failures. It is not an apply command,
and the worker still requires complete live collection, exact identity, current
provider evidence, host gates, and an explicit `--apply` before a reversible
destination action.

TVDB is the identity required for Sonarr actions. Missing, nonpositive, or
conflicting TVDB values fail closed; title/year matching cannot authorize an
action. The worker chooses one regular season (Season 1 when unstarted,
otherwise the next-episode season or next aired season after completion) and
uses that season's PL provider observation. A selected season that is
`unknown` or `stale` blocks a new Sonarr action. `GET /api/export/sonarr/tv`
remains a compatibility empty array with
`X-Watchlist-Contract: compatibility-only`; neither route performs a Sonarr
operation.

`GET /api/sync/status` remains a public read and returns `404` when there is no
persisted backend sync status. Its optional TV object uses exact names:

```text
{
  status, lastSuccessfulSyncAt,
  tv: {
    connectionStatus, lastActivityPoll, lastCompleteGeneration, generationAge,
    activeCount, caughtUpCount, sourceRemovedCount, providerErrorCount,
    mutationCapable, healthReasons
  }
}
```

The protected `GET /api/integrations/trakt/status` is different: it reports
only the Trakt connection state and must not be used as a substitute for this
public operational status.

# Links

- [Backend Service](../systems/backend_service.md)
- [Watchlist Item](../data_models/watchlist_item.md)
- [Availability States](../data_models/availability_states.md)
- [TV Show](../data_models/tv_show.md)
