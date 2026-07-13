---
type: Backlog
title: TV Phase 3 Reversible Destinations Implementation Plan
description: Test-first implementation plan for the report-only and reversible Sonarr and Plex universal-watchlist phase of Trakt-backed TV synchronization.
tags:
  - tv
  - trakt
  - sonarr
  - plex
  - worker
  - rollout
timestamp: 2026-07-13T00:00:00Z
version: 0.1.0
---

# TV Phase 3 Reversible Destinations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the report-only-first Python TV worker that strictly consumes one published backend generation, safely reconciles exact-TVDB Sonarr monitoring and exact-identity Plex universal-watchlist membership, and records explicit destination ownership without enabling any destructive cleanup.

**Architecture:** The backend remains authoritative for Trakt membership, progress, lifecycle, Plex-history health, and desired TV state. A separate TV worker path reads GET /api/export/tv/sync-state, collects live Sonarr and Plex universal-watchlist state, computes a deterministic pure plan, applies per-boundary safety policy, and optionally executes only reversible actions. SQLite stores the single-run lease, exact destination ownership/adoption, run audit, and reversible action audit; the existing movie pipeline remains unchanged.

**Tech Stack:** Python 3.11, dataclasses, enum, sqlite3, httpx 0.28, PlexAPI 4.18, pytest 8, Docker Compose, the existing watchlist-app sync-key boundary, Sonarr API v3.

---

## Scope And Hard Safety Boundary

This is the Phase 3 plan selected by the approved TV integration design. It
assumes Phase 1 has published the versioned TV export and Phase 2 has completed
the configured-account Plex history bootstrap sufficiently for a generation to
be mutation capable.

Phase 3 implements only:

- strict complete-generation export parsing;
- exact-TVDB Sonarr lookup, add, series/season monitoring, episode monitoring,
  and exact aired-unwatched episode search;
- exact external-identity Plex universal-watchlist add/remove;
- report-only adoption candidates and explicitly gated adoption;
- SQLite ownership, run lease, runs, observations, and reversible action audit;
- deterministic collection, planning, policy, execution, reporting, CLI, and
  independent continuous scheduling; and
- report-only visibility for cleanup authorization counts.

Phase 3 must not implement or call:

- either cleanup claim/result endpoint;
- DELETE /api/v3/episodefile/{id};
- DELETE /api/v3/series/{id};
- a Plex library mutation;
- a Sonarr SeriesSearch, SeasonSearch, or missingEpisodeSearch command;
- a Trakt endpoint; or
- MongoDB directly.

The configuration keys
TV_SYNC_ALLOW_SEASON_FILE_DELETION,
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION, and
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE are parsed with false defaults so the
deployment contract is forward compatible. Phase 3 validation rejects startup
if any of them is true. A later Phase 4 or Phase 5 plan must remove that
temporary rejection only after implementing the corresponding safety gates.

## Contract Fixed For This Plan

GET /api/export/tv/sync-state returns camelCase JSON with this version-1 shape:

~~~json
{
  "schemaVersion": "1",
  "generationId": "tv-generation-42",
  "publishedAt": "2026-07-13T12:00:00Z",
  "generatedAt": "2026-07-13T11:59:50Z",
  "kind": "scheduled_full",
  "mutationCapable": true,
  "healthReasons": [],
  "plexHistory": {
    "machineIdentifier": "plex-machine",
    "accountId": 17,
    "librarySectionId": "4",
    "librarySectionTitle": "TV Shows",
    "capable": true,
    "bootstrapComplete": true,
    "lastCollectionComplete": true,
    "lastCollectionSucceeded": true,
    "observedEventCount": 42,
    "pendingPostCutoverRoutes": 0,
    "collectedAt": "2026-07-13T11:58:00Z",
    "watermark": "2026-07-13T11:57:00Z"
  },
  "shows": [],
  "cleanupAuthorizations": []
}
~~~

Each show contains:

~~~text
traktId: positive integer
tvdbId: positive integer or null
tmdbId: positive integer or null
imdbId: non-empty string or null
title: non-empty string
year: integer or null
identityStatus: verified | missing | conflict | legacy_unresolved
inTraktWatchlist: boolean
lifecycleState: active | caught_up | source_removed |
                terminal_cleanup_pending | retired_terminal
lifecycleVersion: positive integer
traktStatus: non-empty string
aired: nonnegative integer
completed: nonnegative integer no greater than aired
lastWatchedEpisode: episode reference or null
nextEpisode: episode reference or null
sonarrDesired: boolean
sonarrMonitoredDesired: boolean
plexWatchlistDesired: boolean
seasons: array
polandAvailability: object
blockers: unique string array
~~~

Each season contains seasonNumber, aired, completed, monitoredDesired,
searchAiredUnwatchedEpisodes as an integer episode-number array, cleanupState,
and episodes. Each episode contains positive traktEpisodeId, seasonNumber,
episodeNumber, optional tvdbId, optional title, optional timezone-aware
firstAired, aired, watched,
optional lastWatchedAt, optional plexRatingKey,
optional watchedByConfiguredPlexAccount, and optional plexLastViewedAt.

Cleanup authorizations are parsed for report counts only. They contain eventId,
actionType season_files or terminal_series, Trakt/TVDB identities, optional
seasonNumber, lifecycleVersion, predicateHash, manifestId, authorizedAt,
expiresAt, expectedAired, expectedCompleted, plexEvidenceCollectedAt, and
plexHistoryWatermark. Every manifestId must equal the enclosing generationId.

Stable backend blocker strings include:

- identity_missing_tvdb
- identity_conflict
- trakt_outbox_unresolved
- plex_post_cutover_routing_unresolved
- plex_event_quarantined
- plex_history_unavailable
- plex_backfill_incomplete
- plex_evidence_stale
- explicit_trakt_watchlist
- trakt_next_episode_known

`plexHistory.collectedAt` may be null only on a non-mutation-capable generation
whose Plex history capability or bootstrap is incomplete. A mutation-capable
generation requires capable/bootstrap/last-collection complete and successful,
non-null configured Plex identity fields, non-null `collectedAt`, nonnegative
binding-wide `observedEventCount`, and zero `pendingPostCutoverRoutes`.
`plexHistory.watermark` is non-null unless the strict complete-successful empty
case has `observedEventCount == 0`, or the canonical unavailable/incomplete
fail-closed tuple has last-collection flags false, both counts zero, and null
collection time. A positive count requires a watermark. Any other null
combination or negative count rejects the snapshot. A positive
pending-route count is accepted only in a fail-closed envelope with
`mutationCapable=false` and the exact
`plex_post_cutover_routing_unresolved` health reason; zero routes requires that
reason absent. Mismatched combinations reject the snapshot.

POST /api/worker/tv/runs accepts this redacted Phase 3 summary:

~~~text
runId, workerId, generationId
mode: report_only | apply
startedAt, finishedAt
status
collections: per-boundary complete flag and count
gates: booleans, cleanup authorization counts, and fixed caps
actions: actionId, optional eventId, type, target, status, optional reason
blockers: stable string array
~~~

## File Structure

Create:

- workers/vod-filter/src/models/tv_sync.py — immutable backend contract models.
- workers/vod-filter/src/models/tv_destination.py — Sonarr, Plex, ownership,
  collected-state, decision, plan, policy, and execution models.
- workers/vod-filter/src/clients/tv_backend_client.py — strict export reader
  and redacted run-summary writer.
- workers/vod-filter/src/clients/sonarr_client.py — the reversible Sonarr v3
  surface only.
- workers/vod-filter/src/clients/plex_tv_client.py — Plex universal-watchlist
  show surface only.
- workers/vod-filter/src/models/migrations/0001_tv_worker_state.sql — additive
  TV SQLite schema.
- workers/vod-filter/src/services/tv_state_store.py — migrations, lease,
  ownership, run, observation, and action audit.
- workers/vod-filter/src/services/tv_sync_collector.py — side-effect-free
  boundary collection.
- workers/vod-filter/src/services/tv_sync_planner.py — pure deterministic
  reconciliation.
- workers/vod-filter/src/services/tv_sync_policy.py — snapshot, collection,
  apply, adoption, and destructive-disable gates.
- workers/vod-filter/src/services/tv_destination_executor.py — reversible
  destination execution and immediate audit.
- workers/vod-filter/src/services/tv_sync_report.py — redacted JSON/Markdown
  and backend summary creation.
- workers/vod-filter/sync_tv.py — single-run composition root.
- contracts/tv/worker-sync-state-v1.json — sole shared backend/Python worker
  contract fixture created in Phase 1.
- workers/vod-filter/tests/vod_filter/test_tv_backend_client.py
- workers/vod-filter/tests/vod_filter/test_tv_state_store.py
- workers/vod-filter/tests/vod_filter/test_sonarr_client.py
- workers/vod-filter/tests/vod_filter/test_plex_tv_client.py
- workers/vod-filter/tests/vod_filter/test_tv_sync_collector.py
- workers/vod-filter/tests/vod_filter/test_tv_sync_planner.py
- workers/vod-filter/tests/vod_filter/test_tv_sync_policy.py
- workers/vod-filter/tests/vod_filter/test_tv_destination_executor.py
- workers/vod-filter/tests/vod_filter/test_tv_sync_report.py
- workers/vod-filter/tests/vod_filter/test_sync_tv_cli.py
- workers/vod-filter/tests/vod_filter/test_tv_workflow_simulation.py

Modify:

- workers/vod-filter/src/config.py — conditional TV/Sonarr settings and
  permanently-false Phase 3 destructive gates.
- workers/vod-filter/continuous_sync.py — independent movie/TV deadlines.
- workers/vod-filter/healthcheck.py — workflow-aware heartbeat while preserving
  the existing heartbeat format.
- workers/vod-filter/Dockerfile — copy sync_tv.py.
- workers/vod-filter/example.env — non-secret TV examples.
- workers/vod-filter/docker-compose.yml — TV scheduling environment only; no
  TV media mount is needed before terminal cleanup.
- deploy/production/worker.env.example — disabled-by-default Phase 3 contract.
- deploy/production/compose.yaml — TV interval and worker identity wiring; no
  media-root bind mount in Phase 3.
- .github/workflows/movie-ci.yml — compile sync_tv.py.
- tests/deployment/test_deploy_script.py — preserve non-root/read-only
  deployment and assert destructive TV defaults remain false.

Do not modify:

- workers/vod-filter/src/clients/radarr_client.py
- workers/vod-filter/src/clients/plex_client.py
- workers/vod-filter/src/services/movie_sync_collector.py
- workers/vod-filter/src/services/movie_sync_policy.py
- workers/vod-filter/src/services/movie_sync_executor.py
- workers/vod-filter/src/services/sync_reconciliation.py
- workers/vod-filter/src/models/schema.sql

The TV store uses a separate additive migration so existing movie SQLite rows
and behavior remain unchanged.

### Task 1: Establish The Strict Version-1 TV Export Contract

**Files:**

- Fixture: contracts/tv/worker-sync-state-v1.json
- Create: workers/vod-filter/tests/vod_filter/test_tv_backend_client.py
- Create: workers/vod-filter/src/models/tv_sync.py
- Create: workers/vod-filter/src/clients/tv_backend_client.py

- [ ] **Step 1: Consume the complete shared fixture**

Use the root fixture with two shows: one active unfinished show and one
caught-up show. Do not copy it into the worker tree. The fixture is owned by
the backend serialization test and uses this exact contract:

~~~json
{
  "schemaVersion": "1",
  "generationId": "tv-generation-42",
  "publishedAt": "2026-07-13T12:00:00Z",
  "generatedAt": "2026-07-13T11:59:50Z",
  "kind": "scheduled_full",
  "mutationCapable": true,
  "healthReasons": [],
  "plexHistory": {
    "machineIdentifier": "plex-machine",
    "accountId": 17,
    "librarySectionId": "4",
    "librarySectionTitle": "TV Shows",
    "capable": true,
    "bootstrapComplete": true,
    "lastCollectionComplete": true,
    "lastCollectionSucceeded": true,
    "observedEventCount": 42,
    "pendingPostCutoverRoutes": 0,
    "collectedAt": "2026-07-13T11:58:00Z",
    "watermark": "2026-07-13T11:57:00Z"
  },
  "shows": [
    {
      "traktId": 101,
      "tvdbId": 7101,
      "tmdbId": 8101,
      "imdbId": "tt0007101",
      "title": "Weekly Show",
      "year": 2025,
      "identityStatus": "verified",
      "inTraktWatchlist": false,
      "lifecycleState": "active",
      "lifecycleVersion": 4,
      "traktStatus": "returning series",
      "aired": 3,
      "completed": 2,
      "lastWatchedEpisode": {
        "traktEpisodeId": 1102,
        "seasonNumber": 1,
        "episodeNumber": 2,
        "tvdbId": 9102,
        "title": "Second",
        "firstAired": "2026-07-06T18:00:00Z",
        "aired": true,
        "watched": true,
        "lastWatchedAt": "2026-07-07T20:00:00Z",
        "plexRatingKey": "episode-102",
        "watchedByConfiguredPlexAccount": true,
        "plexLastViewedAt": "2026-07-07T20:00:00Z"
      },
      "nextEpisode": null,
      "sonarrDesired": true,
      "sonarrMonitoredDesired": true,
      "plexWatchlistDesired": true,
      "seasons": [
        {
          "seasonNumber": 0,
          "aired": 0,
          "completed": 0,
          "monitoredDesired": false,
          "searchAiredUnwatchedEpisodes": [],
          "cleanupState": "none",
          "episodes": []
        },
        {
          "seasonNumber": 1,
          "aired": 3,
          "completed": 2,
          "monitoredDesired": true,
          "searchAiredUnwatchedEpisodes": [3],
          "cleanupState": "none",
          "episodes": [
            {
              "traktEpisodeId": 1103,
              "seasonNumber": 1,
              "episodeNumber": 3,
              "tvdbId": 9103,
              "title": "Third",
              "firstAired": "2026-07-12T18:00:00Z",
              "aired": true,
              "watched": false,
              "lastWatchedAt": null,
              "plexRatingKey": null,
              "watchedByConfiguredPlexAccount": false,
              "plexLastViewedAt": null
            }
          ]
        }
      ],
      "polandAvailability": {
        "state": "available",
        "region": "PL",
        "fetchedAt": "2026-07-13T11:45:00Z",
        "link": "https://www.themoviedb.org/tv/8101/watch",
        "offers": [
          {
            "providerId": 119,
            "providerName": "Prime Video",
            "category": "flatrate",
            "logoUrl": "/api/images/tmdb/w500/prime.jpg"
          }
        ]
      },
      "blockers": []
    },
    {
      "traktId": 102,
      "tvdbId": 7102,
      "tmdbId": 8102,
      "imdbId": "tt0007102",
      "title": "Caught Up Show",
      "year": 2024,
      "identityStatus": "verified",
      "inTraktWatchlist": false,
      "lifecycleState": "caught_up",
      "lifecycleVersion": 7,
      "traktStatus": "continuing",
      "aired": 8,
      "completed": 8,
      "lastWatchedEpisode": {
        "traktEpisodeId": 1208,
        "seasonNumber": 1,
        "episodeNumber": 8,
        "tvdbId": 9208,
        "title": "Finale",
        "firstAired": "2026-06-01T18:00:00Z",
        "aired": true,
        "watched": true,
        "lastWatchedAt": "2026-06-02T20:00:00Z",
        "plexRatingKey": "episode-208",
        "watchedByConfiguredPlexAccount": true,
        "plexLastViewedAt": "2026-06-02T20:00:00Z"
      },
      "nextEpisode": null,
      "sonarrDesired": true,
      "sonarrMonitoredDesired": true,
      "plexWatchlistDesired": false,
      "seasons": [],
      "polandAvailability": {
        "state": "confirmed_unavailable",
        "region": "PL",
        "fetchedAt": "2026-07-13T11:45:00Z",
        "link": "https://www.themoviedb.org/tv/8102/watch",
        "offers": []
      },
      "blockers": []
    }
  ],
  "cleanupAuthorizations": [
    {
      "eventId": "season-event-1",
      "actionType": "season_files",
      "traktId": 102,
      "tvdbId": 7102,
      "seasonNumber": 1,
      "lifecycleVersion": 7,
      "predicateHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      "manifestId": "tv-generation-42",
      "authorizedAt": "2026-07-13T11:55:00Z",
      "expiresAt": "2026-07-13T12:25:00Z",
      "expectedAired": 8,
      "expectedCompleted": 8,
      "plexEvidenceCollectedAt": "2026-07-13T11:54:00Z",
      "plexHistoryWatermark": "2026-07-13T11:53:00Z"
    },
    {
      "eventId": "terminal-event-1",
      "actionType": "terminal_series",
      "traktId": 102,
      "tvdbId": 7102,
      "seasonNumber": null,
      "lifecycleVersion": 7,
      "predicateHash": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
      "manifestId": "tv-generation-42",
      "authorizedAt": "2026-07-13T11:55:00Z",
      "expiresAt": "2026-07-13T12:25:00Z",
      "expectedAired": 8,
      "expectedCompleted": 8,
      "plexEvidenceCollectedAt": "2026-07-13T11:54:00Z",
      "plexHistoryWatermark": "2026-07-13T11:53:00Z"
    }
  ]
}
~~~

