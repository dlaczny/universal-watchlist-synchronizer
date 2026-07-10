---
type: Integration
title: TMDB
description: Metadata, artwork, watch-provider data, and TV watchlist integration.
tags:
  - tmdb
  - metadata
  - tv
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Purpose

TMDB provides movie and TV metadata, artwork, watch-provider data, and the TV
watchlist source of truth.

# Configuration

| Setting | Meaning |
|---|---|
| `Tmdb:AccessToken` / `TMDB__AccessToken` | TMDB v4 read token. |
| `Tmdb:BaseUrl` | Defaults to `https://api.themoviedb.org/3`. |
| `Tmdb:ImageBaseUrl` | Defaults to `https://image.tmdb.org/t/p`. |
| `Tmdb:AccountId` / `TMDB__AccountId` | Account ID for TV watchlist sync. |
| `Tmdb:SessionId` / `TMDB__SessionId` | Session ID for TV watchlist sync. |
| `Tmdb:Language` | Defaults to `en-US`. |

# Movie Enrichment

- Try `/movie/{id}` using Letterboxd proxy `id` as a candidate TMDB movie ID.
- Fallback through `/find/{imdbId}?external_source=imdb_id`.
- Fetch movie details for title, original title, IMDb ID, overview, release
  date, genres, runtime, poster path, backdrop path, and vote data.
- Fetch `/movie/{id}/watch/providers`.
- Store provider groups by region in MongoDB.
- Expose artwork to Android through backend-relative image proxy URLs.
- Preserve existing metadata when a temporary TMDB failure occurs.

# Owned Service Rules

Owned subscribed-service availability currently uses Poland flatrate providers.
Provider names are matched case-insensitively against Max/HBO Max,
SkyShowtime, Crunchyroll, Amazon Prime Video, and Prime Video.

Rent and buy providers do not count as owned subscribed-service availability.
`releasedOnVod` is true when Poland or the US has at least one flatrate, rent,
or buy provider.

# TV Watchlist Sync

- Source: `/account/{account_id}/watchlist/tv`.
- Pages through all results.
- Fetches `/tv/{series_id}` and `/tv/{series_id}/external_ids`.
- Stores records as `MediaType.TvShow`, `Source.Tmdb`, and IDs matching
  `tv-tmdb-{tmdbId}`.
- Deletes removed TMDB TV watchlist items from the backend store.

# Current Gap

Plex TV inventory sync and availability matching are not implemented yet. TV
records can show `not_on_plex`, `unreleased`, or `unknown_match` until TV Plex
matching is added.

# Links

- Data model: [Watchlist Item](../data_models/watchlist_item.md)
- Integration: [Plex](plex.md)
- Backlog: [Roadmap](../backlog/roadmap.md)

