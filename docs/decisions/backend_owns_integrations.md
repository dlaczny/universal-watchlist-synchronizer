---
type: Decision
title: Backend Owns Integrations
description: The backend owns source integrations and read-model credentials; the worker separately owns local destination credentials.
tags:
  - decision
  - backend
  - integrations
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Decision

Android clients call only the backend API. The backend owns Letterboxd, Trakt,
TMDB, Plex inventory, MongoDB, source sync, caching, matching, and read-only
client contracts. The movie worker separately owns host-local Radarr and Plex
watchlist credentials and mutations.

# Rationale

This keeps Android simple, avoids leaking credentials into APKs, and allows the
backend to serve cached data when third-party services are unavailable.

# Consequences

- Android uses backend DTOs and backend image URLs.
- Integration behavior should be testable behind interfaces or adapters.
- Backend API docs must change with client contract changes.
- Destination credentials remain in the worker host environment and never flow
  through Android or GitHub Actions.
- Trakt device/OAuth credentials and the persistent key ring remain server-side.
  TV source reads must publish one complete generation last; a client or worker
  never owns TV synchronization or a partial-generation fallback.

