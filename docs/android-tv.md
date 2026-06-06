# Android TV Client

The Android TV client lives in `android/` and currently targets the seeded backend API.

## Requirements

- Android Studio or Android SDK installed.
- Java 17 or 21 for command-line Gradle builds. On this machine, Android Studio's bundled JDK works:

```powershell
$env:JAVA_HOME='C:\Program Files\Android\Android Studio\jbr'
$env:Path="$env:JAVA_HOME\bin;$env:Path"
```

- Android SDK path in `android/local.properties`:

```properties
sdk.dir=C\:/Users/laczn/AppData/Local/Android/Sdk
```

`local.properties` is ignored by git.

## Backend

The app defaults to:

```text
http://10.0.2.2:5000
```

That address lets an Android emulator reach a backend running on the host machine. To run the backend for local testing:

```powershell
dotnet run --project backend\src\Watchlist.Api\Watchlist.Api.csproj --urls http://localhost:5000
```

To use a different backend URL, update `WATCHLIST_API_BASE_URL` in `android/app/build.gradle`.

## Build And Test

From the repository root:

```powershell
android\gradlew.bat -p android :app:testDebugUnitTest --no-daemon
android\gradlew.bat -p android :app:assembleDebug --no-daemon
```

The debug APK is produced under:

```text
android/app/build/outputs/apk/debug/
```

## Current UX

- Android TV launcher activity.
- Remote-first poster grid with artwork, title, and availability badge on each tile.
- Non-Plex movies with `vodReleaseKnown=true` and `releasedOnVod=false` render a `Not released` badge, based on backend TMDB watch-provider data for Poland and the US.
- Non-Plex movies with `ownedServiceAvailability` entries render a provider badge such as `Prime`, `Max`, `SkyShowtime`, `Crunchyroll`, or `Max +1`.
- Persistent Plex-inspired left rail with `All`, `Movies`, `TV Shows`, `On Plex`, `Unavailable`, and disabled search.
- Main content header with the active collection title, item count, and `Date added` / `A-Z` sort controls.
- Configurable poster grid density through `WATCHLIST_GRID_COLUMNS` in `android/app/build.gradle`; the default TV value is `7`.
- `SharedPreferences` restore for the selected collection, sort mode, unavailable filter, and the last focused item where possible.
- Predictable D-pad focus movement between the left rail, sort controls, and poster grid.
- Loading, empty, and backend error states.
- Pressing Select on a focused poster opens a Plex-like detail screen with backdrop, poster, metadata, description, and a state-aware primary action button.
- The details screen renders from grid data immediately, then refreshes from `GET /api/watchlist/{id}`.

`Date added` uses the backend `addedAt` field and asks the API for `sort=added_desc`.

## Manual Remote Test

Run the backend and launch the debug build on an Android TV emulator or device. Complete this flow using only D-pad navigation, Select, and Back:

1. Confirm focus starts in a usable location and moves predictably through the left rail, sort controls, and poster grid.
2. Confirm `All`, `Movies`, and `TV Shows` change the collection from the left rail.
3. Confirm `Date added` and `A-Z` reload the collection from the backend with the selected sort.
4. Confirm `On Plex` remains selected in the rail and `Unavailable` toggles unavailable, unreleased, and uncertain-match items.
5. From a poster in the first grid column, press Left and confirm focus moves into the rail.
6. From the rail, press Right and confirm focus returns to the last focused poster where possible.
7. Confirm more than one poster row is visible on a common TV viewport with the default grid density.
8. Focus a poster and press Select. Confirm the detail screen opens, the primary action button receives focus, and Back returns to the grid with the poster focus restored.
9. Confirm missing detail metadata is hidden and missing artwork uses the neutral fallback.

## Startup Availability Refresh

On app open, Android TV loads cached watchlist data first. After the first successful render it calls `POST /api/sync/availability/refresh` in the background.

- `ranPlexSync = false`: keep the current grid.
- `ranPlexSync = true`: reload the current watchlist query once.
- Refresh failure: keep cached data visible.

Manual test:

1. Start MongoDB and the backend.
2. Open the Android TV app.
3. Confirm posters render before Plex refresh needs to complete.
4. Run `Invoke-RestMethod http://localhost:5000/api/sync/status` and confirm Plex sync state after refresh when stale or missing.
5. Temporarily stop Plex or remove local Plex credentials, restart the backend, and confirm cached watchlist data still renders if `GET /api/watchlist` succeeds.

## Limitations

- No Android phone UI.
- No image cache.
- No edit/add/remove watchlist flows.
- No connected-device UI test yet.
