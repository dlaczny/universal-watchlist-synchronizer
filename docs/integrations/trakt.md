---
type: Integration
title: Trakt
description: Server-side TV watchlist and watched-progress source using protected OAuth device authorization.
tags:
  - trakt
  - tv
  - oauth
timestamp: 2026-07-19T00:00:00Z
version: 1.0.0
---

# Purpose And Ownership

Trakt is the Phase 1 authority for TV watchlist membership, watched progress,
episode/season schedules, and show status. Only the backend calls Trakt. No
client, worker, CI job, or exported DTO receives a Trakt secret or token.

# Connection Lifecycle

The backend uses Trakt's device flow. Every `/api/integrations/*` route,
including the otherwise read-only Trakt status route, requires the sync key.
Those protected routes start a device authorization, expose only the one-time
user code in that start response, report safe connection status, and can
explicitly disconnect the singleton connection. Device code, access token,
refresh token, client secret, and protected ciphertext are never returned by
status or written to logs.

Every Trakt request includes the required `trakt-api-version: 2` and
`trakt-api-key` headers; the API key value is the configured Client ID and is
kept server-side with the rest of the integration configuration. Requests also
identify the backend with a stable User-Agent so Trakt's Cloudflare edge does
not reject the server-side device flow as an anonymous automated request.

MongoDB stores the singleton connection in `trakt_connections` after values
are protected with the persistent ASP.NET Data Protection key ring. The
key-ring hosted service validates a writable key-ring before the service is
ready; Production requires an absolute persistent path. A restart can decrypt
the connection only with the original key-ring and application name. An
unreadable connection is recovered by reconnecting; do not delete the old
key-ring, because doing so also prevents recovery of other protected state.

# Complete Reads

The client reads `last_activities`, the entire show watchlist, watched show
progress, detailed per-show progress, full season schedules (including S00
identity-only specials), and full show metadata. Watchlist and progress reads
preserve Trakt's exact `X-Pagination-Page-Count` and request page size in the
generation manifest. Malformed, duplicate, missing, or inconsistent identities
and pagination are rejected before publication.

S00 specials with a valid Trakt episode identity but no optional TVDB identity
are excluded from the special-identity list; they do not invalidate an otherwise
complete source snapshot. Conflicting, duplicate, or malformed special entries
remain publication-blocking.

The synchronizer compares the activity cursor before and after collecting the
candidate. A cursor change is a race: no candidate is published. HTTP 429 is
reported as a typed rate-limit failure with the parsed `Retry-After` delay; the
hosted service logs a stable failure category and leaves the old generation in
place rather than guessing or retrying a partial source.

# Links

- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
- [Backend API](../apis/backend_api.md)
- [TV Sync Operations](../runbooks/tv_sync_operations.md)
- [Trakt OAuth documentation](https://docs.trakt.tv/docs/authentication-oauth)
