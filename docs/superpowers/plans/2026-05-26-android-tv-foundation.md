# Android TV Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first read-only Android TV client that consumes the seeded backend API and renders a Plex-like Featured Detail Row.

**Architecture:** Add an `android/` Gradle project with one `app` module. Keep business parsing/filtering in testable Java classes and keep Android-specific rendering inside `MainActivity`.

**Tech Stack:** Android Gradle Plugin 9.2.0, Gradle 9.4.1, Java 17, Android SDK 36, JUnit 4.

---

## Tasks

### Task 1: Android Build Skeleton

Create `android/settings.gradle`, `android/build.gradle`, `android/gradle.properties`, `android/app/build.gradle`, `android/app/src/main/AndroidManifest.xml`, and `local.properties` locally. Generate Gradle wrapper files. Verify `android/gradlew.bat -p android :app:tasks` works.

### Task 2: Testable Client Model And Filters

Create `WatchlistItem`, `WatchlistFilters`, and unit tests. Tests must fail before implementation and then pass. Cover media type filtering and available-only filtering.

### Task 3: Backend JSON Client

Create `WatchlistApiClient` with JSON parsing and HTTP fetch methods. Add unit tests for parsing `/api/watchlist` JSON and `/api/sync/status` JSON.

### Task 4: Android TV UI

Create `MainActivity`, `WatchlistConfig`, and `RemoteImageLoader`. Render Movies/TV and All/Available controls, focused detail panel, and horizontal focusable row. Fetch data from the backend on a background executor and show loading/error/empty states.

### Task 5: Verify And Document

Run Android unit tests and backend tests. Add `docs/android-tv.md` with build/run instructions, backend URL configuration, and current limitations.

## Self-Review

- Scope maps to the approved Android TV foundation slice.
- No edit flows, phone UI, persistence, or live sync work is included.
- The plan is intentionally lean because the repo currently has no Android project.
