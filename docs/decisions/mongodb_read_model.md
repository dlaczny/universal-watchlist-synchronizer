---
type: Decision
title: MongoDB Read Model
description: MongoDB stores the normalized read model and sync history used by backend APIs.
tags:
  - decision
  - mongodb
  - read-model
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Decision

MongoDB stores the normalized watchlist read model, Plex inventory, and sync
history. Android browsing reads through the backend, not through live
third-party calls.

# Rationale

Cached MongoDB data keeps Android browsing fast and stable. It also gives sync
metadata for freshness, integration failures, and match confidence.

# Consequences

- MongoDB outages surface as `503`.
- Bootstrap seed data exists only for empty local-development collections.
- Sync jobs must be repeatable and idempotent.