- [ ] **Step 2: Write failing happy-path and HTTP-boundary tests**

Add tests that load the fixture through httpx.MockTransport, assert the exact
GET path, and verify the mapped immutable values:

~~~python
def test_fetch_snapshot_maps_one_complete_generation(load_tv_fixture):
    payload = load_tv_fixture()

    def handler(request: httpx.Request) -> httpx.Response:
        assert request.method == "GET"
        assert request.url.path == "/api/export/tv/sync-state"
        return httpx.Response(200, json=payload)

    client = TvBackendClient(
        "http://watchlist-api:8080",
        http_client=httpx.Client(transport=httpx.MockTransport(handler)),
        sync_key="sync-key",
    )

    snapshot = client.fetch_sync_snapshot()

    assert snapshot.schema_version == "1"
    assert snapshot.generation_id == "tv-generation-42"
    assert snapshot.mutation_capable is True
    assert snapshot.plex_history.account_id == 17
    assert snapshot.plex_history.last_collection_complete is True
    assert snapshot.plex_history.last_collection_succeeded is True
    assert snapshot.plex_history.observed_event_count == 42
    assert snapshot.plex_history.pending_post_cutover_routes == 0
    assert [show.tvdb_id for show in snapshot.shows] == [7101, 7102]
    assert snapshot.shows[0].last_watched_episode.trakt_episode_id == 1102
    assert snapshot.shows[0].seasons[1].search_aired_unwatched_episodes == (3,)
    assert snapshot.shows[0].poland_availability.state.value == "available"
    assert snapshot.shows[0].poland_availability.offers[0].provider_id == 119
    assert [a.action_type.value for a in snapshot.cleanup_authorizations] == [
        "season_files",
        "terminal_series",
    ]


def test_fetch_snapshot_maps_http_failure_without_response_body():
    client = TvBackendClient(
        "http://watchlist-api:8080",
        http_client=httpx.Client(
            transport=httpx.MockTransport(
                lambda request: httpx.Response(
                    503,
                    json={"secret": "must-not-appear"},
                )
            )
        ),
        sync_key="sync-key",
    )

    with pytest.raises(TvBackendError, match="HTTP 503") as error:
        client.fetch_sync_snapshot()

    assert "must-not-appear" not in str(error.value)
~~~

- [ ] **Step 3: Run the focused tests and verify the missing-module failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_backend_client.py -q
Pop-Location
~~~

Expected: collection fails because src.models.tv_sync and
src.clients.tv_backend_client do not exist.

- [ ] **Step 4: Define the immutable contract types**

Implement these exact public types in tv_sync.py:

~~~python
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import Enum


class TvIdentityStatus(str, Enum):
    VERIFIED = "verified"
    MISSING = "missing"
    CONFLICT = "conflict"
    LEGACY_UNRESOLVED = "legacy_unresolved"


class TvLifecycleState(str, Enum):
    ACTIVE = "active"
    CAUGHT_UP = "caught_up"
    SOURCE_REMOVED = "source_removed"
    TERMINAL_CLEANUP_PENDING = "terminal_cleanup_pending"
    RETIRED_TERMINAL = "retired_terminal"


class TvCleanupActionType(str, Enum):
    SEASON_FILES = "season_files"
    TERMINAL_SERIES = "terminal_series"


class TvProviderState(str, Enum):
    AVAILABLE = "available"
    CONFIRMED_UNAVAILABLE = "confirmed_unavailable"
    UNKNOWN = "unknown"
    STALE = "stale"


@dataclass(frozen=True)
class TvProviderOffer:
    provider_id: int
    provider_name: str
    category: str
    logo_url: str | None


@dataclass(frozen=True)
class TvProviderAvailability:
    state: TvProviderState
    region: str
    fetched_at: datetime | None
    link: str | None
    offers: tuple[TvProviderOffer, ...]


@dataclass(frozen=True)
class TvEpisodeProgress:
    trakt_episode_id: int
    season_number: int
    episode_number: int
    tvdb_id: int | None
    title: str | None
    first_aired: datetime | None
    aired: bool
    watched: bool
    last_watched_at: datetime | None
    plex_rating_key: str | None
    watched_by_configured_plex_account: bool | None
    plex_last_viewed_at: datetime | None


@dataclass(frozen=True)
class TvSeasonProgress:
    season_number: int
    aired: int
    completed: int
    monitored_desired: bool
    search_aired_unwatched_episodes: tuple[int, ...]
    cleanup_state: str
    episodes: tuple[TvEpisodeProgress, ...]


@dataclass(frozen=True)
class TvShowSnapshot:
    trakt_id: int
    tvdb_id: int | None
    tmdb_id: int | None
    imdb_id: str | None
    title: str
    year: int | None
    identity_status: TvIdentityStatus
    in_trakt_watchlist: bool
    lifecycle_state: TvLifecycleState
    lifecycle_version: int
    trakt_status: str
    aired: int
    completed: int
    last_watched_episode: TvEpisodeProgress | None
    next_episode: TvEpisodeProgress | None
    sonarr_desired: bool
    sonarr_monitored_desired: bool
    plex_watchlist_desired: bool
    seasons: tuple[TvSeasonProgress, ...]
    poland_availability: TvProviderAvailability
    blockers: tuple[str, ...]


@dataclass(frozen=True)
class TvPlexHistoryBinding:
    machine_identifier: str | None
    account_id: int | None
    library_section_id: str | None
    library_section_title: str | None
    capable: bool
    bootstrap_complete: bool
    last_collection_complete: bool
    last_collection_succeeded: bool
    observed_event_count: int
    pending_post_cutover_routes: int
    collected_at: datetime | None
    watermark: datetime | None


@dataclass(frozen=True)
class TvCleanupAuthorization:
    event_id: str
    action_type: TvCleanupActionType
    trakt_id: int
    tvdb_id: int
    season_number: int | None
    lifecycle_version: int
    predicate_hash: str
    manifest_id: str
    authorized_at: datetime
    expires_at: datetime
    expected_aired: int
    expected_completed: int
    plex_evidence_collected_at: datetime
    plex_history_watermark: datetime | None


@dataclass(frozen=True)
class TvSyncSnapshot:
    schema_version: str
    generation_id: str
    published_at: datetime
    generated_at: datetime
    kind: str
    mutation_capable: bool
    health_reasons: tuple[str, ...]
    plex_history: TvPlexHistoryBinding
    shows: tuple[TvShowSnapshot, ...]
    cleanup_authorizations: tuple[TvCleanupAuthorization, ...]
~~~

- [ ] **Step 5: Implement the strict reader and mapper**

TvBackendClient must accept an injected httpx.Client, use no sync-first POST,
and expose:

~~~python
class TvBackendError(Exception):
    pass


class TvBackendClient:
    def __init__(
        self,
        base_url: str,
        *,
        http_client: httpx.Client | None = None,
        timeout_seconds: int = 30,
        sync_key: str,
    ):
        self.base_url = base_url.rstrip("/")
        self.http_client = http_client or httpx.Client(timeout=timeout_seconds)
        self.sync_key = sync_key

    def fetch_sync_snapshot(self) -> TvSyncSnapshot:
        response = self.http_client.get(
            f"{self.base_url}/api/export/tv/sync-state"
        )
        try:
            response.raise_for_status()
        except httpx.HTTPStatusError as error:
            raise TvBackendError(
                f"watchlist-app TV snapshot failed: HTTP {response.status_code}"
            ) from error
        try:
            payload = response.json()
        except ValueError as error:
            raise TvBackendError(
                "watchlist-app TV snapshot returned invalid JSON"
            ) from error
        return map_tv_sync_snapshot(payload)
~~~

Implement private field readers that distinguish bool from int, require
timezone-aware ISO-8601 timestamps, trim non-empty strings, and reject duplicate
show Trakt IDs, duplicate non-null TVDB IDs, duplicate season numbers,
duplicate episode numbers within a season, duplicate blocker strings, duplicate
cleanup event IDs, progress where completed exceeds aired, episode/season
number disagreement, unsupported enum strings, and cleanup manifest IDs that
do not equal the generation ID.

Map `polandAvailability` to the typed provider records above. Require exactly
`state`, `region`, `fetchedAt`, `link`, and `offers`; require positive provider
IDs, nonblank names, categories in `flatrate|free|ads|rent|buy`, unique
`(providerId, category)` pairs, and timezone-aware fetch times. `available`
requires at least one offer, `confirmed_unavailable` requires a successful
fetch time and no offers, and `unknown` may have null time/link. Add malformed
tests for a missing field, `{}`, unknown state/category, duplicate offer, and a
timezone-free fetch time.

Enforce the conditional Plex-history invariant: `capable=true` requires a
nonblank machine identifier, positive account ID, and nonblank string library
section ID. `capable=false` requires those three identity fields and the section
title to be null. Require boolean last-collection fields plus nonnegative
integer `observedEventCount` and `pendingPostCutoverRoutes` (reject bool as int).
`mutationCapable=true` additionally requires capable/bootstrap/last-collection
complete and successful, a timezone-aware collectedAt, and zero pending routes.
A positive observed count requires a timezone-aware watermark; a null watermark
is accepted only for the complete-successful zero-count tuple or the canonical
unavailable/incomplete tuple with `mutationCapable=false`, capable/bootstrap/
last-collection flags false, both counts zero, null configured binding and
collection time, and the capability/bootstrap blocker strings. A positive
pending-route count is accepted only when `mutationCapable=false` and
`healthReasons` contains exactly one
`plex_post_cutover_routing_unresolved`; zero routes requires that blocker
absent. A non-mutation-capable envelope may otherwise have null collection
times; every supplied value must still be a valid timestamp and every tuple
must remain internally consistent.

- [ ] **Step 6: Run the happy-path tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_backend_client.py -q
Pop-Location
~~~

Expected: the two initial tests pass.

- [ ] **Step 7: Add parameterized malformed-contract tests**

First add a positive incapable-envelope test by taking the shared fixture,
setting `mutationCapable=false`, both health reasons, `capable=false`,
`bootstrapComplete=false`, `lastCollectionComplete=false`,
`lastCollectionSucceeded=false`, both counts to zero, all Plex identity fields
plus collectedAt/watermark to null, and `cleanupAuthorizations=[]`. Assert it
parses and cannot become apply-eligible.

Add exact mutations and expected message fragments:

~~~python
@pytest.mark.parametrize(
    ("mutate", "message"),
    [
        (lambda value: value.update(schemaVersion="2"), "schemaVersion"),
        (lambda value: value.update(generationId=""), "generationId"),
        (lambda value: value.update(publishedAt="2026-07-13T12:00:00"), "timezone"),
        (lambda value: value.update(mutationCapable="true"), "mutationCapable"),
        (
            lambda value: value["shows"][0].update(completed=4),
            "completed cannot exceed aired",
        ),
        (
            lambda value: value["shows"].append(copy.deepcopy(value["shows"][0])),
            "duplicate traktId",
        ),
        (
            lambda value: value["shows"][1].update(tvdbId=7101),
            "duplicate tvdbId",
        ),
        (
            lambda value: value["shows"][0].update(identityStatus="guess"),
            "identityStatus",
        ),
    ],
)
def test_fetch_snapshot_rejects_malformed_complete_contract(
    load_tv_fixture,
    mutate,
    message,
):
    payload = load_tv_fixture()
    mutate(payload)
    client = client_returning(payload)

    with pytest.raises(TvBackendError, match=message):
        client.fetch_sync_snapshot()
~~~

Add separate tests for duplicate seasons, duplicate episodes, invalid optional
dates, a cleanup authorization bound to another manifest, and invalid
season_files seasonNumber null. Also prove the canonical
`identityStatus="legacy_unresolved"` value parses successfully but is never
apply-eligible; the planner emits `identity_legacy_unresolved` and no Sonarr or
Plex destination action for that show. Add complete-empty Plex-history fixtures
with a null watermark, reject every other null-watermark combination, and prove
`plex_post_cutover_routing_unresolved` or a positive
`pendingPostCutoverRoutes` makes the whole snapshot mutation-incapable and
produces zero reversible destination actions.

- [ ] **Step 8: Run all parser tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_backend_client.py -q
Pop-Location
~~~

Expected: all TV backend client tests pass.

- [ ] **Step 9: Commit the contract slice**

~~~powershell
git add workers/vod-filter/tests/vod_filter/test_tv_backend_client.py workers/vod-filter/src/models/tv_sync.py workers/vod-filter/src/clients/tv_backend_client.py
git commit -m "feat(worker): parse complete TV sync snapshots"
~~~

### Task 2: Add Migration-Safe TV Ownership, Adoption, Audit, And Run Lease

**Files:**

- Create: workers/vod-filter/src/models/migrations/0001_tv_worker_state.sql
- Create: workers/vod-filter/src/services/tv_state_store.py
- Create: workers/vod-filter/src/models/tv_destination.py
- Create: workers/vod-filter/tests/vod_filter/test_tv_state_store.py

- [ ] **Step 1: Write migration and persistence tests first**

The tests must create CacheService once to establish the current movie schema,
then create TvStateStore against the same file. Assert current movie rows remain
and TV migrations are idempotent:

~~~python
from uuid import UUID


def test_tv_migration_preserves_existing_movie_database(tmp_path):
    database = tmp_path / "vod-filter.db"
    movie_store = CacheService(str(database))
    movie_store.upsert_movie(100, "Movie", 2024)

    tv_store = TvStateStore(database)
    tv_store.close()
    reopened = TvStateStore(database)

    assert movie_store.get_movie(100).title == "Movie"
    assert reopened.applied_migrations() == ("0001_tv_worker_state",)


def test_tv_ownership_binds_destination_target_and_lifecycle(tmp_path):
    store = TvStateStore(tmp_path / "state.db")

    store.mark_managed(
        destination="sonarr",
        tvdb_id=7101,
        target_id="501",
        ownership_kind="added",
        lifecycle_version=4,
        action="add_series",
    )

    managed = store.get_managed_destinations()
    assert UUID(managed[0].ownership_id)
    assert managed == (
        ManagedTvDestination(
            ownership_id=managed[0].ownership_id,
            destination="sonarr",
            tvdb_id=7101,
            target_id="501",
            ownership_kind="added",
            lifecycle_version=4,
            last_action="add_series",
            retired_at=None,
        ),
    )
~~~

Add tests for:

- exact Plex target IDs;
- adopted versus added ownership;
- lifecycle-version refresh without changing first-managed time;
- releasing only the requested destination by setting `retired_at`, never
  deleting its ownership history;
- preserving the retired target when revival creates a new active target;
- enforcing at most one active row per destination/TVDB while permitting any
  number of retired rows;
- run start/finish with redacted JSON summary;
- observation replacement only after a complete boundary read;
- reversible action start/completed/failed audit; and
- duplicate action IDs updating one row rather than appending ambiguous rows.

- [ ] **Step 2: Add concurrent lease tests**

Use injected UTC times so no sleeps are required:

