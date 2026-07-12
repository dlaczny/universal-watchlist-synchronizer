---
type: System
title: Android TV Client
description: Read-only Android TV client optimized for remote navigation and backend-owned DTOs.
tags:
  - android
  - android-tv
  - client
timestamp: 2026-07-08T00:00:00Z
version: 0.2.0
---

# Overview

The Android client lives under `android/`. Feature work and deployment are
currently on hold. The preserved client is read-only and consumes backend DTOs
through `WatchlistApiClient`; only contract-preserving fixes are in active
scope.

# Runtime Contract

- Calls only the backend API.
- Does not call Letterboxd, TMDB, Plex, Radarr, or MongoDB directly.
- Does not store third-party credentials.
- Uses backend-provided image URLs.
- Loads cached watchlist data first, then triggers stale-aware availability
  refresh in the background.

# Key Classes

| Class | Responsibility |
|---|---|
| `MainActivity` | Remote-first poster grid, left rail, sort controls, focus links, state persistence, startup availability refresh. |
| `DetailsActivity` | Plex-like detail page rendered from grid data, then refreshed from `GET /api/watchlist/{id}`. |
| `WatchlistApiClient` | Builds backend paths, performs HTTP GET/POST calls, parses JSON DTOs, resolves relative image URLs. |
| `WatchlistItem` | Serializable browse item DTO used by the UI. |
| `WatchlistItemDetails` | Detail DTO and metadata summary formatting. |
| `BrowsingState` | Immutable selected collection, sort, availability service, unavailable toggle, and focus state. |
| `WatchlistFilters` | Local service-aware visibility filtering. |
| `CollectionOrganizer` | Legacy available-only filtering and alphabetical sorting helper. |
| `RemoteImageLoader` | Async image loading with generation checks to ignore obsolete requests. |
| `WatchlistConfig` | Build-time backend URL and responsive grid-column bounds. |

# UI Behavior

- Persistent left rail: All, Movies, TV Shows, Plex and owned-service filters,
  unavailable toggle, disabled search.
- Main content header: active collection title, visible item count, Date added
  and A-Z sort controls.
- Poster grid with artwork, title, availability/provider badge, loading, empty,
  and backend error states.
- D-pad focus links between rail, sort controls, and poster grid.
- Select opens details; Back returns to grid with focus restored when possible.

# Availability Badges

- `available_on_plex` renders as Plex availability.
- `vodReleaseKnown=true` and `releasedOnVod=false` on non-Plex items renders
  `Not released`.
- `ownedServiceAvailability` renders provider badges such as Prime, Max,
  SkyShowtime, Crunchyroll, or a provider plus count.
- Missing artwork uses neutral fallback UI.

# Build Configuration

| BuildConfig field | Meaning |
|---|---|
| `WATCHLIST_API_BASE_URL` | Backend URL. Emulator default is `http://10.0.2.2:5000`; TV builds use the LAN backend URL. |
| `WATCHLIST_GRID_COLUMNS` | Preferred grid density clamped between 5 and 9 and reduced when viewport width cannot fit it. |

# Manual Remote Test

Use only D-pad navigation, Select, and Back:

1. Confirm focus starts in a usable location.
2. Confirm rail navigation switches All, Movies, and TV Shows.
3. Confirm sorting reloads the backend query.
4. Confirm availability filters and unavailable toggle produce expected items.
5. Confirm left/right focus movement between first grid column and rail.
6. Confirm detail screen opens and Back restores grid focus.
7. Confirm error, empty, and missing artwork states are readable on TV.

# Links

- API: [Backend API](../apis/backend_api.md)
- Product decision: [Android TV Read-Only V1](../decisions/android_tv_read_only_v1.md)
- Validation: [Validation](../runbooks/validation.md)

