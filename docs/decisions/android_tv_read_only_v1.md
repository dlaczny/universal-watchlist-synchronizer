---
type: Decision
title: Android TV Read-Only V1
description: Version 1 Android clients browse backend data but do not mutate watchlists.
tags:
  - decision
  - android-tv
  - product
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Decision

Version 1 Android UI is read-only. Do not add create, edit, delete, reorder, or
watchlist mutation flows unless product scope changes explicitly.

# Rationale

The first goal is a reliable remote-first TV browsing experience. Letterboxd and
TMDB remain the watchlist sources of truth, while the backend provides a cached
read model.

# Consequences

- Android screens focus on browsing, filtering, details, and state display.
- Sync and mutation operations remain backend or worker concerns.
- API/client contract changes are acceptable while local-only, provided tests
  and OKF docs are updated together.