~~~python
def test_one_unexpired_tv_run_lease_blocks_another_worker(tmp_path):
    store = TvStateStore(tmp_path / "state.db")
    now = datetime(2026, 7, 13, 12, tzinfo=timezone.utc)

    first = store.acquire_run_lease(
        worker_id="worker-a",
        now=now,
        duration=timedelta(minutes=20),
    )

    with pytest.raises(TvRunLeaseUnavailable):
        store.acquire_run_lease(
            worker_id="worker-b",
            now=now + timedelta(minutes=1),
            duration=timedelta(minutes=20),
        )

    reclaimed = store.acquire_run_lease(
        worker_id="worker-b",
        now=now + timedelta(minutes=21),
        duration=timedelta(minutes=20),
    )
    assert reclaimed.worker_id == "worker-b"
    assert reclaimed.lease_id != first.lease_id
~~~

Also open two independent `TvStateStore` connections to the same file and use a
barrier to race acquisition, expiry reclaim, and renewal. Exactly one contender
wins each acquire/reclaim; the loser receives `TvRunLeaseUnavailable`. Renew and
release require the matching lease ID and worker ID. Renewal at
`expires_at <= now` is rejected and can never revive an expired lease; after a
different worker reclaims it, every stale renew/release from the old process is
ineffective.

- [ ] **Step 3: Run the state tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_state_store.py -q
Pop-Location
~~~

Expected: collection fails because TvStateStore and the migration do not exist.

- [ ] **Step 4: Create the additive SQL migration**

Use this schema, with no foreign keys to movie tables:

