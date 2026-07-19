---
type: Backlog
title: Android TV Integration Backlog
description: Deferred Android TV integration work that requires explicit user approval to resume.
tags:
  - backlog
  - android-tv
  - tv
timestamp: 2026-07-19T00:00:00Z
version: 1.1.0
---

# Android TV Integration Backlog

> **Hard gate:** Do not start or resume any item in this document unless the
> user explicitly asks to resume Android TV work. A generic request to continue
> TV integration does not satisfy this gate.

This backlog is the durable execution record for Android-specific work removed
from the active Phase 1 plan. Backend, worker/export, deploy, non-Android CI,
and general OKF work remain in that active plan.

## Lock Backend And Android To Shared Versioned Fixtures (Former Task 12)

The original task also included backend and worker fixture work. Those portions
remain in the active plan; this deferred section contains only the shared
fixture assets and Android-client consumption work.

**Files:**

- Create: `contracts/tv/watchlist-browse-v1.json`
- Create: `contracts/tv/watchlist-detail-v1.json`
- Create: `contracts/tv/enums-v1.json`

1. Commit canonical, comment-free browse and detail payloads. The browse
   payload must include the `tv-trakt-12345` show, TV lifecycle/progress,
   `nextEpisode`, `seasonCleanupPending`, `plexAvailability`, Poland provider
   offers, and relevant-season availability. The detail payload must preserve
   the same common summary and add nullable movie metadata, the disabled
   primary action, `lastWatchedEpisode`, destinations, and ordered seasons.
2. Commit `enums-v1.json` with contract version 1 and the original lifecycle,
   lifecycle-event, identity, provider-state, provider-category, and
   destination-state arrays. The Android client consumes these exact assets;
   it must not fork them into Android-only fixture copies.
3. Preserve the Android fixture commit boundary:

   ```powershell
   git add contracts/tv
   git commit -m "test: lock TV client contract fixtures"
   ```

## Parse The Shared TV Contract In Android And Remove Client Writes (Former Task 13)

**Files:**

