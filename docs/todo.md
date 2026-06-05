# Backlog

## Android TV

- [x] Redesign the Android TV controls for a remote-first experience.
  - Replace the featured-detail-row prototype with an Apple TV-inspired poster grid.
  - Add top navigation for All, Movies, TV Shows, and a disabled search icon.
  - Add a collection toolbar with Date added / A-Z sorting and a filter icon.
  - Add a compact availability overlay with On Plex and Unavailable checkboxes.
  - Restore the last selection, sort mode, filter state, and focused item where possible.
  - Keep D-pad navigation predictable and focused elements visually obvious.
  - Verify the complete browse flow with only a TV remote.
- [ ] Extract `MainActivity` responsibilities into focused Android TV components.
- [ ] Add focused automated coverage for loader generation and activity lifecycle state restoration.

## Backend API Follow-ups

- [x] Support a combined movie and TV query for the Android TV `All` collection.
- [x] Add stable watchlist `addedAt` data to MongoDB documents and API DTOs.
- [x] Replace the single availability mode with source-aware multi-select filtering.
- [x] Add manual Letterboxd movie watchlist sync into MongoDB.
- [x] Add TMDB metadata enrichment for imported Letterboxd movies.
  - Use the Letterboxd proxy `id` as the TMDB movie id when it resolves, for example `1297842` -> `https://www.themoviedb.org/movie/1297842`.
  - Use `imdb_id` as a fallback/verification key when the direct TMDB id lookup is missing or ambiguous.
  - Cache poster, backdrop, overview, canonical title, release date/status, and useful matching metadata in MongoDB.
  - Cache TMDB watch-provider data for Poland and mark subscribed-service availability for HBO Max/Max, SkyShowtime, Crunchyroll, and Amazon Prime Video.
  - Mark `releasedOnVod` when TMDB watch providers show any stream, rent, or buy option in Poland or the US.
- [ ] Add Android TV badges for TMDB provider availability and `releasedOnVod`.
- [ ] Cache TMDB poster/backdrop image bytes locally instead of storing only TMDB image URLs.
- [ ] Refine TMDB subscribed-service matching with provider IDs after confirming provider names/IDs from live data.
- [ ] Add Plex availability matching for imported movies.
- [ ] Keep TV shows visually below movies in the Android TV collection while movies are the product focus.