~~~sql
CREATE TABLE IF NOT EXISTS worker_schema_migrations (
    migration_id TEXT PRIMARY KEY,
    checksum TEXT NOT NULL,
    applied_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS tv_managed_destinations (
    ownership_id TEXT PRIMARY KEY,
    destination TEXT NOT NULL,
    tvdb_id INTEGER NOT NULL,
    target_id TEXT NOT NULL,
    ownership_kind TEXT NOT NULL,
    lifecycle_version INTEGER NOT NULL,
    first_managed_at TEXT NOT NULL,
    last_managed_at TEXT NOT NULL,
    retired_at TEXT,
    last_action TEXT NOT NULL,
    CHECK (destination IN ('sonarr', 'plex_watchlist')),
    CHECK (tvdb_id > 0),
    CHECK (length(target_id) > 0),
    CHECK (ownership_kind IN ('added', 'adopted')),
    CHECK (lifecycle_version > 0)
);

CREATE TABLE IF NOT EXISTS tv_run_leases (
    workflow TEXT PRIMARY KEY,
    lease_id TEXT NOT NULL UNIQUE,
    worker_id TEXT NOT NULL,
    acquired_at TEXT NOT NULL,
    heartbeat_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    CHECK (workflow = 'tv_sync')
);

CREATE TABLE IF NOT EXISTS tv_sync_runs (
    run_id TEXT PRIMARY KEY,
    worker_id TEXT NOT NULL,
    generation_id TEXT,
    mode TEXT NOT NULL,
    status TEXT NOT NULL,
    started_at TEXT NOT NULL,
    finished_at TEXT,
    summary_json TEXT,
    error TEXT,
    CHECK (mode IN ('report_only', 'apply')),
    CHECK (status IN ('running', 'success', 'partial', 'blocked', 'failed'))
);

CREATE TABLE IF NOT EXISTS tv_destination_observations (
    run_id TEXT NOT NULL,
    boundary TEXT NOT NULL,
    target_id TEXT NOT NULL,
    complete INTEGER NOT NULL,
    state_json TEXT NOT NULL,
    observed_at TEXT NOT NULL,
    PRIMARY KEY (run_id, boundary, target_id),
    CHECK (boundary IN (
        'sonarr',
        'plex_watchlist',
        'plex_library',
        'path_inventory'
    )),
    CHECK (complete IN (0, 1))
);

CREATE TABLE IF NOT EXISTS tv_action_history (
    run_id TEXT NOT NULL,
    action_id TEXT NOT NULL,
    destination TEXT NOT NULL,
    action_type TEXT NOT NULL,
    tvdb_id INTEGER NOT NULL,
    target_id TEXT,
    status TEXT NOT NULL,
    reason TEXT,
    attempted_at TEXT NOT NULL,
    completed_at TEXT,
    PRIMARY KEY (run_id, action_id),
    CHECK (destination IN ('sonarr', 'plex_watchlist', 'ownership')),
    CHECK (tvdb_id > 0),
    CHECK (status IN ('running', 'completed', 'skipped', 'failed'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_tv_managed_destination_active
    ON tv_managed_destinations(destination, tvdb_id)
    WHERE retired_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_tv_managed_destination_target_history
    ON tv_managed_destinations(destination, tvdb_id, target_id, retired_at);
CREATE INDEX IF NOT EXISTS idx_tv_sync_runs_started
    ON tv_sync_runs(started_at);
CREATE INDEX IF NOT EXISTS idx_tv_action_history_tvdb
    ON tv_action_history(tvdb_id, attempted_at);
~~~

- [ ] **Step 5: Implement the migration runner and store API**

TvStateStore must:

- connect with sqlite3.connect(path, timeout=5.0, isolation_level=None);
- set row_factory to sqlite3.Row, PRAGMA foreign_keys=ON, and
  PRAGMA busy_timeout=5000;
- enumerate src/models/migrations/[0-9][0-9][0-9][0-9]_*.sql in lexical order;
- acquire BEGIN IMMEDIATE before checking and applying each migration;
- execute a migration and insert its stem into worker_schema_migrations in one
  transaction;
- store a lowercase SHA-256 checksum beside each migration ID and reject a
  changed checksum rather than silently editing an applied schema;
- serialize summary and observation dictionaries with
  json.dumps(sort_keys=True, separators=(",", ":"));
- execute lease acquire, expiry reclaim, renew, and release as transactional
  compare-and-set operations under `BEGIN IMMEDIATE`, never as a read followed
  by an unlocked write. Reclaim updates only the exact expired row observed in
  that transaction. Renew uses one conditional update matching workflow,
  lease ID, worker ID, and `expires_at > now`; zero affected rows raises
  `TvRunLeaseUnavailable`. Commit before returning the lease and roll back every
  failure; and
- expose the method signatures used by the tests. Place
  ManagedTvDestination in tv_destination.py and import it into
  tv_state_store.py; place TvRunLease in tv_state_store.py:

~~~text
class TvRunLeaseUnavailable(RuntimeError):
    pass


@dataclass(frozen=True)
class TvRunLease:
    lease_id: str
    worker_id: str
    expires_at: datetime


@dataclass(frozen=True)
class ManagedTvDestination:
    ownership_id: str
    destination: str
    tvdb_id: int
    target_id: str
    ownership_kind: str
    lifecycle_version: int
    last_action: str
    retired_at: datetime | None


class TvStateStore:
    # Public interface; each method uses the transactions specified below.
    __init__(self, database_path: str | Path)
    close(self) -> None
    applied_migrations(self) -> tuple[str, ...]
    def acquire_run_lease(
        self,
        *,
        worker_id: str,
        now: datetime,
        duration: timedelta,
    ) -> TvRunLease
    def renew_run_lease(
        self,
        *,
        lease_id: str,
        worker_id: str,
        now: datetime,
        duration: timedelta,
    ) -> TvRunLease
    def release_run_lease(self, *, lease_id: str, worker_id: str) -> None
    def mark_managed(
        self,
        *,
        destination: str,
        tvdb_id: int,
        target_id: str,
        ownership_kind: str,
        lifecycle_version: int,
        action: str,
    ) -> None
    def release_managed(
        self,
        *,
        destination: str,
        tvdb_id: int,
        retired_at: datetime,
        action: str,
    ) -> None
    def get_managed_destinations(self) -> tuple[ManagedTvDestination, ...]
    def get_managed_destination_history(
        self,
    ) -> tuple[ManagedTvDestination, ...]
    def start_run(
        self,
        *,
        run_id: str,
        worker_id: str,
        mode: str,
        started_at: datetime,
    ) -> None
    def finish_run(
        self,
        *,
        run_id: str,
        generation_id: str | None,
        status: str,
        finished_at: datetime,
        summary: dict,
        error: str | None,
    ) -> None
    def replace_observations(
        self,
        *,
        run_id: str,
        boundary: str,
        complete: bool,
        observations: tuple[dict, ...],
        observed_at: datetime,
    ) -> None
    def record_action_started(
        self,
        *,
        run_id: str,
        action_id: str,
        destination: str,
        action_type: str,
        tvdb_id: int,
        target_id: str | None,
        attempted_at: datetime,
    ) -> None
    def record_action_result(
        self,
        *,
        run_id: str,
        action_id: str,
        status: str,
        reason: str | None,
        completed_at: datetime,
    ) -> None
~~~

Implement every listed method before running the tests. Validation must reject
bool-as-int identities, blank target/action IDs,
unknown destination/ownership/status strings and timezone-free timestamps.
`get_managed_destinations` returns only rows whose `retired_at` is null;
`get_managed_destination_history` returns active and retired rows in stable
first-managed order. `mark_managed` updates the active row only when its exact
target is unchanged; a different target must first retire the old row, then
insert a fresh ownership ID.

For summary/observation dictionaries, recursively reject the shared exact
case-insensitive key denylist `token`, `accessToken`, `refreshToken`, `apiKey`, `api_key`, `password`, `secret`, `clientSecret`, `plexToken`, `sonarrApiKey`, `syncKey`, `mongoConnectionString`, `rawPath`, `mediaPath`, `media_path`, `filesystemPath`, `filesystem_path`, `responseBody`, and `response_body`. Test every name at the root and at two nested dictionary/array depths.
Do not use substring matching: safe derived keys such as
`terminalPathFingerprint`, `pathCount`, `predicateHash`, and `inventoryHash`
must remain valid while their raw source values never enter the store.

- [ ] **Step 6: Run migration, ownership, audit, and lease tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_state_store.py -q
Pop-Location
~~~

Expected: all state-store tests pass.

- [ ] **Step 7: Run the existing movie persistence tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_managed_destinations.py tests\vod_filter\test_run_history.py tests\vod_filter\test_radarr_observations.py -q
Pop-Location
~~~

Expected: all existing movie SQLite tests pass unchanged.

- [ ] **Step 8: Commit the state slice**

~~~powershell
git add workers/vod-filter/src/models/migrations/0001_tv_worker_state.sql workers/vod-filter/src/models/tv_destination.py workers/vod-filter/src/services/tv_state_store.py workers/vod-filter/tests/vod_filter/test_tv_state_store.py
git commit -m "feat(worker): persist TV destination ownership"
~~~

### Task 3: Add The Reversible Exact-TVDB Sonarr Client

**Files:**

- Create: workers/vod-filter/src/clients/sonarr_client.py
- Create: workers/vod-filter/tests/vod_filter/test_sonarr_client.py
- Modify: workers/vod-filter/src/models/tv_destination.py

- [ ] **Step 1: Write exact-identity collection tests**

Use httpx.MockTransport and verify query parameters, duplicate handling, and
strict response mapping:

~~~python
def test_get_all_series_maps_exact_tvdb_identity():
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.method == "GET"
        assert request.url.path == "/api/v3/series"
        return httpx.Response(
            200,
            json=[
                {
                    "id": 501,
                    "tvdbId": 7101,
                    "title": "Weekly Show",
                    "year": 2025,
                    "monitored": True,
                    "monitorNewItems": "all",
                    "seriesType": "standard",
                    "nextAiring": None,
                    "seasons": [
                        {"seasonNumber": 0, "monitored": False},
                        {"seasonNumber": 1, "monitored": True}
                    ]
                }
            ],
        )

    series = sonarr_client(handler).get_all_series()

    assert len(series) == 1
    assert series[0].series_id == 501
    assert series[0].tvdb_id == 7101
    assert series[0].monitored is True
    assert series[0].monitor_new_items == "all"
    assert series[0].seasons == (
        SonarrSeason(0, False),
        SonarrSeason(1, True),
    )
    assert series[0].resource["id"] == 501


def test_get_all_series_rejects_duplicate_tvdb_identity():
    payload = [sonarr_series_json(501, 7101), sonarr_series_json(502, 7101)]
    client = sonarr_client(lambda request: httpx.Response(200, json=payload))

    with pytest.raises(SonarrError, match="duplicate TVDB ID 7101"):
        client.get_all_series()
~~~

Also test non-list JSON, missing/nonpositive IDs, timezone-free nextAiring, and
HTTP failures whose exception text contains only method/path/status.

- [ ] **Step 2: Write exact lookup and safe-add tests**

The add test must prove search text only discovers the candidate and TVDB
identity authorizes it:

~~~python
def test_add_series_uses_exact_tvdb_and_never_starts_broad_search():
    requests: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        if request.method == "GET":
            assert request.url.path == "/api/v3/series/lookup"
            assert request.url.params["term"] == "tvdb:7101"
            return httpx.Response(
                200,
                json=[
                    {
                        "title": "Wrong Result",
                        "tvdbId": 9999,
                        "titleSlug": "wrong",
                        "images": []
                    },
                    {
                        "title": "Weekly Show",
                        "tvdbId": 7101,
                        "titleSlug": "weekly-show",
                        "images": []
                    }
                ],
            )
        body = json.loads(request.content)
        assert request.url.path == "/api/v3/series"
        assert body["tvdbId"] == 7101
        assert body["rootFolderPath"] == "/tv"
        assert body["qualityProfileId"] == 3
        assert body["seriesType"] == "standard"
        assert body["monitored"] is True
        assert body["monitorNewItems"] == "all"
        assert body["seasonFolder"] is True
        assert body["addOptions"] == {
            "monitor": "none",
            "searchForMissingEpisodes": False,
            "searchForCutoffUnmetEpisodes": False,
            "ignoreEpisodesWithFiles": False,
            "ignoreEpisodesWithoutFiles": False
        }
        assert "languageProfileId" not in body
        return httpx.Response(201, json=sonarr_series_json(501, 7101))

    result = sonarr_client(handler).add_series(
        tvdb_id=7101,
        root_folder="/tv",
        quality_profile_id=3,
        series_type="standard",
        language_profile_id=None,
    )

    assert result.series_id == 501
    assert [request.url.path for request in requests] == [
        "/api/v3/series/lookup",
        "/api/v3/series",
    ]
~~~

Add tests that zero or two exact lookup rows fail closed, the posted response
must retain TVDB 7101, and a configured language profile is included only when
the caller supplies its positive ID.

- [ ] **Step 3: Write season and exact-episode operation tests**

Cover the complete reversible API:

~~~python
def test_update_monitoring_keeps_series_and_new_seasons_monitored():
    current = sonarr_series_json(501, 7101)
    current["monitored"] = False
    current["monitorNewItems"] = "none"
    current["seasons"] = [
        {"seasonNumber": 0, "monitored": True},
        {"seasonNumber": 1, "monitored": False},
        {"seasonNumber": 2, "monitored": False},
    ]

    def handler(request: httpx.Request) -> httpx.Response:
        body = json.loads(request.content)
        assert request.method == "PUT"
        assert request.url.path == "/api/v3/series/501"
        assert body["id"] == 501
        assert body["tvdbId"] == 7101
        assert body["monitored"] is True
        assert body["monitorNewItems"] == "all"
        assert body["seasons"] == [
            {"seasonNumber": 0, "monitored": False},
            {"seasonNumber": 1, "monitored": True},
            {"seasonNumber": 2, "monitored": True},
        ]
        return httpx.Response(202, json=body)

    sonarr_client(handler).update_series_monitoring(
        current_resource=current,
        expected_tvdb_id=7101,
        monitored_seasons={0: False, 1: True, 2: True},
    )


def test_search_episodes_uses_only_exact_episode_ids():
    requests: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        body = json.loads(request.content)
        if request.url.path == "/api/v3/episode/monitor":
            assert request.method == "PUT"
            assert body == {"episodeIds": [7003, 7004], "monitored": True}
            return httpx.Response(202, json=[])
        assert request.url.path == "/api/v3/command"
        assert body == {"name": "EpisodeSearch", "episodeIds": [7003, 7004]}
        return httpx.Response(201, json={"id": 90, "name": "EpisodeSearch"})

    client = sonarr_client(handler)
    client.set_episode_monitoring((7004, 7003), monitored=True)
    client.search_episodes((7004, 7003))

    assert len(requests) == 2
~~~

Add GET /api/v3/episode?seriesId=501 tests that map id, seriesId, tvdbId,
seasonNumber, episodeNumber, airDateUtc, hasFile, episodeFileId, and monitored.
Reject duplicate Sonarr season/episode coordinates. Assert season 0 can be
collected but is never passed to search_episodes by later planner tests.

- [ ] **Step 4: Assert the Phase 3 client exposes no destructive operation**

~~~python
def test_phase_3_sonarr_client_has_no_destructive_surface():
    forbidden = {
        "delete_episode_file",
        "delete_series",
        "remove_series",
    }
    assert forbidden.isdisjoint(dir(SonarrClient))
~~~

- [ ] **Step 5: Run the Sonarr tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sonarr_client.py -q
Pop-Location
~~~

Expected: collection fails because SonarrClient and destination models do not
exist.

- [ ] **Step 6: Add the Sonarr destination models**

Add these concrete types to tv_destination.py:

~~~python
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import Enum
from typing import Any


@dataclass(frozen=True)
class SonarrSeason:
    season_number: int
    monitored: bool


@dataclass(frozen=True)
class SonarrEpisode:
    episode_id: int
    series_id: int
    tvdb_id: int
    season_number: int
    episode_number: int
    air_date_utc: datetime | None
    has_file: bool
    episode_file_id: int | None
    monitored: bool


@dataclass(frozen=True)
class SonarrSeries:
    series_id: int
    tvdb_id: int
    title: str
    year: int | None
    monitored: bool
    monitor_new_items: str
    series_type: str
    next_airing: datetime | None
    seasons: tuple[SonarrSeason, ...]
    resource: dict[str, Any]
~~~

The resource mapping must be a defensive copy so an executor cannot mutate a
collector-owned object.

- [ ] **Step 7: Implement SonarrClient with one request helper**

Use:

~~~python
class SonarrError(RuntimeError):
    pass


class SonarrClient:
    def __init__(
        self,
        base_url: str,
        api_key: str,
        *,
        http_client: httpx.Client | None = None,
        timeout_seconds: int = 30,
    ):
        self.base_url = base_url.rstrip("/")
        self.http_client = http_client or httpx.Client(
            base_url=f"{self.base_url}/",
            headers={"X-Api-Key": api_key},
            timeout=timeout_seconds,
        )

    def _request(self, method: str, path: str, **kwargs) -> Any:
        response = self.http_client.request(
            method,
            f"{self.base_url}{path}",
            **kwargs,
        )
        try:
            response.raise_for_status()
        except httpx.HTTPStatusError as error:
            raise SonarrError(
                f"Sonarr {method} {path} failed: HTTP {response.status_code}"
            ) from error
        if response.status_code == 204:
            return None
        try:
            return response.json()
        except ValueError as error:
            raise SonarrError(
                f"Sonarr {method} {path} returned invalid JSON"
            ) from error
~~~

Implement the tested public methods:

~~~text
def get_all_series(self) -> tuple[SonarrSeries, ...]
def lookup_series(self, tvdb_id: int) -> dict[str, Any]
def add_series(
    self,
    *,
    tvdb_id: int,
    root_folder: str,
    quality_profile_id: int,
    series_type: str,
    language_profile_id: int | None,
) -> SonarrSeries
def get_episodes(self, series_id: int) -> tuple[SonarrEpisode, ...]
def update_series_monitoring(
    self,
    *,
    current_resource: dict[str, Any],
    expected_tvdb_id: int,
    monitored_seasons: dict[int, bool],
) -> SonarrSeries
def set_episode_monitoring(
    self,
    episode_ids: tuple[int, ...],
    *,
    monitored: bool,
) -> None
def search_episodes(self, episode_ids: tuple[int, ...]) -> int
~~~

Every identity and ID argument must reject bool, zero, and negative values.
Every episode ID tuple must be deduplicated and sorted before submission. Empty
tuples make no HTTP request. update_series_monitoring must verify the current
resource's TVDB ID and preserve all unrelated fields while replacing only
monitored, monitorNewItems, and the monitored value inside each known season.

- [ ] **Step 8: Run the Sonarr client tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sonarr_client.py -q
Pop-Location
~~~

Expected: every collection, add, monitoring, targeted-search, redaction, and
no-destructive-surface test passes.

- [ ] **Step 9: Commit the Sonarr slice**

~~~powershell
git add workers/vod-filter/src/models/tv_destination.py workers/vod-filter/src/clients/sonarr_client.py workers/vod-filter/tests/vod_filter/test_sonarr_client.py
git commit -m "feat(worker): add exact TVDB Sonarr operations"
~~~

### Task 4: Add The Exact-Identity Plex Universal-Watchlist Client

**Files:**

- Create: workers/vod-filter/src/clients/plex_tv_client.py
- Create: workers/vod-filter/tests/vod_filter/test_plex_tv_client.py
- Modify: workers/vod-filter/src/models/tv_destination.py

- [ ] **Step 1: Write exact GUID extraction and watchlist collection tests**

Use lightweight fake Plex objects. The test must prove only show rows survive:

~~~python
def test_get_watchlist_shows_filters_type_and_maps_exact_guids():
    movie = FakePlexItem("movie", "Movie", "movie-key", ["tmdb://1"])
    show = FakePlexItem(
        "show",
        "Weekly Show",
        "show-key",
        ["tvdb://7101", "tmdb://8101", "imdb://tt0007101"],
        year=2025,
    )
    account = FakeAccount(account_id=17, watchlist=[movie, show])
    client = PlexTvClient.from_objects(
        account=account,
        machine_identifier="plex-machine",
    )

    rows = client.get_watchlist_shows()

    assert rows == (
        PlexWatchlistShow(
            target_id="show-key",
            title="Weekly Show",
            year=2025,
            tvdb_id=7101,
            tmdb_id=8101,
            imdb_id="tt0007101",
        ),
    )
~~~

Add tests for nested guid objects, query suffixes, invalid numeric GUIDs,
duplicate exact TVDB rows, non-show filtering, configured account mismatch, and
missing stable target ID.

- [ ] **Step 2: Write exact discovery/add/remove tests**

~~~python
def test_add_uses_text_only_for_discovery_and_exact_ids_for_authorization():
    wrong = FakePlexItem(
        "show",
        "Weekly Show",
        "wrong-key",
        ["tvdb://9999"],
        year=2025,
    )
    exact = FakePlexItem(
        "show",
        "Localized Title",
        "exact-key",
        ["tmdb://8101"],
        year=2025,
    )
    account = FakeAccount(
        account_id=17,
        discovery=[wrong, exact],
        watchlist=[],
    )
    client = PlexTvClient.from_objects(
        account=account,
        machine_identifier="plex-machine",
    )

    target_id = client.add_show_to_watchlist(
        PlexShowIdentity(
            tvdb_id=7101,
            tmdb_id=8101,
            imdb_id="tt0007101",
            title="Weekly Show",
            year=2025,
        )
    )

    assert target_id == "exact-key"
    assert wrong.add_calls == 0
    assert exact.add_calls == 1


def test_remove_refuses_title_only_match():
    title_only = FakePlexItem(
        "show",
        "Weekly Show",
        "wrong-key",
        ["tvdb://9999"],
        year=2025,
    )
    account = FakeAccount(account_id=17, watchlist=[title_only])
    client = PlexTvClient.from_objects(
        account=account,
        machine_identifier="plex-machine",
    )

    removed = client.remove_show_from_watchlist(
        PlexShowIdentity(
            tvdb_id=7101,
            tmdb_id=8101,
            imdb_id="tt0007101",
            title="Weekly Show",
            year=2025,
        )
    )

    assert removed is False
    assert title_only.remove_calls == 0
~~~

Add cases for exact TVDB, verified alternate TMDB/IMDb, already-present add,
already-absent remove, ambiguous multiple exact results, and rate-limit errors.

- [ ] **Step 3: Assert the client cannot mutate a Plex library**

~~~python
def test_phase_3_plex_client_has_no_library_mutation_surface():
    forbidden = {
        "delete_from_library",
        "delete_episode",
        "remove_library_item",
    }
    assert forbidden.isdisjoint(dir(PlexTvClient))
~~~

- [ ] **Step 4: Run the Plex TV tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_plex_tv_client.py -q
Pop-Location
~~~

Expected: collection fails because PlexTvClient and Plex destination models do
not exist.

- [ ] **Step 5: Add exact Plex destination models**

Append:

~~~python
@dataclass(frozen=True)
class PlexShowIdentity:
    tvdb_id: int
    tmdb_id: int | None
    imdb_id: str | None
    title: str
    year: int | None


@dataclass(frozen=True)
class PlexWatchlistShow:
    target_id: str
    title: str
    year: int | None
    tvdb_id: int | None
    tmdb_id: int | None
    imdb_id: str | None
~~~

- [ ] **Step 6: Implement PlexTvClient as a watchlist-only adapter**

The constructor verifies the account and server identity once:

~~~python
class PlexTvError(RuntimeError):
    pass


class PlexTvIdentityNotFound(PlexTvError):
    pass


class PlexTvIdentityAmbiguous(PlexTvError):
    pass


class PlexTvClient:
    def __init__(
        self,
        url: str,
        token: str,
        *,
        expected_account_id: int,
    ):
        self.server = PlexServer(url, token)
        self.account = MyPlexAccount(token=token)
        self.machine_identifier = str(self.server.machineIdentifier)
        actual_account_id = int(self.account.id)
        if actual_account_id != expected_account_id:
            raise PlexTvError(
                f"Plex account mismatch: expected {expected_account_id}, "
                f"received {actual_account_id}"
            )
        self.account_id = actual_account_id

    @classmethod
    def from_objects(
        cls,
        *,
        account,
        machine_identifier: str,
    ) -> "PlexTvClient":
        instance = cls.__new__(cls)
        instance.account = account
        instance.server = None
        instance.machine_identifier = machine_identifier
        instance.account_id = int(account.id)
        return instance
~~~

Implement:

~~~python
def get_watchlist_shows(self) -> tuple[PlexWatchlistShow, ...]
def add_show_to_watchlist(self, identity: PlexShowIdentity) -> str
def remove_show_from_watchlist(self, identity: PlexShowIdentity) -> bool
~~~

Use account.watchlist(libtype="show") for collection and
account.searchDiscover(query=query, limit=50, libtype="show") for discovery.
Generate unique discovery queries from title, ASCII-folded title, and
title-plus-year. A candidate matches only if its exact TVDB ID matches, or its
exact TMDB/IMDb ID matches one of the alternate IDs carried by a
backend-verified identity. Never compare title or year for authorization.

Catch Plex exceptions and raise redacted PlexTvError messages containing the
operation and identity IDs but not URL, token, raw object, or response body.
Retain the existing retry_on_plex_rate_limit decorator.

- [ ] **Step 7: Run Plex TV tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_plex_tv_client.py -q
Pop-Location
~~~

Expected: all identity, watchlist lifecycle, ambiguity, redaction, and
no-library-mutation tests pass.

- [ ] **Step 8: Run existing movie Plex tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_plex_client_identity.py tests\vod_filter\test_plex_service.py -q
Pop-Location
~~~

Expected: existing movie Plex behavior passes unchanged.

- [ ] **Step 9: Commit the Plex slice**

~~~powershell
git add workers/vod-filter/src/models/tv_destination.py workers/vod-filter/src/clients/plex_tv_client.py workers/vod-filter/tests/vod_filter/test_plex_tv_client.py
git commit -m "feat(worker): add exact Plex TV watchlist operations"
~~~

### Task 5: Collect One Coherent Read-Only TV Worker State

**Files:**

- Create: workers/vod-filter/src/services/tv_sync_collector.py
- Create: workers/vod-filter/tests/vod_filter/test_tv_sync_collector.py
- Modify: workers/vod-filter/src/models/tv_destination.py

- [ ] **Step 1: Write successful full-collection tests**

Use fakes that record every call:

~~~python
def test_collector_reads_backend_then_exact_destination_state():
    snapshot = snapshot_fixture()
    sonarr = FakeSonarr(
        series=(sonarr_series(501, 7101), sonarr_series(502, 7102)),
        episodes={
            501: (sonarr_episode(7003, 501, 1, 3),),
            502: (),
        },
    )
    plex = FakePlexTv(
        machine_identifier="plex-machine",
        account_id=17,
        watchlist=(plex_show("show-7102", 7102),),
    )
    store = FakeTvStateStore(
        managed=(
            managed("sonarr", 7101, "501"),
            managed("plex_watchlist", 7102, "show-7102"),
        )
    )

    state = TvSyncCollector(
        backend_client=FakeBackend(snapshot),
        sonarr_client=sonarr,
        plex_client=plex,
        state_store=store,
    ).collect()

    assert state.snapshot == snapshot
    assert state.sonarr.complete is True
    assert state.plex_watchlist.complete is True
    assert state.sonarr.items == sonarr.series
    assert state.sonarr_episodes[501] == sonarr.episodes[501]
    assert state.ownership == store.managed
    assert sonarr.calls == [
        ("get_all_series",),
        ("get_episodes", 501),
        ("get_episodes", 502),
    ]
    assert plex.calls == [("get_watchlist_shows",)]
~~~

The backend snapshot is fetched first. Sonarr episodes are fetched only for an
exact TVDB series corresponding to a snapshot show. No destination method with
add, update, monitor, search, or remove in its name may be called.

- [ ] **Step 2: Write boundary-failure and binding tests**

~~~python
def test_sonarr_failure_blocks_only_sonarr_collection():
    snapshot = snapshot_fixture()
    state = collector(
        snapshot=snapshot,
        sonarr_error=SonarrError("offline"),
        plex_watchlist=(),
    ).collect()

    assert state.sonarr.complete is False
    assert state.sonarr.error == "sonarr_collection_failed"
    assert state.plex_watchlist.complete is True
    assert state.snapshot_collection.complete is True


@pytest.mark.parametrize(
    ("machine", "account", "reason"),
    [
        ("other-machine", 17, "plex_machine_mismatch"),
        ("plex-machine", 99, "plex_account_mismatch"),
    ],
)
def test_collector_rejects_plex_binding_mismatch(machine, account, reason):
    state = collector(
        snapshot=snapshot_fixture(),
        plex_machine=machine,
        plex_account=account,
    ).collect()

    assert state.plex_watchlist.complete is False
    assert state.plex_watchlist.error == reason
~~~

Add tests for backend failure, per-series Sonarr episode failure, Plex failure,
ownership read failure, and a duplicate destination identity. Exception text
stored in collected state must be a stable reason, not the raw exception.

- [ ] **Step 3: Run collector tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_collector.py -q
Pop-Location
~~~

Expected: collection fails because TvSyncCollector and collected-state models do
not exist.

- [ ] **Step 4: Add collection models**

Append these types to tv_destination.py:

~~~python
@dataclass(frozen=True)
class BoundaryCollection:
    complete: bool
    count: int
    error: str | None
    items: tuple[Any, ...]


@dataclass(frozen=True)
class CollectedTvSyncState:
    snapshot: TvSyncSnapshot | None
    snapshot_collection: BoundaryCollection
    sonarr: BoundaryCollection
    sonarr_episodes: dict[int, tuple[SonarrEpisode, ...]]
    plex_watchlist: BoundaryCollection
    ownership: tuple[ManagedTvDestination, ...]
    ownership_complete: bool
    ownership_error: str | None

    @property
    def collection_errors(self) -> tuple[str, ...]:
        reasons = [
            collection.error
            for collection in (
                self.snapshot_collection,
                self.sonarr,
                self.plex_watchlist,
            )
            if not collection.complete and collection.error is not None
        ]
        if not self.ownership_complete and self.ownership_error is not None:
            reasons.append(self.ownership_error)
        return tuple(sorted(set(reasons)))
~~~

Use MappingProxyType or defensive copies for sonarr_episodes so a planner cannot
change collected state.

- [ ] **Step 5: Implement the side-effect-free collector**

Expose:

~~~python
class TvSyncCollector:
    def __init__(
        self,
        *,
        backend_client,
        sonarr_client,
        plex_client,
        state_store,
    ):
        self.backend_client = backend_client
        self.sonarr_client = sonarr_client
        self.plex_client = plex_client
        self.state_store = state_store

    def collect(self) -> CollectedTvSyncState:
        # Fetch backend first, then independently collect each destination.
        # Convert every caught exception to the stable reason assigned below.
~~~

Use these exact stable collection reasons:

~~~text
backend_snapshot_failed
sonarr_collection_failed
sonarr_episode_collection_failed
plex_watchlist_collection_failed
plex_machine_mismatch
plex_account_mismatch
worker_ownership_failed
~~~

When snapshot collection fails, Sonarr and Plex watchlist may still be collected
for operator visibility, but snapshot is null and no plan can be mutation
eligible. A single Sonarr episode failure makes the Sonarr boundary incomplete.
Plex binding compares client.machine_identifier and client.account_id with the
backend TvPlexHistoryBinding before accepting watchlist rows.

- [ ] **Step 6: Run the collector tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_collector.py -q
Pop-Location
~~~

Expected: all full, partial, binding, stable-error, and no-mutation tests pass.

- [ ] **Step 7: Commit the collector slice**

~~~powershell
git add workers/vod-filter/src/models/tv_destination.py workers/vod-filter/src/services/tv_sync_collector.py workers/vod-filter/tests/vod_filter/test_tv_sync_collector.py
git commit -m "feat(worker): collect live TV destination state"
~~~

### Task 6: Build The Deterministic Reversible Planner

**Files:**

- Create: workers/vod-filter/src/services/tv_sync_planner.py
- Create: workers/vod-filter/tests/vod_filter/test_tv_sync_planner.py
- Modify: workers/vod-filter/src/models/tv_destination.py

- [ ] **Step 1: Write active-show Sonarr planning tests**

~~~python
def test_active_missing_show_plans_exact_series_add_only():
    state = collected_state(
        shows=(tv_show(tvdb_id=7101, lifecycle="active"),),
        sonarr_series=(),
        plex_watchlist=(),
    )

    plan = build_tv_plan(state, now=NOW)

    assert actions(plan, destination="sonarr") == [
        decision(
            action_id="sonarr:add_series:7101",
            action_type="add_series",
            tvdb_id=7101,
            target_id=None,
            reason="desired_sonarr_series_missing",
            payload={
                "trakt_id": 101,
                "title": "Weekly Show",
                "lifecycle_version": 4,
            },
        )
    ]
    assert "search_episodes" not in action_types(plan)
~~~

The first add intentionally performs no search. Sonarr must expose its generated
episode IDs on the next collection before an exact EpisodeSearch can be
authorized.

- [ ] **Step 2: Write monitoring and targeted-search tests**

~~~python
def test_existing_show_plans_monitoring_and_only_exact_missing_aired_episode():
    state = collected_state(
        shows=(
            tv_show(
                tvdb_id=7101,
                seasons=(
                    tv_season(0, monitored=False, search=()),
                    tv_season(1, monitored=True, search=(3,)),
                ),
            ),
        ),
        sonarr_series=(
            sonarr_series(
                series_id=501,
                tvdb_id=7101,
                monitored=False,
                monitor_new_items="none",
                seasons=(
                    SonarrSeason(0, True),
                    SonarrSeason(1, False),
                ),
            ),
        ),
        sonarr_episodes={
            501: (
                sonarr_episode(
                    episode_id=7003,
                    series_id=501,
                    season=1,
                    episode=3,
                    aired_at=NOW - timedelta(days=1),
                    has_file=False,
                ),
                sonarr_episode(
                    episode_id=7004,
                    series_id=501,
                    season=1,
                    episode=4,
                    aired_at=NOW + timedelta(days=6),
                    has_file=False,
                ),
                sonarr_episode(
                    episode_id=7099,
                    series_id=501,
                    season=0,
                    episode=1,
                    aired_at=NOW - timedelta(days=30),
                    has_file=False,
                ),
            )
        },
        managed=(managed("sonarr", 7101, "501"),),
    )

    plan = build_tv_plan(state, now=NOW)

    assert payload(plan, "sonarr:ensure_monitoring:7101") == {
        "series_id": 501,
        "season_monitoring": {"0": False, "1": True},
    }
    assert payload(plan, "sonarr:monitor_episodes:7101") == {
        "series_id": 501,
        "episode_ids": [7003],
    }
    assert payload(plan, "sonarr:search_episodes:7101") == {
        "series_id": 501,
        "episode_ids": [7003],
    }
~~~

Add cases proving:

- an episode omitted from searchAiredUnwatchedEpisodes is never searched;
- an episode with a file is not searched;
- a future or unknown Sonarr air date is not searched;
- season 0 is never monitored or searched;
- ambiguous duplicate Sonarr S/E coordinates produce
  sonarr_episode_mapping_ambiguous;
- a missing requested Sonarr episode produces sonarr_episode_mapping_missing;
- a cleaned season with monitoredDesired true plans re-monitoring; and
- source_removed plans no Sonarr mutation.

- [ ] **Step 3: Write Plex lifecycle and adoption-candidate tests**

~~~python
def test_caught_up_show_removes_only_owned_plex_entry():
    state = collected_state(
        shows=(tv_show(tvdb_id=7102, lifecycle="caught_up", plex_desired=False),),
        plex_watchlist=(plex_show("show-7102", 7102),),
        managed=(managed("plex_watchlist", 7102, "show-7102"),),
    )

    plan = build_tv_plan(state, now=NOW)

    assert actions(plan, destination="plex_watchlist") == [
        decision(
            action_id="plex_watchlist:remove:7102",
            action_type="remove_watchlist",
            tvdb_id=7102,
            target_id="show-7102",
            reason="caught_up_plex_watchlist_not_desired",
            managed=True,
        )
    ]


def test_unmanaged_exact_rows_are_adoption_candidates_not_silent_ownership():
    state = collected_state(
        shows=(tv_show(tvdb_id=7101, plex_desired=True),),
        sonarr_series=(sonarr_series(501, 7101),),
        plex_watchlist=(plex_show("show-7101", 7101),),
        managed=(),
    )

    plan = build_tv_plan(state, now=NOW)

    assert adoption_ids(plan) == [
        "ownership:adopt:plex_watchlist:7101",
        "ownership:adopt:sonarr:7101",
    ]
    assert all(
        decision.requires_adoption
        for decision in plan.decisions
        if decision.destination in {
            TvDestination.SONARR,
            TvDestination.PLEX_WATCHLIST,
        }
        and decision.target_id is not None
    )
~~~

Add tests for active Plex add, explicit Trakt watchlist add, newly aired
restoration, already-present managed keep, source_removed owned removal,
unmanaged desired-false adoption candidate, conflicting ownership target,
missing/conflicting TVDB identity, and alternate Plex ID matching only after
backend identityStatus verified.

- [ ] **Step 4: Write cleanup-suppression and deterministic-order tests**

~~~python
def test_cleanup_authorizations_are_reported_but_never_planned_for_execution():
    state = collected_state(
        cleanup_authorizations=(
            cleanup_authorization("season-event", "season_files"),
            cleanup_authorization("terminal-event", "terminal_series"),
        )
    )

    first = build_tv_plan(state, now=NOW)
    second = build_tv_plan(state, now=NOW)

    assert first == second
    assert [
        (item.event_id, item.reason)
        for item in first.suppressed_cleanups
    ] == [
        ("season-event", "cleanup_phase_disabled"),
        ("terminal-event", "cleanup_phase_disabled"),
    ]
    assert all(not decision.destructive for decision in first.decisions)
~~~

- [ ] **Step 5: Run planner tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_planner.py -q
Pop-Location
~~~

Expected: collection fails because planner models and build_tv_plan do not
exist.

- [ ] **Step 6: Add decision and plan models**

Append:

~~~python
class TvDestination(str, Enum):
    SONARR = "sonarr"
    PLEX_WATCHLIST = "plex_watchlist"
    OWNERSHIP = "ownership"


class TvActionType(str, Enum):
    ADD_SERIES = "add_series"
    ENSURE_MONITORING = "ensure_monitoring"
    MONITOR_EPISODES = "monitor_episodes"
    SEARCH_EPISODES = "search_episodes"
    ADD_WATCHLIST = "add_watchlist"
    REMOVE_WATCHLIST = "remove_watchlist"
    ADOPT = "adopt"
    RELEASE_OWNERSHIP = "release_ownership"
    KEEP = "keep"
    SKIP = "skip"


@dataclass(frozen=True)
class TvDecision:
    action_id: str
    destination: TvDestination
    action_type: TvActionType
    trakt_id: int
    tvdb_id: int
    target_id: str | None
    lifecycle_version: int
    reason: str
    managed: bool
    requires_adoption: bool
    destructive: bool
    payload: dict[str, Any]


@dataclass(frozen=True)
class SuppressedTvCleanup:
    event_id: str
    action_type: str
    reason: str


@dataclass(frozen=True)
class TvSyncPlan:
    generation_id: str | None
    decisions: tuple[TvDecision, ...]
    suppressed_cleanups: tuple[SuppressedTvCleanup, ...]
    source_counts: dict[str, int]
~~~

- [ ] **Step 7: Implement the pure planner**

Expose:

~~~text
def build_tv_plan(
    state: CollectedTvSyncState,
    *,
    now: datetime,
) -> TvSyncPlan
~~~

Implement the function with pure indexing and decision construction. It may
not call a client or state-store mutation. Require timezone-aware now. Index
snapshot shows and Sonarr by TVDB, Plex rows by exact verified identity, and
ownership by destination/TVDB. Create stable action IDs exactly as used in the
tests. Sort decisions by:

~~~python
DESTINATION_ORDER = {
    TvDestination.OWNERSHIP: 0,
    TvDestination.SONARR: 1,
    TvDestination.PLEX_WATCHLIST: 2,
}

ACTION_ORDER = {
    TvActionType.ADOPT: 0,
    TvActionType.ADD_SERIES: 1,
    TvActionType.ENSURE_MONITORING: 2,
    TvActionType.MONITOR_EPISODES: 3,
    TvActionType.SEARCH_EPISODES: 4,
    TvActionType.ADD_WATCHLIST: 5,
    TvActionType.REMOVE_WATCHLIST: 6,
    TvActionType.RELEASE_OWNERSHIP: 7,
    TvActionType.KEEP: 8,
    TvActionType.SKIP: 9,
}
~~~

The final key is destination order, TVDB ID, action order, action ID. Every
decision has destructive false. Cleanup authorizations become
SuppressedTvCleanup rows only.

- [ ] **Step 8: Run all planner tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_planner.py -q
Pop-Location
~~~

Expected: all active, caught-up, source-removed, reactivation, adoption,
identity, exact-search, cleanup-suppression, and deterministic-order tests pass.

- [ ] **Step 9: Commit the planner slice**

~~~powershell
git add workers/vod-filter/src/models/tv_destination.py workers/vod-filter/src/services/tv_sync_planner.py workers/vod-filter/tests/vod_filter/test_tv_sync_planner.py
git commit -m "feat(worker): plan reversible TV destinations"
~~~

### Task 7: Apply Per-Boundary Phase 3 Safety Policy

**Files:**

- Create: workers/vod-filter/src/services/tv_sync_policy.py
- Create: workers/vod-filter/tests/vod_filter/test_tv_sync_policy.py
- Modify: workers/vod-filter/src/models/tv_destination.py

- [ ] **Step 1: Write report-only, freshness, and source-health tests**

~~~python
def test_report_only_marks_reversible_actions_dry_run():
    plan, state = fresh_plan_and_state()
    evaluation = evaluate_tv_plan(
        plan,
        state,
        TvSyncPolicy(allow_mutation=False, allow_adoption=False),
        now=NOW,
    )

    assert evaluation.global_blockers == ("mutation_disabled",)
    assert all(
        item.status == "dry_run"
        for item in evaluation.decisions
        if item.decision.action_type is not TvActionType.KEEP
    )


@pytest.mark.parametrize(
    ("change", "reason"),
    [
        (
            lambda snapshot: replace(snapshot, mutation_capable=False),
            "snapshot_not_mutation_capable",
        ),
        (
            lambda snapshot: replace(
                snapshot,
                published_at=NOW - timedelta(minutes=31),
            ),
            "snapshot_stale",
        ),
    ],
)
def test_source_gate_blocks_all_external_mutation(change, reason):
    plan, state = fresh_plan_and_state(snapshot_change=change)
    evaluation = evaluate_tv_plan(
        plan,
        state,
        TvSyncPolicy(allow_mutation=True, allow_adoption=True),
        now=NOW,
    )

    assert reason in evaluation.global_blockers
    assert no_external_decision_is_eligible(evaluation)
~~~

Also cover missing snapshot, future publishedAt, and unsupported schema caught
at the parser boundary.

- [ ] **Step 2: Write independent destination-boundary tests**

~~~python
def test_sonarr_failure_does_not_block_healthy_plex_action():
    plan, state = plan_with_sonarr_and_plex_actions(
        sonarr_complete=False,
        plex_complete=True,
    )
    evaluation = evaluate_tv_plan(
        plan,
        state,
        TvSyncPolicy(allow_mutation=True, allow_adoption=True),
        now=NOW,
    )

    assert status_for(evaluation, "sonarr:add_series:7101") == "blocked"
    assert reasons_for(evaluation, "sonarr:add_series:7101") == (
        "sonarr_collection_incomplete",
    )
    assert status_for(evaluation, "plex_watchlist:add:7101") == "eligible"
~~~

Add the inverse Plex failure test, ownership-read failure, show-level
identity_missing_tvdb and identity_conflict blockers, and an action whose
stored ownership target differs from the live target.

- [ ] **Step 3: Write adoption and destructive-disable tests**

~~~python
def test_adoption_requires_both_apply_and_explicit_adoption_gate():
    plan, state = plan_with_adoption_candidate()

    disabled = evaluate_tv_plan(
        plan,
        state,
        TvSyncPolicy(allow_mutation=True, allow_adoption=False),
        now=NOW,
    )
    enabled = evaluate_tv_plan(
        plan,
        state,
        TvSyncPolicy(allow_mutation=True, allow_adoption=True),
        now=NOW,
    )

    assert reasons_for(
        disabled,
        "ownership:adopt:sonarr:7101",
    ) == ("adoption_disabled",)
    assert status_for(
        enabled,
        "ownership:adopt:sonarr:7101",
    ) == "eligible"


def test_phase_3_never_makes_cleanup_authorization_executable():
    plan, state = plan_with_cleanup_authorizations()
    evaluation = evaluate_tv_plan(
        plan,
        state,
        TvSyncPolicy(
            allow_mutation=True,
            allow_adoption=True,
            allow_season_file_deletion=True,
            allow_terminal_series_deletion=True,
            allow_no_recycle_bin_delete=True,
        ),
        now=NOW,
    )

    assert evaluation.suppressed_cleanup_reasons == {
        "season-event": "cleanup_phase_disabled",
        "terminal-event": "cleanup_phase_disabled",
    }
    assert all(not item.decision.destructive for item in evaluation.decisions)
~~~

- [ ] **Step 4: Run policy tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_policy.py -q
Pop-Location
~~~

Expected: collection fails because TvSyncPolicy and policy-result models do not
exist.

- [ ] **Step 5: Add policy result models**

Append:

~~~python
@dataclass(frozen=True)
class TvDecisionEvaluation:
    decision: TvDecision
    status: str
    blockers: tuple[str, ...]


@dataclass(frozen=True)
class TvPolicyEvaluation:
    generation_id: str | None
    global_blockers: tuple[str, ...]
    decisions: tuple[TvDecisionEvaluation, ...]
    suppressed_cleanup_reasons: dict[str, str]


@dataclass(frozen=True)
class TvSyncPolicy:
    allow_mutation: bool = False
    allow_adoption: bool = False
    max_snapshot_age_minutes: int = 30
    allow_season_file_deletion: bool = False
    allow_terminal_series_deletion: bool = False
    allow_no_recycle_bin_delete: bool = False

    def __post_init__(self) -> None:
        if self.max_snapshot_age_minutes < 1:
            raise ValueError("max_snapshot_age_minutes must be at least 1")
~~~

- [ ] **Step 6: Implement policy evaluation**

Expose:

~~~text
def evaluate_tv_plan(
    plan: TvSyncPlan,
    state: CollectedTvSyncState,
    policy: TvSyncPolicy,
    *,
    now: datetime,
) -> TvPolicyEvaluation
~~~

Implement the function with these exact rules:

1. Missing snapshot adds snapshot_unavailable.
2. mutationCapable false adds snapshot_not_mutation_capable.
3. Published age above max adds snapshot_stale; a future timestamp adds
   snapshot_from_future.
4. allow_mutation false adds mutation_disabled and returns external decisions
   as dry_run rather than failed.
5. Sonarr actions require a complete Sonarr boundary and complete ownership.
6. Plex actions require a complete Plex boundary and complete ownership.
7. Adoption requires allow_adoption and allow_mutation.
8. An action requiring adoption is blocked until its adoption decision is
   eligible in the same evaluation or matching ownership already exists.
9. KEEP and SKIP decisions are skipped and make no client call.
10. Every suppressed cleanup reason remains cleanup_phase_disabled regardless
    of destructive configuration values.

Sort and deduplicate all blocker tuples. Policy must not mutate state or plan.

- [ ] **Step 7: Run policy tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_policy.py -q
Pop-Location
~~~

Expected: all report-only, apply, freshness, boundary, identity, ownership,
adoption, and destructive-disable tests pass.

- [ ] **Step 8: Commit the policy slice**

~~~powershell
git add workers/vod-filter/src/models/tv_destination.py workers/vod-filter/src/services/tv_sync_policy.py workers/vod-filter/tests/vod_filter/test_tv_sync_policy.py
git commit -m "feat(worker): gate reversible TV actions"
~~~

### Task 8: Execute Only Policy-Eligible Reversible Actions

**Files:**

- Create: workers/vod-filter/src/services/tv_destination_executor.py
- Create: workers/vod-filter/tests/vod_filter/test_tv_destination_executor.py
- Modify: workers/vod-filter/src/models/tv_destination.py

- [ ] **Step 1: Write dry-run and blocked-action tests**

~~~python
def test_executor_dry_run_calls_no_client_or_ownership_mutation():
    sonarr = RecordingSonarr()
    plex = RecordingPlex()
    store = RecordingStore()
    evaluation = report_only_evaluation()

    result = TvDestinationExecutor(sonarr, plex, store).execute(
        run_id="run-1",
        evaluation=evaluation,
        apply=False,
        shows_by_tvdb=shows_by_tvdb(),
        sonarr_by_tvdb=sonarr_by_tvdb(),
    )

    assert sonarr.calls == []
    assert plex.calls == []
    assert store.mutations == []
    assert {item.status for item in result.actions} == {"dry_run", "skipped"}
~~~

Add a test proving policy-blocked decisions call no mutation and report their
stable blocker.

- [ ] **Step 2: Write Sonarr execution tests**

~~~python
def test_executor_adds_exact_series_then_records_ownership():
    sonarr = RecordingSonarr(add_result=sonarr_series(501, 7101))
    store = RecordingStore()

    result = executor(sonarr=sonarr, store=store).execute(
        run_id="run-1",
        evaluation=eligible_evaluation("sonarr:add_series:7101"),
        apply=True,
        shows_by_tvdb=shows_by_tvdb(),
        sonarr_by_tvdb={},
    )

    assert sonarr.calls == [
        (
            "add_series",
            {
                "tvdb_id": 7101,
                "root_folder": "/tv",
                "quality_profile_id": 3,
                "series_type": "standard",
                "language_profile_id": None,
            },
        )
    ]
    assert store.management_calls == [
        ("sonarr", 7101, "501", "added", 4, "add_series")
    ]
    assert result.actions[0].status == "completed"
~~~

Add tests for ensure monitoring, monitor exact episode IDs, EpisodeSearch exact
IDs, and add returning no exact series. Verify action audit is started before
the external call and completed immediately after it.

- [ ] **Step 3: Write adoption dependency and Plex lifecycle tests**

~~~python
def test_successful_adoption_precedes_dependent_monitoring():
    store = RecordingStore()
    sonarr = RecordingSonarr()
    evaluation = eligible_adoption_and_monitoring_evaluation()

    executor(sonarr=sonarr, store=store).execute(
        run_id="run-1",
        evaluation=evaluation,
        apply=True,
        shows_by_tvdb=shows_by_tvdb(),
        sonarr_by_tvdb={7101: sonarr_series(501, 7101)},
    )

    assert store.management_calls[0] == (
        "sonarr",
        7101,
        "501",
        "adopted",
        4,
        "adopt",
    )
    assert sonarr.calls[0][0] == "update_series_monitoring"


def test_owned_caught_up_show_is_removed_from_plex_and_released():
    plex = RecordingPlex(remove_result=True)
    store = RecordingStore()

    executor(plex=plex, store=store).execute(
        run_id="run-1",
        evaluation=eligible_evaluation("plex_watchlist:remove:7102"),
        apply=True,
        shows_by_tvdb=shows_by_tvdb(),
        sonarr_by_tvdb={},
    )

    assert plex.calls == [("remove_show_from_watchlist", 7102)]
    assert store.release_calls == [("plex_watchlist", 7102)]
~~~

Add Plex add/already-present behavior, Plex adoption, ownership target conflict,
adoption failure blocking dependent action, and independent continuation after
one destination error.

- [ ] **Step 4: Assert no cleanup or Plex-library surface exists**

~~~python
def test_phase_3_executor_has_no_destructive_surface():
    forbidden = {
        "claim_cleanup",
        "report_cleanup_result",
        "delete_episode_file",
        "delete_series",
        "delete_from_library",
    }
    assert forbidden.isdisjoint(dir(TvDestinationExecutor))
~~~

- [ ] **Step 5: Run executor tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_destination_executor.py -q
Pop-Location
~~~

Expected: collection fails because TvDestinationExecutor and execution-result
models do not exist.

- [ ] **Step 6: Add execution result models**

Append:

~~~python
@dataclass(frozen=True)
class TvActionResult:
    action_id: str
    destination: str
    action_type: str
    tvdb_id: int
    target_id: str | None
    status: str
    reason: str | None


@dataclass(frozen=True)
class TvExecutionResult:
    generation_id: str | None
    actions: tuple[TvActionResult, ...]
    errors: tuple[str, ...]
~~~

- [ ] **Step 7: Implement the reversible dispatcher**

TvDestinationExecutor receives the configured Sonarr settings at construction
and dispatches only these action types:

~~~python
REVERSIBLE_ACTIONS = {
    TvActionType.ADOPT,
    TvActionType.ADD_SERIES,
    TvActionType.ENSURE_MONITORING,
    TvActionType.MONITOR_EPISODES,
    TvActionType.SEARCH_EPISODES,
    TvActionType.ADD_WATCHLIST,
    TvActionType.REMOVE_WATCHLIST,
    TvActionType.RELEASE_OWNERSHIP,
}
~~~

Before each eligible applied action call
state_store.record_action_started. On success immediately call
record_action_result with completed; on a caught exception call it with failed
and a stable reason, then continue with independent decisions. Track failed
adoption keys and skip their dependent actions with adoption_failed.

ADD_SERIES calls SonarrClient.add_series and records ownership_kind added.
ADOPT records the exact live target with ownership_kind adopted and performs no
external call. ADD_WATCHLIST records the target ID returned by Plex.
REMOVE_WATCHLIST releases ownership only after true or already-absent
convergence. RELEASE_OWNERSHIP changes SQLite only.

Raise RuntimeError before dispatch if a decision has destructive true or an
action type is outside REVERSIBLE_ACTIONS, KEEP, and SKIP.

- [ ] **Step 8: Run executor tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_destination_executor.py -q
Pop-Location
~~~

Expected: all dry-run, block, Sonarr, Plex, adoption, partial-failure, audit,
and destructive-surface tests pass.

- [ ] **Step 9: Commit the executor slice**

~~~powershell
git add workers/vod-filter/src/models/tv_destination.py workers/vod-filter/src/services/tv_destination_executor.py workers/vod-filter/tests/vod_filter/test_tv_destination_executor.py
git commit -m "feat(worker): execute reversible TV actions"
~~~

### Task 9: Persist Protected Worker Run Summaries In The Backend

**Files:**

- Create: backend/src/Watchlist.Application/ITvWorkerRunRepository.cs
- Create: backend/src/Watchlist.Application/TvWorkerRunService.cs
- Create: backend/src/Watchlist.Application/TvWorkerRunSummaryRequestDto.cs
- Create: backend/src/Watchlist.Application/TvWorkerRunSummaryResponseDto.cs
- Create: backend/src/Watchlist.Application/TvWorkerCollectionSummaryDto.cs
- Create: backend/src/Watchlist.Application/TvWorkerGateSummaryDto.cs
- Create: backend/src/Watchlist.Application/TvWorkerActionSummaryDto.cs
- Create: backend/src/Watchlist.Infrastructure/MongoTvWorkerRunDocument.cs
- Create: backend/src/Watchlist.Infrastructure/MongoTvWorkerRunRepository.cs
- Modify: backend/src/Watchlist.Infrastructure/MongoDbOptions.cs
- Modify: backend/src/Watchlist.Infrastructure/MongoTvIndexHostedService.cs
- Modify: backend/src/Watchlist.Infrastructure/DependencyInjection.cs
- Modify: backend/src/Watchlist.Api/TvEndpointRouteBuilderExtensions.cs
- Test: backend/tests/Watchlist.Application.Tests/TvWorkerRunServiceTests.cs
- Test: backend/tests/Watchlist.Application.Tests/MongoTvWorkerRunRepositoryTests.cs
- Test: backend/tests/Watchlist.Api.Tests/TvWorkerApiTests.cs

- [ ] **Step 1: Write failing service tests for immutable current-generation runs**

Use the Phase 1 published-generation repository fake and a recording worker-run
repository. Assert a valid summary for the currently published generation is
accepted once, stores one immutable document, and returns its durable acceptance
time:

~~~csharp
var response = await service.AcceptAsync(
    TvWorkerRunFixtures.ValidRequest(
        runId: "run-1",
        generationId: "tv-generation-42"),
    Now,
    CancellationToken.None);

response.Should().Be(new TvWorkerRunSummaryResponseDto(
    "run-1",
    Now));
repository.Documents.Should().ContainSingle();
repository.Documents[0].GenerationId.Should().Be("tv-generation-42");
~~~

Add tests proving:

- the same `runId` plus the same canonical body is an idempotent retry that
  returns the original `acceptedAt` and inserts no second document;
- the same `runId` plus a changed body fails with
  `worker_run_id_conflict` and leaves the original document unchanged;
- a non-current, unknown, blank, or malformed generation ID fails before write;
- `finishedAt >= startedAt`, both values are non-default, and duration is no
  longer than 24 hours;
- mode is exactly `report_only` or `apply`, and status is exactly `success`,
  `partial`, `blocked`, or `failed`;
- run/worker/action/event/target identifiers and blockers are nonblank,
  bounded, and unique where the contract requires uniqueness;
- counts are nonnegative and destructive gate values agree with the phase
  available in the deployed backend; and
- an exact forbidden field name at any JSON depth is rejected before DTO
  deserialization while `terminalPathFingerprint`, `pathCount`,
  `predicateHash`, and `inventoryHash` are accepted.

- [ ] **Step 2: Define the exact version-1 request and response records**

Use camelCase JSON and annotate every request record with
`[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]`:

~~~csharp
public sealed record TvWorkerCollectionSummaryDto(
    bool Complete,
    int Count);

public sealed record TvWorkerGateSummaryDto(
    bool SnapshotFresh,
    bool MutationCapable,
    bool ApplyEnabled,
    bool AdoptionEnabled,
    bool SeasonFileDeletion,
    bool TerminalSeriesDeletion,
    bool NoRecycleBinDelete,
    int SeasonCleanupAuthorizationCount,
    int TerminalCleanupAuthorizationCount,
    int MaxReversibleActionsPerRun,
    int MaxEpisodeSearchesPerShow);

public sealed record TvWorkerActionSummaryDto(
    string ActionId,
    string? EventId,
    string Type,
    string Target,
    string Status,
    string? Reason);

public sealed record TvWorkerRunSummaryRequestDto(
    string RunId,
    string WorkerId,
    string GenerationId,
    string Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string Status,
    IReadOnlyDictionary<string, TvWorkerCollectionSummaryDto> Collections,
    TvWorkerGateSummaryDto Gates,
    IReadOnlyList<TvWorkerActionSummaryDto> Actions,
    IReadOnlyList<string> Blockers);

public sealed record TvWorkerRunSummaryResponseDto(
    string RunId,
    DateTimeOffset AcceptedAt);
~~~

Collection names are an allowlisted forward-compatible set:
`snapshot`, `sonarr`, `plexWatchlist`, `ownership`, `plexLibrary`, and
`pathInventory`. Phase 3 emits only the first four. Action targets are stable
logical identifiers or exact destination IDs, never titles or filesystem paths.

- [ ] **Step 3: Run the service tests and verify failure**

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~TvWorkerRunServiceTests"
~~~

Expected: compile failure because the DTOs, repository contract, and service do
not exist.

- [ ] **Step 4: Implement strict validation, canonical hashing, and idempotency**

`TvWorkerRunService` must load the published generation pointer and reject a
request unless `GenerationId` equals that exact pointer. Serialize the validated
typed DTO with the shared web JSON options, sort all dictionary keys and stable
arrays, then store a lowercase SHA-256 `RequestHash`. Do not hash or persist the
sync key.

Before typed deserialization, the endpoint walks the request `JsonElement` and
rejects the shared exact case-insensitive keys `token`, `accessToken`, `refreshToken`, `apiKey`, `api_key`, `password`, `secret`, `clientSecret`, `plexToken`, `sonarrApiKey`, `syncKey`, `mongoConnectionString`, `rawPath`, `mediaPath`, `media_path`, `filesystemPath`, `filesystem_path`, `responseBody`, and `response_body`. It tests every name at root and nested dictionary/array depths, then deserializes with unmapped-member rejection and
passes the DTO to the service. This is the same denylist used by the worker.

`ITvWorkerRunRepository.InsertOrGetAsync` atomically returns `inserted`,
`identical_retry`, or `conflict`; a retry may never update the stored body or
acceptance time. Map validation to 400 and stale/current-generation conflicts
to 409 with stable problem codes.

- [ ] **Step 5: Write and run Mongo persistence tests**

Create a Mongo document with `RunId` as the stable `_id`, `RequestHash`, the
validated fields, and `AcceptedAt`. Add
`TvWorkerRunsCollectionName = "tv_worker_runs"` to `MongoDbOptions` and a
descending `{ generationId, acceptedAt }` index in
`MongoTvIndexHostedService`. Tests must prove atomic first insert, identical
retry, changed-body conflict, and persistence after repository reconstruction.

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --filter "FullyQualifiedName~MongoTvWorkerRunRepositoryTests"
~~~

Expected: all Mongo worker-run repository tests pass.

- [ ] **Step 6: Add and test the protected API endpoint**

Map `POST /api/worker/tv/runs` through
`TvEndpointRouteBuilderExtensions`. Require the existing
`X-Watchlist-Sync-Key` boundary. A successful first insert or identical retry
returns exactly:

~~~http
HTTP/1.1 202 Accepted
Content-Type: application/json

{"runId":"run-1","acceptedAt":"2026-07-13T12:00:04+00:00"}
~~~

API tests cover missing/wrong key 401, malformed or redaction-unsafe body 400,
stale generation 409, changed-body retry 409, and successful/identical retry
202. Assert no response echoes a submitted action reason or raw request body.

Run:

~~~powershell
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --filter "FullyQualifiedName~TvWorkerApiTests"
~~~

Expected: all protected run-summary API tests pass.

- [ ] **Step 7: Commit the backend run-ledger slice**

~~~powershell
git add backend/src/Watchlist.Application backend/src/Watchlist.Infrastructure backend/src/Watchlist.Api/TvEndpointRouteBuilderExtensions.cs backend/tests/Watchlist.Application.Tests backend/tests/Watchlist.Api.Tests/TvWorkerApiTests.cs
git commit -m "feat: persist TV worker run summaries"
~~~

### Task 10: Produce Redacted Local Reports And Backend Run Summaries

**Files:**

- Create: workers/vod-filter/src/services/tv_sync_report.py
- Create: workers/vod-filter/tests/vod_filter/test_tv_sync_report.py
- Modify: workers/vod-filter/src/clients/tv_backend_client.py
- Modify: workers/vod-filter/tests/vod_filter/test_tv_backend_client.py

- [ ] **Step 1: Write JSON, Markdown, and redaction tests**

~~~python
def test_reports_include_generation_boundaries_gates_and_actions(tmp_path):
    paths, summary = write_tv_sync_reports(
        report_dir=tmp_path,
        run_id="run-1",
        worker_id="worker-1",
        started_at=NOW,
        finished_at=NOW + timedelta(seconds=4),
        state=collected_state_fixture(),
        evaluation=policy_evaluation_fixture(),
        execution=execution_result_fixture(),
        apply=False,
    )

    payload = json.loads(paths.json_path.read_text(encoding="utf-8"))
    assert payload["generationId"] == "tv-generation-42"
    assert payload["mode"] == "report_only"
    assert payload["collections"]["sonarr"] == {
        "complete": True,
        "count": 2,
    }
    assert payload["gates"]["seasonFileDeletion"] is False
    assert payload["gates"]["terminalSeriesDeletion"] is False
    assert payload["gates"]["seasonCleanupAuthorizationCount"] == 0
    assert payload["gates"]["terminalCleanupAuthorizationCount"] == 0
    assert summary["runId"] == "run-1"
    assert "## Proposed and executed actions" in paths.markdown_path.read_text(
        encoding="utf-8"
    )


def test_reports_redact_secrets_and_full_paths(tmp_path):
    paths, summary = report_with_values(
        tmp_path,
        token="plex-secret-token",
        api_key="sonarr-secret-key",
        media_path="/srv/media/tv/Weekly Show/episode.mkv",
    )
    rendered = (
        paths.json_path.read_text(encoding="utf-8")
        + paths.markdown_path.read_text(encoding="utf-8")
        + json.dumps(summary)
    )

    assert "plex-secret-token" not in rendered
    assert "sonarr-secret-key" not in rendered
    assert "/srv/media" not in rendered
    assert "terminalPathFingerprint" in rendered
    assert "pathCount" in rendered
~~~

The report includes adoption candidates, managed counts, deterministic reasons,
collection completeness/counts, snapshot health/freshness, action statuses,
fixed destructive gate states, cleanup authorization counts/caps, and the exact
generation ID.

- [ ] **Step 2: Write the protected backend summary test**

~~~python
def test_post_run_summary_uses_sync_key_and_redacted_contract():
    captured: dict = {}

    def handler(request: httpx.Request) -> httpx.Response:
        captured.update(json.loads(request.content))
        assert request.method == "POST"
        assert request.url.path == "/api/worker/tv/runs"
        assert request.headers["X-Watchlist-Sync-Key"] == "sync-key"
        return httpx.Response(
            202,
            json={
                "runId": "run-1",
                "acceptedAt": "2026-07-13T12:00:04Z",
            },
        )

    client = tv_backend_client(handler)
    client.post_run_summary(run_summary_fixture())

    assert captured["mode"] == "report_only"
    assert captured["generationId"] == "tv-generation-42"
    assert set(captured) == {
        "runId",
        "workerId",
        "generationId",
        "mode",
        "startedAt",
        "finishedAt",
        "status",
        "collections",
        "gates",
        "actions",
        "blockers",
    }
~~~

Add an HTTP failure test whose exception omits the response body.

- [ ] **Step 3: Run report/client tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_report.py tests\vod_filter\test_tv_backend_client.py -q
Pop-Location
~~~

Expected: report tests fail because tv_sync_report does not exist and client
tests fail because post_run_summary is absent.

- [ ] **Step 4: Implement report creation**

Expose:

~~~text
@dataclass(frozen=True)
class TvSyncReportPaths:
    json_path: Path
    markdown_path: Path


def write_tv_sync_reports(
    *,
    report_dir: Path,
    run_id: str,
    worker_id: str,
    started_at: datetime,
    finished_at: datetime,
    state: CollectedTvSyncState,
    evaluation: TvPolicyEvaluation,
    execution: TvExecutionResult,
    apply: bool,
) -> tuple[TvSyncReportPaths, dict]
~~~

Implement the function with one canonical camelCase summary dictionary.
Write it as UTF-8 sorted/indented JSON. Render Markdown from the same dictionary,
not from a second calculation. Use action IDs/TVDB IDs/target IDs only; never
include config objects, raw exception strings, raw HTTP bodies, Sonarr resource
dictionaries, or Plex objects.

- [ ] **Step 5: Implement post_run_summary**

Add:

~~~python
def post_run_summary(self, summary: dict[str, Any]) -> None:
    headers = {"X-Watchlist-Sync-Key": self.sync_key}
    response = self.http_client.post(
        f"{self.base_url}/api/worker/tv/runs",
        headers=headers,
        json=summary,
    )
    try:
        response.raise_for_status()
    except httpx.HTTPStatusError as error:
        raise TvBackendError(
            f"watchlist-app TV run report failed: HTTP {response.status_code}"
        ) from error
~~~

Require status 202 and validate that the response contains exactly the submitted
`runId` plus a timezone-aware `acceptedAt`; a mismatched run ID is a protocol
error. Validate the exact request top-level keys and recursively reject only the
shared exact case-insensitive denylist `token`, `accessToken`, `refreshToken`, `apiKey`, `api_key`, `password`, `secret`, `clientSecret`, `plexToken`, `sonarrApiKey`, `syncKey`, `mongoConnectionString`, `rawPath`, `mediaPath`, `media_path`, `filesystemPath`, `filesystem_path`, `responseBody`, and `response_body`.
Explicitly test that `terminalPathFingerprint`, `pathCount`, `predicateHash`,
and `inventoryHash` are accepted. Never reject a safe derived field merely
because its key contains the substring `path`.

- [ ] **Step 6: Run report/client tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_sync_report.py tests\vod_filter\test_tv_backend_client.py -q
Pop-Location
~~~

Expected: all local report, redaction, summary-shape, authorization, and HTTP
failure tests pass.

- [ ] **Step 7: Commit the reporting slice**

~~~powershell
git add workers/vod-filter/src/services/tv_sync_report.py workers/vod-filter/tests/vod_filter/test_tv_sync_report.py workers/vod-filter/src/clients/tv_backend_client.py workers/vod-filter/tests/vod_filter/test_tv_backend_client.py
git commit -m "feat(worker): report reversible TV sync runs"
~~~

### Task 11: Wire Safe Configuration And The Single-Run TV CLI

**Files:**

- Modify: workers/vod-filter/src/config.py
- Modify: workers/vod-filter/tests/vod_filter/test_config.py
- Create: workers/vod-filter/sync_tv.py
- Create: workers/vod-filter/tests/vod_filter/test_sync_tv_cli.py

- [ ] **Step 1: Write disabled-default and conditional-requirement tests**

~~~python
def test_tv_phase_3_defaults_are_safe():
    config = Config()

    assert config.tv_sync_enabled is False
    assert config.tv_sync_apply is False
    assert config.tv_sync_adopt_existing_destinations is False
    assert config.tv_sync_allow_season_file_deletion is False
    assert config.tv_sync_allow_terminal_series_deletion is False
    assert config.tv_sync_allow_no_recycle_bin_delete is False
    assert config.tv_sync_interval_seconds == 900
    assert config.tv_sync_max_snapshot_age_minutes == 30


def test_disabled_tv_does_not_require_sonarr(monkeypatch):
    monkeypatch.setenv("TV_SYNC_ENABLED", "false")
    monkeypatch.delenv("SONARR_URL", raising=False)
    monkeypatch.delenv("SONARR_API_KEY", raising=False)

    Config().validate()


def test_enabled_tv_requires_exact_destination_configuration(monkeypatch):
    monkeypatch.setenv("TV_SYNC_ENABLED", "true")
    monkeypatch.delenv("SONARR_URL", raising=False)

    with pytest.raises(ConfigurationError, match="SONARR_URL"):
        Config()
~~~

Add enabled happy-path assertions for SONARR_URL, SONARR_API_KEY,
SONARR_ROOT_FOLDER, SONARR_QUALITY_PROFILE_ID, optional
SONARR_LANGUAGE_PROFILE_ID, SONARR_SERIES_TYPE, PLEX_TV_ACCOUNT_ID,
TV_SYNC_WORKER_ID, TV_SYNC_RUN_LEASE_SECONDS, and
TV_SYNC_MAX_SNAPSHOT_AGE_MINUTES.

- [ ] **Step 2: Write Phase 3 destructive-config rejection tests**

~~~python
@pytest.mark.parametrize(
    "key",
    [
        "TV_SYNC_ALLOW_SEASON_FILE_DELETION",
        "TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION",
        "TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE",
    ],
)
def test_phase_3_rejects_enabled_destructive_switch(monkeypatch, key):
    set_valid_tv_environment(monkeypatch)
    monkeypatch.setenv(key, "true")

    with pytest.raises(
        ConfigurationError,
        match="destructive TV cleanup is unavailable in Phase 3",
    ):
        Config().validate()
~~~

Also reject unsupported series types, nonpositive IDs/intervals/lease values,
blank worker ID, and invalid Sonarr URL. Assert repr(config) redacts both API
keys and the Plex token.

- [ ] **Step 3: Run configuration tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_config.py -q
Pop-Location
~~~

Expected: new tests fail because TV configuration fields are absent.

- [ ] **Step 4: Add conditional TV configuration**

Add these fields without changing the existing movie fields:

~~~python
self.tv_sync_enabled = _env_bool("TV_SYNC_ENABLED", False)
self.tv_sync_apply = _env_bool("TV_SYNC_APPLY", False)
self.tv_sync_adopt_existing_destinations = _env_bool(
    "TV_SYNC_ADOPT_EXISTING_DESTINATIONS",
    False,
)
self.tv_sync_allow_season_file_deletion = _env_bool(
    "TV_SYNC_ALLOW_SEASON_FILE_DELETION",
    False,
)
self.tv_sync_allow_terminal_series_deletion = _env_bool(
    "TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION",
    False,
)
self.tv_sync_allow_no_recycle_bin_delete = _env_bool(
    "TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE",
    False,
)
self.tv_sync_interval_seconds = self._parse_int(
    "TV_SYNC_INTERVAL_SECONDS",
    default="900",
    minimum=60,
)
self.tv_sync_max_snapshot_age_minutes = self._parse_int(
    "TV_SYNC_MAX_SNAPSHOT_AGE_MINUTES",
    default="30",
    minimum=1,
)
self.tv_sync_run_lease_seconds = self._parse_int(
    "TV_SYNC_RUN_LEASE_SECONDS",
    default="1200",
    minimum=60,
)
self.tv_sync_worker_id = os.getenv("TV_SYNC_WORKER_ID", "").strip()
self.sonarr_url = os.getenv("SONARR_URL", "").rstrip("/")
self.sonarr_api_key = os.getenv("SONARR_API_KEY", "")
self.sonarr_root_folder = os.getenv("SONARR_ROOT_FOLDER", "")
self.sonarr_quality_profile_id = self._parse_int(
    "SONARR_QUALITY_PROFILE_ID",
    default="1",
    minimum=1,
)
self.sonarr_language_profile_id = self._parse_int(
    "SONARR_LANGUAGE_PROFILE_ID",
    default=None,
    minimum=1,
)
self.sonarr_series_type = os.getenv("SONARR_SERIES_TYPE", "standard").lower()
self.plex_tv_account_id = self._parse_int(
    "PLEX_TV_ACCOUNT_ID",
    default=None,
    minimum=1,
)
~~~

Implement _env_bool to accept only case-insensitive true or false and raise
ConfigurationError for another value. When TV is enabled, validate all required
fields. Reject any true destructive switch with the exact Phase 3 error tested
above.

- [ ] **Step 5: Run all configuration tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_config.py -q
Pop-Location
~~~

Expected: all old movie and new TV configuration tests pass.

- [ ] **Step 6: Write orchestration and lease-finally tests**

~~~python
def test_execute_tv_sync_runs_collect_plan_policy_execute_report_in_order(tmp_path):
    events: list[str] = []
    dependencies = recording_dependencies(events)

    result = execute_tv_sync(
        dependencies=dependencies,
        apply=False,
        report_dir=tmp_path,
        now=NOW,
        run_id="run-1",
    )

    assert events == [
        "lease.acquire",
        "run.start",
        "collect",
        "observations.persist",
        "plan",
        "policy",
        "execute",
        "report.local",
        "report.backend",
        "run.finish",
        "lease.release",
    ]
    assert result.exit_code == 0


def test_execute_tv_sync_releases_lease_after_exception(tmp_path):
    dependencies = dependencies_failing_at("collect")

    with pytest.raises(RuntimeError, match="collection crashed"):
        execute_tv_sync(
            dependencies=dependencies,
            apply=True,
            report_dir=tmp_path,
            now=NOW,
            run_id="run-1",
        )

    assert dependencies.store.release_calls == [
        (dependencies.lease.lease_id, "worker-1")
    ]
~~~

Add tests for lease unavailable, report-only mutation_disabled not producing
exit 3, applied policy block producing exit 3, collection failure exit 1,
partial action failure exit 2, backend run-summary failure preserving local
reports and producing exit 2, and TV disabled returning 0 without constructing
clients. With an injected clock, also prove a long collection/execution renews
the matching SQLite lease before half of its duration remains, and a failed
renewal stops before the next external action. `--apply` is only a per-run
request: test all four request/environment combinations and require effective
apply only when both the request and `TV_SYNC_APPLY` are true.

- [ ] **Step 7: Run CLI tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sync_tv_cli.py -q
Pop-Location
~~~

Expected: collection fails because sync_tv.py does not exist.

- [ ] **Step 8: Implement the testable orchestration function**

Define:

~~~text
@dataclass(frozen=True)
class TvSyncDependencies:
    config: Config
    backend_client: TvBackendClient
    collector: TvSyncCollector
    planner: Callable
    policy_evaluator: Callable
    executor: TvDestinationExecutor
    state_store: TvStateStore
    clock: Callable[[], datetime]


@dataclass(frozen=True)
class TvSyncRunResult:
    exit_code: int
    run_id: str
    generation_id: str | None
    json_path: Path | None
    markdown_path: Path | None


def execute_tv_sync(
    *,
    dependencies: TvSyncDependencies,
    apply: bool,
    report_dir: Path,
    now: datetime,
    run_id: str,
) -> TvSyncRunResult
~~~

Implement the body in the tested order. Persist observations only for complete
boundaries. Acquire the SQLite lease before start_run and always release it in
finally. Renew that same lease before each collection or execution boundary
whenever half of its configured duration or less remains; renewal failure is a
closed policy stop and no later external action may start. Build TvSyncPolicy
from config and `effective_apply = apply and config.tv_sync_apply`; the explicit
argument is a request and can never override a false environment gate. Local
report creation precedes the backend summary POST.

Exit codes are:

~~~text
0 = report-only or applied convergence with complete required collections
1 = fatal/configuration/backend snapshot collection failure
2 = partial destination execution or backend run-summary failure
3 = applied run blocked by source/policy/lease
~~~

- [ ] **Step 9: Implement main and dependency composition**

parse_args accepts --apply, --report-dir, --quiet, and --log-level. main:

1. Loads and validates Config.
2. Returns 0 with one disabled log when TV_SYNC_ENABLED is false.
3. Constructs TvStateStore, TvBackendClient, SonarrClient, PlexTvClient,
   TvSyncCollector, and TvDestinationExecutor.
4. Treats `args.apply` only as a per-run request and passes
   `args.apply and config.tv_sync_apply` as effective apply. A true CLI flag
   never overrides `TV_SYNC_APPLY=false`.
5. Generates UUID4 run ID and timezone-aware now.
6. Calls execute_tv_sync.
7. Writes a workflow heartbeat through healthcheck.py.
8. Prints only local report paths and returns the result exit code.

- [ ] **Step 10: Run CLI and configuration tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_sync_tv_cli.py tests\vod_filter\test_config.py -q
Pop-Location
~~~

Expected: all CLI lifecycle, exit-code, lease, disabled-mode, and configuration
tests pass.

- [ ] **Step 11: Commit the CLI slice**

~~~powershell
git add workers/vod-filter/src/config.py workers/vod-filter/tests/vod_filter/test_config.py workers/vod-filter/sync_tv.py workers/vod-filter/tests/vod_filter/test_sync_tv_cli.py
git commit -m "feat(worker): add safe TV sync entry point"
~~~

### Task 12: Schedule TV Independently And Package It Disabled By Default

**Files:**

- Modify: workers/vod-filter/continuous_sync.py
- Modify: workers/vod-filter/healthcheck.py
- Modify: workers/vod-filter/tests/vod_filter/test_continuous_sync.py
- Modify: workers/vod-filter/tests/vod_filter/test_worker_healthcheck.py
- Modify: workers/vod-filter/Dockerfile
- Modify: workers/vod-filter/example.env
- Modify: workers/vod-filter/docker-compose.yml
- Modify: deploy/production/worker.env.example
- Modify: deploy/production/compose.yaml
- Modify: .github/workflows/movie-ci.yml
- Modify: tests/deployment/test_deploy_script.py

- [ ] **Step 1: Write independent-deadline scheduler tests**

~~~python
def test_scheduler_runs_movie_hourly_and_tv_every_fifteen_minutes():
    clock = FakeClock()
    calls: list[tuple[str, int]] = []

    continuous_sync(
        movie_interval=3600,
        tv_interval=900,
        tv_enabled=True,
        run_movie=lambda: calls.append(("movie", int(clock.now))),
        run_tv=lambda: calls.append(("tv", int(clock.now))),
        monotonic=clock.monotonic,
        sleep=clock.sleep,
        stop_after=3601,
    )

    assert calls == [
        ("movie", 0),
        ("tv", 0),
        ("tv", 900),
        ("tv", 1800),
        ("tv", 2700),
        ("movie", 3600),
        ("tv", 3600),
    ]
~~~

Add tests that disabled TV never imports/runs sync_tv, one workflow exception
does not shift the other deadline, --dry-run sets both apply variables false,
and a deadline advances from its prior scheduled time rather than completion
time.

- [ ] **Step 2: Write workflow-aware heartbeat tests**

~~~python
def test_health_requires_recent_tv_when_tv_is_enabled(tmp_path):
    path = tmp_path / "last-run.json"
    write_heartbeat(
        path,
        workflow="movie_sync",
        status="completed",
        exit_code=0,
        written_at=NOW,
    )
    write_heartbeat(
        path,
        workflow="tv_sync",
        status="reconciliation",
        exit_code=0,
        written_at=NOW,
    )

    assert check_heartbeat(
        path,
        max_age_seconds=1800,
        now=NOW + timedelta(minutes=15),
        required_workflows=("movie_sync", "tv_sync"),
    )
~~~

Retain a backward-compatible test for the current single-workflow heartbeat.

- [ ] **Step 3: Run scheduler/health tests and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_continuous_sync.py tests\vod_filter\test_worker_healthcheck.py -q
Pop-Location
~~~

Expected: new tests fail because independent deadlines and workflow heartbeat
fields are absent.

- [ ] **Step 4: Implement independent monotonic scheduling**

Keep existing command-line compatibility. Add --tv-interval with
TV_SYNC_INTERVAL_SECONDS default. Track next_movie and next_tv independently.
After each due run, increment that workflow's prior deadline repeatedly until
it is in the future. Sleep only until the nearest enabled deadline. Import
sync_tv.main lazily inside run_tv_sync. The continuous scheduler passes
`--apply` as its per-run request; `sync_tv.main` still requires
`TV_SYNC_APPLY=true`, so the scheduled path cannot bypass the global gate.

- [ ] **Step 5: Implement backward-compatible workflow heartbeats**

write_heartbeat adds or updates:

~~~json
{
  "status": "reconciliation",
  "exit_code": 0,
  "written_at": "2026-07-13T12:00:00+00:00",
  "workflows": {
    "movie_sync": {
      "status": "completed",
      "exit_code": 0,
      "written_at": "2026-07-13T12:00:00+00:00"
    },
    "tv_sync": {
      "status": "reconciliation",
      "exit_code": 0,
      "written_at": "2026-07-13T12:00:00+00:00"
    }
  }
}
~~~

The top-level fields remain the most recently written workflow for backward
compatibility. healthcheck.main requires tv_sync only when TV_SYNC_ENABLED is
true.

- [ ] **Step 6: Run scheduler/health tests**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_continuous_sync.py tests\vod_filter\test_worker_healthcheck.py -q
Pop-Location
~~~

Expected: all old and new scheduler/heartbeat tests pass.

- [ ] **Step 7: Add non-secret disabled production configuration**

Add these exact example values:

~~~dotenv
TV_SYNC_ENABLED=false
TV_SYNC_APPLY=false
TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false
TV_SYNC_ALLOW_SEASON_FILE_DELETION=false
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false
TV_SYNC_INTERVAL_SECONDS=900
TV_SYNC_MAX_SNAPSHOT_AGE_MINUTES=30
TV_SYNC_RUN_LEASE_SECONDS=1200
TV_SYNC_WORKER_ID=watchlist-prod-tv-1

SONARR_URL=http://replace-sonarr-host:8989
SONARR_API_KEY=replace-on-host
SONARR_ROOT_FOLDER=/tv
SONARR_QUALITY_PROFILE_ID=1
SONARR_SERIES_TYPE=standard
PLEX_TV_ACCOUNT_ID=1
~~~

Do not add a TV media bind mount in Phase 3. That mount first becomes required
for terminal cleanup path verification in Phase 5.

- [ ] **Step 8: Package and deployment-test the entry point**

Add sync_tv.py to the existing Dockerfile COPY line. Add it to the compileall
command in movie-ci.yml. Extend test_deploy_script.py to assert:

~~~python
assert "sync_tv.py" in worker_dockerfile
worker_example = (ROOT / "deploy/production/worker.env.example").read_text()
assert "TV_SYNC_ENABLED=false" in worker_example
assert "TV_SYNC_ALLOW_SEASON_FILE_DELETION=false" in worker_example
assert "TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false" in worker_example
assert "TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false" in worker_example
assert ":/app/tv-ro" not in production_compose
~~~

- [ ] **Step 9: Run deployment and compile validation**

Run:

~~~powershell
python -m pytest tests\deployment\test_deploy_script.py -q
Push-Location workers\vod-filter
python -m compileall -q src continuous_sync.py sync_movies.py sync_tv.py reconcile_sync.py healthcheck.py
Pop-Location
~~~

Expected: deployment tests pass and compileall exits 0 with no output.

- [ ] **Step 10: Commit scheduler and packaging**

~~~powershell
git add workers/vod-filter/continuous_sync.py workers/vod-filter/healthcheck.py workers/vod-filter/tests/vod_filter/test_continuous_sync.py workers/vod-filter/tests/vod_filter/test_worker_healthcheck.py workers/vod-filter/Dockerfile workers/vod-filter/example.env workers/vod-filter/docker-compose.yml deploy/production/worker.env.example deploy/production/compose.yaml .github/workflows/movie-ci.yml tests/deployment/test_deploy_script.py
git commit -m "feat(worker): schedule disabled TV synchronization"
~~~

### Task 13: Prove Phase 3 End To End And Preserve Movie Behavior

**Files:**

- Create: workers/vod-filter/tests/vod_filter/test_tv_workflow_simulation.py

- [ ] **Step 1: Write report-only and apply simulations**

Build stateful fake backend, Sonarr, Plex, and SQLite boundaries. Cover:

~~~python
def test_phase_3_report_only_then_apply_and_reactivate(tmp_path):
    simulation = TvPhase3Simulation(tmp_path)

    report_only = simulation.run(
        snapshot="active-unfinished",
        apply=False,
        adopt=False,
    )
    assert report_only.external_mutations == []
    assert report_only.report["actions"]

    added = simulation.run(
        snapshot="active-unfinished",
        apply=True,
        adopt=False,
    )
    assert added.sonarr_has(7101)
    assert added.plex_watchlist_has(7101)

    caught_up = simulation.run(
        snapshot="caught-up-continuing",
        apply=True,
        adopt=False,
    )
    assert caught_up.sonarr_series(7101).monitored is True
    assert caught_up.sonarr_series(7101).monitor_new_items == "all"
    assert not caught_up.plex_watchlist_has(7101)

    reactivated = simulation.run(
        snapshot="new-aired-episode",
        apply=True,
        adopt=False,
    )
    assert reactivated.plex_watchlist_has(7101)
    assert reactivated.sonarr_searches == [(7101, (7009,))]
~~~

- [ ] **Step 2: Add safety simulations**

Add scenarios proving:

- existing exact Sonarr/Plex rows remain unmanaged in report-only;
- apply with adoption false does not mutate unmanaged rows;
- supervised apply with adoption true records exact target IDs before action;
- source_removed removes only an owned/adopted Plex row and causes no Sonarr
  call;
- malformed or mutation-incapable snapshot causes zero mutations;
- Sonarr failure still permits an independently healthy owned Plex action;
- Plex failure still permits an independently healthy owned Sonarr action;
- missing/conflicting TVDB causes zero destination mutation;
- cleanup authorizations appear with cleanup_phase_disabled and trigger no
  backend claim/result request;
- no fake records a Plex-library, Sonarr file-delete, Sonarr series-delete,
  broad-search, Trakt, or MongoDB call; and
- an action failure is audited and a later run converges from fresh state.

- [ ] **Step 3: Run simulations and verify failure**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest tests\vod_filter\test_tv_workflow_simulation.py -q
Pop-Location
~~~

Expected: simulation tests fail until all preceding Phase 3 slices are present;
after completing Tasks 1–12 every simulation passes.

- [ ] **Step 4: Run the complete worker suite**

Run:

~~~powershell
Push-Location workers\vod-filter
python -m pytest -q
Pop-Location
~~~

Expected: all existing 133 movie-worker tests plus every new Phase 3 TV test
pass.

- [ ] **Step 5: Run repository validation**

From the repository root:

~~~powershell
python tests\validate_okf.py
python -m pytest tests\deployment -q
dotnet build backend\Watchlist.sln --configuration Release
dotnet test backend\Watchlist.sln --configuration Release --no-build
docker build -t watchlist-worker:tv-phase-3 workers\vod-filter
~~~

Render production Compose with real non-secret temporary env files:

~~~powershell
$validationRoot = Join-Path $env:TEMP "watchlist-tv-phase3-compose"
$configDir = Join-Path $validationRoot "config"
$dataDir = Join-Path $validationRoot "data"
New-Item -ItemType Directory -Force $configDir, "$dataDir/backend/data-protection-keys", "$dataDir/worker" | Out-Null
Copy-Item deploy/production/backend.env.example "$configDir/backend.env"
Copy-Item deploy/production/worker.env.example "$configDir/worker.env"
$env:WATCHLIST_CONFIG_DIR = $configDir
$env:WATCHLIST_DATA_DIR = $dataDir
$env:WATCHLIST_RUNTIME_UID = "10001"
$env:WATCHLIST_RUNTIME_GID = "10001"
$env:WATCHLIST_RELEASE = "tv-phase-3-validation"
docker compose -f deploy\production\compose.yaml config --quiet
Remove-Item Env:WATCHLIST_CONFIG_DIR, Env:WATCHLIST_DATA_DIR, Env:WATCHLIST_RUNTIME_UID, Env:WATCHLIST_RUNTIME_GID, Env:WATCHLIST_RELEASE
Remove-Item -Recurse -Force $validationRoot
~~~

Expected: OKF and deployment tests pass, backend build/tests pass, worker image
builds, and Compose validates without a TV media-root mount.

- [ ] **Step 6: Commit the simulation and validation slice**

~~~powershell
git add workers/vod-filter/tests/vod_filter/test_tv_workflow_simulation.py
git commit -m "test(worker): simulate reversible TV synchronization"
~~~

### Task 14: Publish The Phase 3 Production And Ownership Contract In OKF

**Files:**

- Create: docs/architecture/tv_sync_production.md
- Create: docs/integrations/sonarr.md
- Create: docs/data_models/tv_lifecycle_event.md
- Modify: docs/reports/tv_integration_rollout.md
- Modify: docs/architecture/index.md
- Modify: docs/architecture/system_boundaries.md
- Modify: docs/apis/backend_api.md
- Modify: docs/apis/export_endpoints.md
- Modify: docs/integrations/index.md
- Modify: docs/integrations/plex.md
- Modify: docs/data_models/index.md
- Modify: docs/systems/vod_filter_worker.md
- Modify: docs/runbooks/tv_sync_operations.md
- Modify: docs/runbooks/vod_filter_operations.md
- Modify: docs/runbooks/validation.md
- Modify: docs/reports/index.md
- Modify: docs/backlog/roadmap.md
- Modify: docs/log.md

- [ ] **Step 1: Write three durable concepts and extend the rollout ledger**

`tv_sync_production.md` records the authoritative backend-generation to
read-only collector to pure planner to policy to reversible executor flow,
including ownership/adoption and the rule that destructive cleanup is a
separate executor introduced only in later phases.

`sonarr.md` records exact TVDB lookup, add/monitor/unmonitor behavior, targeted
episode search, ownership boundaries, and every Phase 3 prohibited command or
DELETE. `tv_lifecycle_event.md` records stable event IDs, lifecycle versions,
source-removal semantics, destination-drift events, one-use future cleanup
authorization references, and immutable history. Extend the cumulative
`tv_integration_rollout.md` ledger created by Phase 1 and updated by Phase 2
with Phase 3 report-only, supervised adoption, reversible apply, rollback,
unresolved blockers, and all seven gate values. Never recreate or replace its
earlier phase evidence.

Every new document and the modified cumulative report must have valid OKF
frontmatter, links to the approved design and program plan, concrete
invariants, owner/system boundaries, validation commands, and a
`last_verified` date of 2026-07-13. State explicitly that Phase 3 keeps all
cleanup switches false.

- [ ] **Step 2: Update the indexes and operational references**

Add each new concept to its section index. Update the API/export docs with the
version-1 snapshot and `POST /api/worker/tv/runs` 202 contract. Update Plex and
the worker docs with exact-identity reversible watchlist ownership. Update both
operations runbooks with report-only/adoption/apply commands and rollback to
`TV_SYNC_ENABLED=false` or `TV_SYNC_APPLY=false`.

The validation runbook must include worker tests, backend worker-run tests,
deployment tests, Compose rendering with real temporary env files, and the
negative mutation-surface scan. Roadmap/log entries link the implementation
commit(s) and record observed results rather than planned success.

- [ ] **Step 3: Validate OKF links and metadata**

Run from the repository root:

~~~powershell
python tests\validate_okf.py
~~~

Expected: all OKF metadata, index membership, and relative links validate.

- [ ] **Step 4: Commit the Phase 3 knowledge layer**

~~~powershell
git add docs
git commit -m "docs: document reversible TV synchronization"
~~~

## Phase 3 Rollout Checkpoints

1. Deploy with TV_SYNC_ENABLED=false. Confirm movie synchronization, existing
   heartbeat, and reports are unchanged.
2. Set TV_SYNC_ENABLED=true while TV_SYNC_APPLY=false and
   TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false. Observe at least one complete
   backend generation and review exact Sonarr/Plex adoption candidates.
3. Confirm the backend snapshot is mutationCapable, Plex history bootstrap is
   complete, the outbox has no unresolved rows for managed shows, post-cutover
   routing has no pending/conflicting row, and all identity warnings are
   understood.
4. For one supervised run, set TV_SYNC_APPLY=true and
   TV_SYNC_ADOPT_EXISTING_DESTINATIONS=true. Review the report immediately,
   then return the adoption flag to false.
5. Keep TV_SYNC_APPLY=true only after exact ownership rows and the first
   convergence report are accepted. Verify caught-up continuing shows stay
   monitored in Sonarr while leaving the Plex watchlist.
6. Keep all three destructive switches false throughout Phase 3. Any startup
   with one true must fail validation before constructing a destination client.
7. Do not add a TV media-root mount, call cleanup claim/result, or enable a
   cleanup switch until the separately reviewed Phase 4 or Phase 5 plan is
   implemented.

## Completion Criteria

- A strict version-1 export is required for every plan.
- Report-only and apply use the same collector, planner, and policy.
- All Sonarr mutations require exact TVDB identity and all Plex mutations
  require exact verified external identity.
- A continuing caught-up show remains monitored in Sonarr and loses only an
  owned/adopted Plex watchlist entry.
- A new aired unwatched episode restores Plex membership and searches only the
  exact missing aired Sonarr episode ID.
- Existing destinations are never silently adopted.
- Source removal causes no Sonarr change.
- Cleanup authorizations are visible but never executable.
- Existing movie behavior and its 133-test baseline remain green.
