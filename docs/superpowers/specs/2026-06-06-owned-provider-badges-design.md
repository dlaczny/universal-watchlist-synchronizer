# Owned Provider Badges Design

## Goal

Show a compact Android TV badge when a watchlist movie is available on one of the user's owned streaming services, using cached TMDB watch-provider enrichment data from the backend.

This is a read-only display feature. It does not add provider filters, watchlist mutations, or live Android calls to TMDB.

## Context

The backend already stores TMDB watch-provider data in MongoDB and computes `OwnedServiceAvailability` from Poland flatrate providers. The Android card currently shows one bottom availability badge such as `On Plex`, `Not released`, or `Unavailable`.

Plex remains the preferred availability source. Owned-service availability should only become the badge when the movie is not on Plex.

## Recommended Approach

Keep one compact bottom badge on each poster card. Treat it as the answer to: "where can I watch this?"

Badge priority:

1. `On Plex`
2. Owned streaming provider, for example `Prime`, `Max`, `SkyShowtime`, or `Crunchyroll`
3. `Not released`
4. `Unreleased`
5. `Match uncertain`
6. `Unavailable`

This avoids crowded poster cards and avoids contradictory labels such as showing both `Unavailable` and `Prime`.

## Backend Contract

Add `ownedServiceAvailability: string[]` to `WatchlistItemDto` and the JSON returned by:

- `GET /api/watchlist`
- `GET /api/watchlist/{id}`

The field is sourced from MongoDB `OwnedServiceAvailability`.

Rules:

- Only Poland (`PL`) flatrate providers count for owned-service availability.
- Rent and buy providers do not count as owned-service availability.
- Provider-name matching remains the current implementation for this slice.
- Provider-ID refinement remains a later backlog item.
- Empty or missing provider data returns an empty array.

## Android Behavior

Android parses `ownedServiceAvailability` into `WatchlistItem`.

Badge label formatting:

- `Amazon Prime Video` and `Prime Video` -> `Prime`
- `Max` and `HBO Max` -> `Max`
- `SkyShowtime` -> `SkyShowtime`
- `Crunchyroll` -> `Crunchyroll`
- Unknown provider names use the original provider name if it fits the badge.

Multiple providers:

- If two providers match, show the first short label plus `+1`, for example `Max +1`.
- If more than two providers match and the first label would be too long, use a count such as `3 services`.

The badge remains a single line with ellipsizing. Provider badges use a distinct calm blue/teal background rather than Plex green or provider-branded colors.

## Out Of Scope

- Provider-specific filters in the availability popup.
- Provider logos or TMDB provider logo images.
- Android direct calls to TMDB.
- Provider-ID based matching.
- Displaying rent/buy provider badges.
- Phone UI.

## Testing

Backend:

- Query-service tests prove `OwnedServiceAvailability` maps from domain to DTO.
- Mongo document tests prove stored `OwnedServiceAvailability` maps into the domain read model.
- API tests prove watchlist JSON exposes `ownedServiceAvailability`.

Android:

- API client tests prove `ownedServiceAvailability` parses from JSON and defaults to an empty list for older responses.
- Badge formatter tests prove priority:
  - Plex beats provider.
  - Provider beats `Not released`.
  - Unknown or empty providers fall back to existing availability labels.
- Multi-provider tests prove compact labels such as `Max +1`.

## Documentation

Update:

- `docs/api.md` with the new DTO field.
- `docs/android-tv.md` with provider badge behavior.
- `docs/architecture.md` or `docs/integrations.md` if the read-model contract description changes.
- `docs/todo.md` to mark Android provider badges complete after implementation.
