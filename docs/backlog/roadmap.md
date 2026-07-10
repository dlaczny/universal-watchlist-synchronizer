---
type: Backlog
title: Roadmap
description: Remaining known work after the OKF conversion.
tags:
  - backlog
  - roadmap
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Android TV

- Extract `MainActivity` responsibilities into focused Android TV components.
- Add focused automated coverage for loader generation and activity lifecycle
  state restoration.
- Add connected-device UI tests for focus navigation when the test environment
  supports it.
- Consider Apple TV-style featured carousel after the poster-grid navigation is
  stable.

# Backend

- Cache TMDB poster/backdrop image bytes locally instead of storing only TMDB
  image URLs.
- Refine TMDB subscribed-service matching with provider IDs after confirming
  provider names/IDs from live data.
- Add an operator review flow or report for `unknown_match` Plex movie matches.
- Add Plex availability matching for TV shows.
- Build the reserved Sonarr TV export shape.

# Documentation

- Add richer JSON schemas only if they become useful for API contract tooling.
