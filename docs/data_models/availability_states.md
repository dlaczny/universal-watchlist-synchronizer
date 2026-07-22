---
type: Data Model
title: Availability States
description: Release and Plex availability states used by backend, Android, and worker decisions.
tags:
  - data-model
  - availability
  - plex
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Release Status

| Value | Meaning |
|---|---|
| `released` | Item is released or otherwise available for matching decisions. |
| `unreleased` | Item has a future release or first-air date. |
| `unknown` | Release state is missing or unrecognized. |

# Availability Status

| Value | Meaning |
|---|---|
| `available_on_plex` | Confident Plex match exists. |
| `not_on_plex` | Released item has no Plex match. |
| `unreleased` | Item should not be treated as missing yet. |
| `unknown_match` | Matching data is incomplete or ambiguous. |

# Important Rule

Do not hide uncertain Plex matches as unavailable. Represent ambiguous or
incomplete matching as `unknown_match` so Android and future operator tooling
can expose it clearly.

# TV Provider Observation

TV provider availability is separate from Plex matching. Its PL states are
`available`, `confirmed_unavailable`, `unknown`, and `stale`. `unknown` and
`stale` are failure/age uncertainty and must never be collapsed into
`confirmed_unavailable` or used to drive a destination action.

# Android Interpretation

- Plex availability has highest badge priority.
- Non-Plex items with `vodReleaseKnown=true` and `releasedOnVod=false` show
  `Not released`.
- Owned service availability can render provider badges.

# Links

- Watchlist item: [Watchlist Item](watchlist_item.md)
- Plex integration: [Plex](../integrations/plex.md)
- TV show: [TV Show](tv_show.md)

