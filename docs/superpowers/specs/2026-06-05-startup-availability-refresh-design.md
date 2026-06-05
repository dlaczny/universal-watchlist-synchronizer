# Startup Availability Refresh Design

## Goal

Keep Android TV startup fast while refreshing Plex availability often enough that the watchlist reflects what is currently available on the user's Plex server.

The app should show cached MongoDB watchlist data immediately when opened. Plex availability should refresh through the backend without blocking the first render.

## Scope

Included:

- Android TV triggers a backend-owned availability refresh when the app opens.
- Backend decides whether Plex movie sync is stale before doing network work.
- Backend exposes a lightweight refresh endpoint separate from full Letterboxd/TMDB sync.
- Android reloads the current watchlist once after a completed refresh.
- Failures keep cached data visible.

Excluded:

- Running Letterboxd sync on every app open.
- Running TMDB enrichment on every app open.
- Blocking Android startup until Plex finishes.
- Plex TV matching.
- A visible sync settings screen.

## Recommendation

Use cached-first startup with stale-aware Plex refresh.

Android should not call `POST /api/sync/all` when the app opens. Full sync is too slow and depends on Letterboxd, TMDB, and Plex all being healthy. App-open freshness should focus on Plex availability because that is the field most likely to change before browsing.

## Backend Endpoint

Add:

```http
POST /api/sync/availability/refresh
```

The endpoint should:

1. Read the latest successful Plex movie sync timestamp.
2. Compare it with the current backend time.
3. Skip Plex sync when the latest successful Plex sync is fresh.
4. Run Plex movie sync when the latest successful Plex sync is missing or stale.
5. Return a small result that tells Android whether it should reload the current watchlist query.

Initial freshness threshold:

```text
15 minutes
```

This can later become `AvailabilityRefresh:FreshnessWindowMinutes`, but a constant is acceptable for the first slice if tests cover it.

## Response Contract

Skipped response:

```json
{
  "status": "skipped",
  "ranPlexSync": false,
  "reason": "fresh",
  "startedAt": "2026-06-05T12:00:00Z",
  "finishedAt": "2026-06-05T12:00:00Z",
  "plex": null
}
```

Completed response:

```json
{
  "status": "completed",
  "ranPlexSync": true,
  "reason": "stale",
  "startedAt": "2026-06-05T12:00:00Z",
  "finishedAt": "2026-06-05T12:00:05Z",
  "plex": {
    "status": "completed",
    "sectionsScanned": 1,
    "itemsFetched": 500,
    "itemsUpserted": 500,
    "itemsDeleted": 2,
    "watchlistItemsMatched": 40,
    "watchlistItemsNotMatched": 220,
    "watchlistItemsUnknown": 3
  }
}
```

`reason` values:

- `fresh`: latest successful Plex movie sync is inside the freshness window.
- `stale`: latest successful Plex movie sync is older than the freshness window.
- `missing`: no previous successful Plex movie sync is known.

## Android Startup Flow

On app open:

1. Build the current watchlist query from selected collection, availability filter, and sort mode.
2. Load `GET /api/watchlist` and render cached data immediately.
3. Start `POST /api/sync/availability/refresh` in the background.
4. If the refresh response has `ranPlexSync = true`, reload the current `GET /api/watchlist` query once.
5. If the response has `ranPlexSync = false`, do nothing.
6. If the refresh fails, keep cached data visible and do not block navigation.

The reload should preserve the user's current top filter, availability popup state, sort mode, and focus as much as practical. V1 can reload the grid and let focus return to the first visible card if preserving exact focus is too invasive.

## Error Handling

Backend:

- Plex unreachable or unauthorized should use the existing Plex dependency handling.
- MongoDB unavailable should continue returning `503`.
- A refresh failure should not mutate watchlist availability to unavailable.

Android:

- Startup refresh failure should not replace the grid with an error screen if cached watchlist loading succeeded.
- If both cached watchlist loading and refresh fail, show the existing backend-offline/error state.
- V1 may log refresh failures without visible UI changes.

## Data Flow

```text
Android opens
  |
  +--> GET /api/watchlist ------------------> MongoDB cached read model --> render grid
  |
  +--> POST /api/sync/availability/refresh --> stale check
                                               |
                                               +-- fresh --> skipped
                                               |
                                               +-- stale/missing --> Plex sync --> MongoDB availability updates
                                                                        |
                                                                        +--> Android reloads GET /api/watchlist once
```

## Testing

Backend tests:

- Returns `skipped` when latest successful Plex sync is inside the freshness window.
- Runs Plex sync when no previous successful Plex sync exists.
- Runs Plex sync when previous successful Plex sync is stale.
- Propagates Plex dependency errors through the same HTTP behavior as manual Plex sync.
- Uses deterministic time through `TimeProvider`.

Android tests:

- Startup renders cached watchlist independently from refresh result.
- Startup calls availability refresh after initial load.
- A completed refresh triggers one additional watchlist load.
- A skipped refresh does not reload.
- A failed refresh keeps cached data visible.

## Documentation Updates

Update:

- `docs/api.md` with the new endpoint and response contract.
- `docs/architecture.md` with cached-first startup refresh behavior.
- `docs/android-tv.md` with the Android startup behavior and manual testing steps.

## Later Extensions

- Make freshness threshold configurable.
- Add visible "last refreshed" metadata.
- Add a manual refresh command in Android TV controls.
- Add a background hosted job so availability can refresh before Android opens.
- Use a single-flight lock so multiple app opens do not run concurrent Plex scans.
