# AGENTS.md

## Project

This repository contains a personal watchlist application for Android TV first, with Android phone support planned later. The app shows movies and TV shows the user wants to watch and clearly marks what is available on the user's Plex server.

Version 1 is read-only from the client UI. Do not add create, edit, delete, reorder, or watchlist mutation flows unless the product scope changes explicitly.

## Repository Layout

- `backend/`: .NET backend service. Owns external integrations, sync jobs, MongoDB persistence, Plex matching, and read-only API endpoints.
- `android/`: Android clients. Android TV is first priority; Android phone comes later.
- `docs/`: Product, architecture, integration notes, API contracts, and decision records.

If these folders do not exist yet, create them only when there is implementation work for that area.

## Architecture Rules

- Android clients must call only the backend API.
- Android clients must not call Letterboxd, TMDB, Plex, or MongoDB directly.
- Backend is the only place for integration credentials, API tokens, sync logic, caching, and matching logic.
- MongoDB stores the normalized read model used by clients so browsing does not depend on live third-party calls.
- Letterboxd and TMDB define what the user wants to watch.
- Plex defines what is available now.

## Product Rules

- Movies watchlist source of truth: Letterboxd.
- TV watchlist source of truth: TMDB account watchlist.
- Availability v1: available on the user's Plex server.
- Streaming provider availability is a later extension, not part of v1.
- Android TV UI direction: Plex-like Featured Detail Row.
- Primary filters: Movies vs TV Shows, All vs Available.
- Treat unreleased items, missing Plex items, and uncertain Plex matches as distinct states.

## Backend Guidance

- Prefer idiomatic .NET and C# patterns already present in the codebase.
- Keep integration clients, sync orchestration, matching, persistence, and API models separated.
- Model sync as repeatable and idempotent.
- Store enough sync metadata to explain stale data, failed integrations, and match confidence.
- Do not hide uncertain Plex matches as unavailable; represent them explicitly.

## Android Guidance

- Optimize Android TV first: remote navigation, clear focus states, readable type, and predictable directional movement.
- Keep Android UI read-only in v1.
- Use backend DTOs as the client contract and avoid duplicating integration-specific models in the Android app.
- Design for poster/backdrop loading failures and backend offline states.

## Testing Expectations

- Backend changes should include focused unit tests for sync normalization, matching, and API behavior.
- Integration code should be testable behind interfaces or adapters.
- Android TV UI changes should be checked for focus navigation and common TV viewport behavior.
- Prefer deterministic fixtures over live external API calls in automated tests.

## Documentation Expectations

- Update `docs/architecture.md` when boundaries or data flow change.
- Update `docs/integrations.md` when an external API assumption changes.
- Add decision records for major technology, API, or data model changes.
