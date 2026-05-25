# Watchlist App Product Brief

## Goal

Build a personal watchlist application for movies and TV shows. The first client is Android TV. Android phone support can follow after the TV experience and backend contract are stable.

The app should feel close to Plex browsing: fast, visual, remote-friendly, and centered on deciding what to watch.

## Version 1 Scope

- Read-only Android TV app.
- Browse movies and TV shows.
- Switch between Movies and TV Shows.
- Switch between All and Available.
- Show availability based on the user's Plex server.
- Use a Featured Detail Row layout: focused item details plus horizontal poster rows.
- Use backend-provided metadata, artwork, availability, and sync status.

## Sources Of Truth

- Movies the user wants to watch: Letterboxd watchlist.
- TV shows the user wants to watch: TMDB account watchlist.
- Availability: user's Plex server.
- Metadata and artwork: TMDB.

Letterboxd and TMDB decide what belongs on the watchlist. Plex decides whether it is watchable now.

## Non-Goals For Version 1

- Editing the watchlist from Android TV.
- Adding, removing, or reordering items from the app.
- Streaming-provider availability such as Netflix or other services.
- Direct Android calls to Letterboxd, TMDB, Plex, or MongoDB.
- Multi-user household accounts.

## UX Principles

- The TV UI should be usable with a simple remote.
- Focus state must always be obvious.
- The Available filter should answer: "Can I watch this from my Plex server now?"
- Unreleased, unavailable, and uncertain-match states should be understandable.
- Browsing should remain usable when external services are temporarily unavailable, using the latest synced backend data.
