# Watchlist App

A personal watchlist app for my own Android TV setup.

The app is built around one specific use case: I want a read-only TV interface that shows movies and TV shows I plan to watch, while clearly marking what is already available on my Plex server and my VOD services.

## What It Does

- Syncs movie watchlist data from Letterboxd.
- Syncs TV watchlist data from a TMDB account watchlist.
- Uses TMDB metadata and artwork for display data.
- Checks Plex and subscribed VOD services to mark where watchlist items are available.
- Serves a normalized read model from a .NET backend.
- Provides an Android TV-first client with a remote-friendly poster grid.

Version 1 is read-only from the client UI. There are no create, edit, delete, reorder, or watchlist mutation flows in the app.

## Repository Layout

- `backend/` - .NET backend service, integrations, sync jobs, Plex matching, MongoDB persistence, and read-only API endpoints.
- `android/` - Android client code, with Android TV as the first target.
- `docs/` - product notes, architecture notes, integration decisions, and implementation plans.

## Architecture

The Android app talks only to the backend API. The backend owns third-party integrations, credentials, sync orchestration, caching, matching, and persistence.

The main sources are:

- Letterboxd for the movie watchlist.
- TMDB for the TV watchlist and metadata.
- Plex and subscribed VOD services for availability.
- MongoDB for the normalized read model consumed by the client.

The app distinguishes between available items, unavailable items, unreleased items, subscribed-service availability, and uncertain Plex matches.

## Status

This is an active personal project and the API/client contract may change freely while it remains local-only. Documentation in `docs/` is more useful than this README for understanding design decisions in detail.

## Local Development

TBD: local setup instructions.

## AI Assistance

This codebase is written with AI assistance, primarily using ChatGPT 5.5 Codex and Deepseek V4 Flash in opencode.

I still treat the project as my responsibility: generated code is reviewed, changed, tested, and adapted for this specific personal use case.
