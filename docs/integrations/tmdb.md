---
type: Integration
title: TMDB
description: Exact-ID movie/TV metadata, artwork, and Poland watch-provider observations.
tags:
  - tmdb
  - metadata
  - tv
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Purpose

TMDB provides movie and TV metadata, artwork, and watch-provider data. It is
not the Phase 1 TV watchlist source; Trakt owns TV membership and progress.

# Configuration

| Setting | Meaning |
|---|---|
| `Tmdb:AccessToken` / `TMDB__AccessToken` | TMDB v4 read token. |
| `Tmdb:BaseUrl` | Defaults to `https://api.themoviedb.org/3`. |
| `Tmdb:ImageBaseUrl` | Defaults to `https://image.tmdb.org/t/p`. |
| `Tmdb:ProviderRegion` | TV provider region; Phase 1 default is `PL`. |
| `Tmdb:OwnedProviderIds` | Stable subscribed provider IDs; never provider-name authority. |
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

# TV Enrichment

The backend enriches a Trakt show only through its exact TMDB ID. It reads show
metadata and PL watch providers, then relevant numbered-season provider data.
Provider observations use `available`, `confirmed_unavailable`, `unknown`, or
`stale`. A TMDB failure retains a prior usable observation as `stale` when
possible, otherwise publishes `unknown`; it never asserts unavailable from a
failed request. `POST /api/sync/tmdb/tv` intentionally returns `410 Gone` and
does not call the former account-watchlist route.

Plex TV inventory matching remains unimplemented in Phase 1.

# Links

- Data model: [TV Show](../data_models/tv_show.md)
- Source: [Trakt](trakt.md)
- Integration: [Plex](plex.md)
- Backlog: [Roadmap](../backlog/roadmap.md)

