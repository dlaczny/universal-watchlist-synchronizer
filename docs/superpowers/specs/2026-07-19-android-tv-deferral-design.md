---
type: Design
title: Defer Android TV Integration Work
description: Removes Android TV integration work from the active TV plan until the user explicitly resumes it.
tags:
  - tv
  - android-tv
  - backlog
timestamp: 2026-07-19T00:00:00Z
version: 1.0.0
---

# Defer Android TV Integration Work

## Decision

Android TV work is deferred. It must not be started or resumed unless the user
explicitly asks for it in a future request.

## Active Scope

The active TV plan continues with backend read APIs, worker/export contracts,
server-side operational safeguards, and backend documentation. Android code,
Android-only fixtures and tests, Android CI, APK validation, and Android-only
documentation are excluded from active execution.

## Backlog Boundary

The deferred backlog retains the Android TV model parsing, browsing and detail
presentation, fixture integration, read-only transport checks, CI wiring, and
device/APK validation. Backend contracts remain versioned and independently
testable so that Android work can be resumed later without reopening backend
ownership boundaries.

## Resume Gate

Only an explicit user request may resume any deferred Android TV item. A
generic request to continue TV integration does not authorize Android work.

## Validation

The active release gate excludes Android Gradle, APK, Android fixture, and
Android-source secret scans. Those checks remain listed with the deferred
Android backlog and become required again when the user explicitly resumes
Android TV work.
