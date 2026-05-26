# Android TV Foundation Design

## Summary

Build the first Android TV client slice for the watchlist app. The app is read-only and consumes the backend API documented in `docs/api.md`.

## Scope

- Create an Android project under `android/`.
- Build an Android TV launcher activity.
- Fetch watchlist data from the backend API.
- Support Movies vs TV Shows and All vs Available.
- Render a Plex-like Featured Detail Row:
  - top filter controls,
  - focused detail panel,
  - horizontal row of focusable items.
- Keep Android phone support out of this slice.

## Technology Choice

Use a native Android app in Java with Android Gradle Plugin 9.2.0 and Gradle 9.4.1. This keeps the first TV slice small and buildable without introducing Compose/Kotlin complexity. Android Studio can still open and evolve the project later.

## Client Architecture

- `WatchlistApiClient`: fetches and parses backend JSON.
- `WatchlistItem`: immutable client model for backend DTOs.
- `WatchlistFilters`: filters items by media type and availability.
- `WatchlistConfig`: resolves backend base URL.
- `RemoteImageLoader`: best-effort poster/backdrop loading.
- `MainActivity`: Android TV UI composition and focus behavior.

## Backend Assumption

The app defaults to `http://10.0.2.2:5000` for emulator access to a backend running on the host machine. This can be changed through `BuildConfig.WATCHLIST_API_BASE_URL`.

## Testing

Unit tests cover JSON parsing and filter behavior. Manual/device verification is still needed for TV remote focus and real rendering.

## Known Limitations

- No local persistence.
- No image cache.
- No Android phone UI.
- No edit/add/remove flows.
