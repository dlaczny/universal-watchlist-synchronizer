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
- Top navigation with enabled `All`, `Movies`, `TV Shows`, and a disabled search icon.
- Collection toolbar with backend-owned `Date added` and `A-Z` sorting plus an availability filter icon.
- Availability popup with an always-checked `On Plex` baseline and an `Unavailable` checkbox that requests `not_on_plex`, `unreleased`, and `unknown_match` from the backend.
- `SharedPreferences` restore for the selected collection, sort mode, availability filter, and the last focused item where possible.
- Predictable D-pad focus movement across navigation, toolbar controls, the popup, and poster grid.
- Back closes the availability popup first, then follows the normal Android activity flow.
- Loading, empty, and backend error states.

`Date added` uses the backend `addedAt` field and asks the API for `sort=added_desc`.

## Manual Remote Test

Run the backend and launch the debug build on an Android TV emulator or device. Complete this flow using only D-pad navigation, Select, and Back:

1. Confirm focus starts in a usable location and moves predictably through the top navigation, toolbar, and poster grid.
2. Confirm `All`, `Movies`, and `TV Shows` change the collection, while search is visible but disabled.
3. Confirm `Date added` and `A-Z` reload the collection from the backend with the selected sort.
4. Open the availability popup from the filter icon. Confirm `On Plex` remains checked, then toggle `Unavailable` and confirm unavailable, unreleased, and uncertain-match items are included or excluded from the grid.
5. Press Back while the popup is open. Confirm it closes without leaving the screen and focus returns to the filter icon.
6. Move focus to a poster, change collection, sort mode, and the `Unavailable` filter, then leave and relaunch the activity. Confirm the saved state and last focused item are restored where possible.
7. Confirm focus remains visually obvious and directional movement does not trap the user at grid or toolbar boundaries.

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
