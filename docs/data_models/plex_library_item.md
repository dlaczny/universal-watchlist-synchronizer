---
type: Data Model
title: Plex Library Item
description: Cached Plex movie inventory record used for matching and Plex-only browse rows.
tags:
  - data-model
  - plex
  - mongodb
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

`plex_library_items` stores the latest cached Plex movie inventory. The backend
uses it for watchlist matching and for Plex-only rows when the user browses Plex
availability.

# Core Fields

| Field | Meaning |
|---|---|
| `ratingKey` | Plex item identifier. |
| `title` | Plex title. |
| `year` | Plex year. |
| `summary` | Plex summary. |
| `posterPath` | Plex poster path. |
| `backdropPath` | Plex backdrop path. |
| `imdbId`, `tmdbId`, `tvdbId` | External IDs parsed from Plex GUIDs. |
| `sectionKey` | Plex library section key. |
| `lastSeenAt` | Latest inventory sync timestamp. |

# Matching Role

Plex inventory is matched to watchlist movie records by stable IDs first, then
unique normalized title/year fallback.

# Links

- Plex integration: [Plex](../integrations/plex.md)
- Backend service: [Backend Service](../systems/backend_service.md)

