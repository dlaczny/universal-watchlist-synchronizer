# MongoDB Read Model Design

## Goal

Move the backend read model from process-local seeded memory to MongoDB while preserving the existing Android TV API contract. The service must remain immediately testable before live Letterboxd, TMDB, and Plex sync integrations are implemented.

## Scope

- Run MongoDB locally with Docker Compose and a persistent volume.
- Store normalized watchlist items in `watchlist_items`.
- Store the latest sync status in `sync_runs`.
- Bootstrap the existing sample items and one seeded sync run only when their collections are empty.
- Serve `/api/watchlist`, `/api/watchlist/{id}`, and `/api/sync/status` from MongoDB.
- Return `503 Service Unavailable` when MongoDB cannot be reached.
- Preserve the Android TV API DTOs and URLs.
- Record the Android TV remote-control redesign in the backlog.

Live Letterboxd, TMDB, and Plex integrations are explicitly out of scope for this slice.

## Runtime Architecture

`compose.yaml` runs one local MongoDB container with a named volume. The backend reads MongoDB settings from configuration and registers MongoDB-backed repositories. A hosted bootstrap service inserts deterministic seed records only when collections are empty.

The existing `IWatchlistReadRepository` remains the read boundary used by `WatchlistQueryService`. A new `ISyncStatusReadRepository` supplies `/api/sync/status`. MongoDB driver exceptions are translated at the HTTP boundary into a clear `503` response instead of silently falling back to in-memory data.

## Data Model

`watchlist_items` documents mirror the existing normalized domain record:

- `id`
- `mediaType`
- `source`
- `sourceId`
- `title`
- `year`
- `overview`
- `posterUrl`
- `backdropUrl`
- `releaseStatus`
- `availabilityStatus`
- `updatedAt`

`sync_runs` documents contain:

- `id`
- `status`
- `lastSuccessfulSyncAt`

The first bootstrapped sync record uses status `seeded`.

## Error Handling

MongoDB unavailability is a backend dependency outage. The API returns HTTP `503` with a small JSON error body. The service does not silently switch to in-memory data because that would hide persistence problems and make browsing behavior differ between environments.

## Testing

- Unit tests cover document-to-domain mapping and DI registration.
- API tests keep using deterministic seeded repositories unless they explicitly test MongoDB failure behavior.
- A MongoDB-backed integration verification starts Docker Compose, runs the backend, calls the read endpoints, and confirms bootstrapped data is returned.
- Existing Android TV behavior remains unchanged because the API contract is unchanged.

