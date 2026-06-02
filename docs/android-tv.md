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
- Top navigation with a disabled `All` collection, `Movies`, `TV Shows`, and a disabled search icon.
- Collection toolbar with `Date added` and `A-Z` sorting plus an availability filter icon.
- Availability popup with an always-checked `On Plex` baseline and an `Unavailable` checkbox.
- `SharedPreferences` restore for the selected collection, sort mode, availability filter, and the last focused item where possible.
- Predictable D-pad focus movement across navigation, toolbar controls, the popup, and poster grid.
- Back closes the availability popup first, then follows the normal Android activity flow.
- Loading, empty, and backend error states.

`Date added` currently preserves backend order. Stable watchlist `addedAt` data is a backend API follow-up.

## Manual Remote Test

Run the backend and launch the debug build on an Android TV emulator or device. Complete this flow using only D-pad navigation, Select, and Back:

1. Confirm focus starts in a usable location and moves predictably through the top navigation, toolbar, and poster grid.
2. Confirm `All` and search are visible but disabled, while `Movies` and `TV Shows` change the collection.
3. Confirm `Date added` preserves backend order and `A-Z` sorts the visible collection alphabetically.
4. Open the availability popup from the filter icon. Confirm `On Plex` remains checked, then toggle `Unavailable` and confirm unavailable items are included or excluded from the grid.
5. Press Back while the popup is open. Confirm it closes without leaving the screen and focus returns to the filter icon.
6. Move focus to a poster, change collection, sort mode, and the `Unavailable` filter, then leave and relaunch the activity. Confirm the saved state and last focused item are restored where possible.
7. Confirm focus remains visually obvious and directional movement does not trap the user at grid or toolbar boundaries.

## Limitations

- No Android phone UI.
- No image cache.
- No edit/add/remove watchlist flows.
- No connected-device UI test yet.
