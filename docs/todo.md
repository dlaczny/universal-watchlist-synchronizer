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

- [ ] Support a combined movie and TV query for the Android TV `All` collection.
- [ ] Add stable watchlist `addedAt` data to MongoDB documents and API DTOs.
- [ ] Replace the single availability mode with source-aware multi-select filtering.
- [ ] Add subscribed streaming-service availability later.

