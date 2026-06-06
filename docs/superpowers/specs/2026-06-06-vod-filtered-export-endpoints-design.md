# VOD-Filtered Export Endpoints Design

## Goal

Add backend export endpoints that return import-friendly watchlist lists for external automation tools. The movie endpoint should mirror the Letterboxd proxy item shape while excluding movies that are already available on the user's subscribed VOD services. A TV endpoint should exist with the same product intent, even though TV watchlist sync is not implemented yet.

The app is still local-only and not deployed, so breaking API changes are acceptable when code, tests, and documentation are updated together.

## Non-Goals

- Do not change Android TV behavior.
- Do not mutate the watchlist.
- Do not call Letterboxd, TMDB, Plex, or any external service during export requests.
- Do not implement TV watchlist sync in this work.
- Do not use Plex availability as an exclusion rule for this endpoint.

## Endpoints

### `GET /api/export/radarr/movies`

Returns Letterboxd watchlist movies that are not already available on the user's subscribed VOD services.

The response shape matches the Letterboxd proxy / Radarr-style JSON:

```json
[
  {
    "id": 1297842,
    "imdb_id": "tt27613895",
    "title": "GOAT",
    "release_year": "2026",
    "clean_title": "/film/goat-2026/",
    "adult": false
  }
]
```

Inclusion rules:

- Item is a movie.
- Item source is Letterboxd.
- Item is still present in the local watchlist read model.
- `OwnedServiceAvailability` is empty.

Exclusion rules:

- `OwnedServiceAvailability` contains at least one subscribed VOD provider from the existing TMDB enrichment rules.
- Non-movie items are excluded.
- Non-Letterboxd movie sources are excluded.

The endpoint must use cached MongoDB data only. If TMDB enrichment has not run for a movie, the movie is included because there is no cached evidence that it is already available on a subscribed service.

### `GET /api/export/sonarr/tv`

Returns TV shows that are not already available on the user's subscribed VOD services.

For the first implementation this endpoint returns an empty JSON array:

```json
[]
```

The endpoint exists now to reserve the contract and allow future integration wiring. When TMDB TV watchlist sync is added, this endpoint should be filled in with a Sonarr-friendly shape after confirming the expected import format.

## Data Mapping

Movie export fields come from cached MongoDB watchlist documents:

- `id`: Letterboxd/TMDB numeric source ID parsed from the Letterboxd proxy `id`.
- `imdb_id`: stored Letterboxd IMDb ID.
- `title`: watchlist title.
- `release_year`: watchlist year formatted as a string, or an empty string when unknown.
- `clean_title`: stored Letterboxd path from `clean_title`.
- `adult`: `false` for now because the current imported read model does not persist adult content as a first-class field.

If a stored source ID cannot be parsed to an integer, the item should be skipped rather than returning malformed Radarr-style data.

## Subscribed VOD Definition

Subscribed VOD filtering reuses the existing TMDB enrichment rule:

- Region: Poland (`PL`)
- Provider group: `flatrate`
- Provider names matched case-insensitively:
  - `Max`
  - `HBO Max`
  - `SkyShowtime`
  - `Crunchyroll`
  - `Amazon Prime Video`
  - `Prime Video`

Rent and buy providers do not count as subscribed VOD availability for this export endpoint.

## Error Handling

- MongoDB unavailable: return the same `503 Service Unavailable` dependency response used by other Mongo-backed endpoints.
- Missing TMDB enrichment: include the movie.
- Missing optional fields:
  - `imdb_id`: return an empty string.
  - `release_year`: return an empty string.
  - `clean_title`: return an empty string only if the original Letterboxd path is unavailable.

## Testing

Backend tests should cover:

- Movies with empty `OwnedServiceAvailability` are included.
- Movies with a subscribed VOD provider are excluded.
- Missing TMDB enrichment does not exclude a movie.
- TV export endpoint returns an empty list for v1.
- The movie response preserves the Letterboxd proxy field names and string year behavior.

## Documentation

Update:

- `docs/api.md` with both export endpoints.
- `docs/integrations.md` with the VOD-filtered export purpose and cached-data rule.