- Create: `android/app/src/main/java/com/watchlist/tv/TvEpisodeProgress.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvProviderOffer.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvProviderAvailability.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvBrowseSummary.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvSeasonProgress.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvDestinationSummary.java`
- Create: `android/app/src/main/java/com/watchlist/tv/TvDetails.java`
- Create: `android/app/src/test/java/com/watchlist/tv/TvContractFixtureTest.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistItem.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistItemDetails.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/WatchlistApiClient.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/BrowsingState.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`
- Modify: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`
- Modify: `android/app/src/test/java/com/watchlist/tv/WatchlistItemDetailsTest.java`
- Modify: `android/app/src/test/java/com/watchlist/tv/BrowsingStateTest.java`
- Modify: `android/app/build.gradle`
- Delete: `android/app/src/main/java/com/watchlist/tv/AvailabilityRefreshResult.java`

1. Add this test resource source set inside the existing `android {}` block:

   ```groovy
   sourceSets {
       test.resources.srcDir rootProject.file("../contracts")
   }
   ```

   `TvContractFixtureTest` loads `/tv/watchlist-browse-v1.json`,
   `/tv/watchlist-detail-v1.json`, and `/tv/enums-v1.json` through the test
   class loader; never copy those payloads into an Android-only fixture.
2. Before production Java changes, test parsing of contract version, lifecycle,
   watchlist/progress, next episode, cleanup state, Plex availability, and
   provider data; ordered nullable detail seasons/episodes; immutable defensive
   lists; unknown raw enum values; and the exact active, caught-up, and retired
   TV URLs. Test that movie/all requests omit `state`, and that no model exposes
   editable integration credentials, destination mutation fields, cleanup
   authorizations, or `mutationCapable`.
3. Keep `TV_STATE_ACTIVE`, `TV_STATE_CAUGHT_UP`, and `TV_STATE_RETIRED` as
   `BrowsingState` constants. Defaults and every copy method retain the TV
   state; `withTvState` retains every other field.
4. Run the original red command from `android`:

   ```powershell
   ./gradlew.bat testDebugUnitTest --tests "com.watchlist.tv.TvContractFixtureTest" --tests "com.watchlist.tv.WatchlistApiClientTest" --tests "com.watchlist.tv.BrowsingStateTest"
   ```

   Expect missing value-object/`withTvState` compilation failures before the
   additive models exist, then parser and URL failures.
5. Implement final `Serializable` value objects with validated constructors,
   final fields/accessors, and `List.copyOf`. Add nullable `TvBrowseSummary tv()`
   and `TvDetails tv()` overloads while keeping movie constructors compatible.
   Preserve JSON names, nullable behavior, server offer order, and delivered
   numeric season/episode order; the client must not infer watched status or
   availability.
6. Change `getWatchlist` and `buildWatchlistPath` to accept `tvState`, append
   `state` only for `collection=tv`, encode values with `URLEncoder`, retain
   availability behavior, and delete refresh/post transport paths. Test that
   public network methods are GET-backed and source contains neither
   `setRequestMethod("POST")` nor `/api/sync/`.
7. Run:

   ```powershell
   ./gradlew.bat testDebugUnitTest --tests "com.watchlist.tv.*"
   ```

   Acceptance: all unit tests, unchanged movie parser tests, and three shared
   fixture tests pass.
8. Preserve the original commit boundary:

   ```powershell
   git add android contracts/tv
   git commit -m "feat: parse TV read models on Android"
   ```

## Present TV Lifecycle, Progress, And Polish Providers On Android TV (Former Task 14)

**Files:**

- Create: `android/app/src/main/java/com/watchlist/tv/TvPresentation.java`
- Create: `android/app/src/test/java/com/watchlist/tv/TvPresentationTest.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/MainActivity.java`
- Modify: `android/app/src/main/java/com/watchlist/tv/DetailsActivity.java`
- Modify: `android/app/src/main/res/values/strings.xml`
- Modify: `android/app/src/test/java/com/watchlist/tv/BrowsingStateTest.java`
- Modify: `android/app/src/test/java/com/watchlist/tv/WatchlistApiClientTest.java`

1. Write `TvPresentation` tests for exact lifecycle labels, `12/18 aired
   watched`, date-known and date-unknown next episodes, no-next-episode,
   Poland streaming availability, confirmed unavailability, unknown/stale data,
   and the explicit Phase 1 destination-unknown messages. Test separate rent
   and buy labels, link rendering only when supplied, and no conversion of
   unknown/stale into unavailable.
2. Run:

   ```powershell
   Set-Location android
   ./gradlew.bat testDebugUnitTest --tests "com.watchlist.tv.TvPresentationTest"
   Set-Location ..
   ```

   Initially expect compilation failure because `TvPresentation` is absent.
3. Implement only deterministic, static presentation methods over parsed
   models and `Locale`; no HTTP, JSON, credentials, or lifecycle computation.
   Format calendar dates in `Europe/Warsaw`, show provider freshness, and use a
   neutral `Data unavailable` line for a missing TV block.
4. Show `Watching | Caught up | Retired` only in TV browsing, mapping to
   `active`, `caught_up`, and `retired`. Preserve the selected TV state when
   movies/all is selected; issue a GET and restore focus for a TV-state change.
5. Show compact TV lifecycle/progress/next/provider data in TV cards without
   changing movie cards, grid width, focus, or ellipsize behavior. Render detail
   sections in order: Progress, Next episode, Seasons, Where to watch in
   Poland, Destinations, and `Data source: Trakt; provider data: TMDB /
   JustWatch`. Preserve movie routing/actions, required attribution, and make
   cleanup state informational only.
6. Run:

   ```powershell
   Set-Location android
   ./gradlew.bat testDebugUnitTest
   ./gradlew.bat assembleDebug
   Set-Location ..
   ```

   Require the debug APK at
   `android/app/build/outputs/apk/debug/app-debug.apk`.
7. On a seeded backend, verify D-pad focus has no trap, state refresh restores
   focus, detail Back returns to the card, unknown/stale/confirmed-unavailable
   remain distinct, no write controls appear, and access logs contain only GET
   browse/detail/status requests. Preserve the original commit boundary:

   ```powershell
   git add android
   git commit -m "feat: present TV progress on Android"
   ```

## Android CI, Gradle, APK, And Release Checks

The original tasks also cover backend, worker, deployment, and general release
work. Those portions remain active; these sections retain only Android CI and
APK gates.

### Mount The Keyring, Lock Mutation Gates, And Extend CI (Former Task 15: Android CI Only)

**Files:**

- Modify: `.github/workflows/android-ci.yml`

1. In `android-ci.yml`, include `contracts/tv/**` and Android sources in path
   filters, then run `testDebugUnitTest` plus `assembleDebug`. Preserve native
   Bash equivalents and the existing secret-safe workflow behavior.
2. Retain the Android CI portion of the original Task 15 commit boundary:

   ```powershell
   git add .github/workflows/android-ci.yml
   git commit -m "ops: deploy TV read model safely"
   ```
### Run The Phase 1 Release Gate And Record Evidence (Former Task 17: Android Release Only)

**Files:**

- Generated: `android/app/build/outputs/apk/debug/app-debug.apk`

1. The Android portion of the release gate includes:

   ```powershell
   Set-Location android
   ./gradlew.bat clean testDebugUnitTest assembleDebug
   Set-Location ..
   ```

   Require shared fixture parsing and
   `android/app/build/outputs/apk/debug/app-debug.apk`.
2. Restore the Android portions of the original release secret/write-surface
   checks:

   ```powershell
   rg -n -i "clientSecret|accessToken|refreshToken|protectedAccessToken|protectedRefreshToken|plexToken|sonarrApiKey" contracts android docs/reports
   rg -n "setRequestMethod\(\"POST\"\)|/api/sync/" android/app/src/main
   ```

   Require redacted names only and no POST/sync transport match. Final release
   status must exclude generated APKs and other build artifacts.

## Make The Phase 1 Operational Contract Discoverable In OKF (Former Task 16)

**Files:**

- Modify: `docs/systems/android_tv_client.md`
- Modify: `docs/decisions/android_tv_read_only_v1.md`

1. Document the client boundary: unauthenticated integration reads only, no
   third-party secrets, and no ownership of TV synchronization.
2. Link the client system and read-only decision from the applicable system and
   decision indexes. Keep this limited to Android-specific documentation.
3. Add the rollout-evidence row `Android shared fixtures and read-only
   transport passed` only after the deferred fixture and transport gates pass.
4. Retain the Task 16 knowledge-layer commit boundary:

   ```powershell
   git add docs
   git commit -m "docs: document TV Phase 1 operations"
   ```
