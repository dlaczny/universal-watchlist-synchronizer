# Android TV Client

The Android TV client lives in `android/` and currently targets the seeded backend API.

## Requirements

- Android Studio or Android SDK installed.
- Android SDK path in `android/local.properties`:

```properties
sdk.dir=C:\\Users\\laczn\\AppData\\Local\\Android\\Sdk
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
- Movies and TV Shows switch.
- All and Available switch.
- Focused detail panel with title, metadata, Plex availability, overview, and poster.
- Horizontal row of focusable watchlist items.
- Loading, empty, and backend error states.

## Limitations

- No Android phone UI.
- No local persistence or image cache.
- No edit/add/remove watchlist flows.
- No connected-device UI test yet.
