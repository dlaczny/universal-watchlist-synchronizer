---
type: Decision
title: Backend Owns Integrations
description: The backend is the only component that handles external integration credentials and sync logic for Android-facing data.
tags:
  - decision
  - backend
  - integrations
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Decision

Android clients call only the backend API. The backend owns Letterboxd, TMDB,
Plex, MongoDB, credentials, tokens, sync logic, caching, matching, and read-only
client contracts.

# Rationale

This keeps Android simple, avoids leaking credentials into APKs, and allows the
backend to serve cached data when third-party services are unavailable.

# Consequences

- Android uses backend DTOs and backend image URLs.
- Integration behavior should be testable behind interfaces or adapters.
- Backend API docs must change with client contract changes.

