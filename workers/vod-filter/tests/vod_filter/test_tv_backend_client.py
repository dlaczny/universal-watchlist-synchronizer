from __future__ import annotations

import copy
import sys
from pathlib import Path

import httpx
import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.clients.watchlist_app_client import WatchlistAppClient, WatchlistAppError
from src.models.tv_sync import TvAvailability, TvEpisode, TvSeason, TvShow, TvSnapshot


def valid_snapshot() -> dict:
    return {
        "schemaVersion": "2",
        "generationId": "tv-generation-42",
        "publishedAt": "2026-07-24T10:00:00Z",
        "generatedAt": "2026-07-24T09:55:00Z",
        "kind": "scheduled_full",
        "mutationCapable": False,
        "destinationSync": {"capable": True, "blockers": []},
        "healthReasons": ["plex_history_phase_not_implemented"],
        "plexHistory": {"capable": False, "bootstrapComplete": False},
        "cleanupAuthorizations": [],
        "shows": [
            {
                "traktId": 101,
                "tvdbId": 202,
                "tmdbId": 303,
                "imdbId": "tt1234567",
                "title": "A Regular Show",
                "year": 2025,
                "identityStatus": "verified",
                "inTraktWatchlist": True,
                "lifecycleState": "active",
                "lifecycleVersion": 1,
                "traktStatus": "returning series",
                "aired": 2,
                "completed": 1,
                "lastWatchedEpisode": None,
                "nextEpisode": None,
                "sonarrDesired": True,
                "sonarrMonitoredDesired": True,
                "plexWatchlistDesired": True,
                "polandAvailability": {
                    "state": "available",
                    "region": "PL",
                    "fetchedAt": "2026-07-24T09:50:00Z",
                    "link": None,
                    "offers": [],
                },
                "blockers": ["phase_1_read_only"],
                "seasons": [
                    {
                        "seasonNumber": 1,
                        "aired": 2,
                        "completed": 1,
                        "monitoredDesired": True,
                        "searchAiredUnwatchedEpisodes": [2],
                        "cleanupState": "not_authorized",
                        "polandAvailability": {
                            "state": "available",
                            "region": "PL",
                            "fetchedAt": "2026-07-24T09:50:00Z",
                            "link": None,
                            "offers": [],
                        },
                        "episodes": [
                            {
                                "traktEpisodeId": 1001,
                                "seasonNumber": 1,
                                "episodeNumber": 1,
                                "tvdbId": 4001,
                                "title": "Pilot",
                                "firstAired": "2026-07-01T00:00:00Z",
                                "aired": True,
                                "watched": True,
                                "lastWatchedAt": "2026-07-02T00:00:00Z",
                                "plexRatingKey": None,
                                "watchedByConfiguredPlexAccount": None,
                                "plexLastViewedAt": None,
                            }
                        ],
                    }
                ],
            }
        ],
    }


def response_client(payload: object, requests: list[tuple[str, str]] | None = None) -> WatchlistAppClient:
    def handler(request: httpx.Request) -> httpx.Response:
        if requests is not None:
            requests.append((request.method, request.url.path))
        return httpx.Response(200, json=payload)

    return WatchlistAppClient(
        base_url="http://watchlist.local/",
        http_client=httpx.Client(transport=httpx.MockTransport(handler)),
    )


def test_fetch_tv_sync_snapshot_returns_frozen_typed_regular_snapshot() -> None:
    requests: list[tuple[str, str]] = []

    snapshot = response_client(valid_snapshot(), requests).fetch_tv_sync_snapshot()

    assert isinstance(snapshot, TvSnapshot)
    assert snapshot.generation_id == "tv-generation-42"
    assert snapshot.published_at.isoformat() == "2026-07-24T10:00:00+00:00"
    assert snapshot.shows == (
        TvShow(
            trakt_id=101,
            tvdb_id=202,
            title="A Regular Show",
            seasons=(
                TvSeason(
                    season_number=1,
                    availability=TvAvailability(
                        state="available",
                        region="PL",
                        fetched_at=snapshot.shows[0].seasons[0].availability.fetched_at,
                    ),
                    episodes=(
                        TvEpisode(
                            trakt_episode_id=1001,
                            season_number=1,
                            episode_number=1,
                            tvdb_id=4001,
                            first_aired=snapshot.shows[0].seasons[0].episodes[0].first_aired,
                            last_watched_at=snapshot.shows[0].seasons[0].episodes[0].last_watched_at,
                        ),
                    ),
                ),
            ),
            specials=(),
        ),
    )
    with pytest.raises((AttributeError, TypeError)):
        snapshot.generation_id = "changed"  # type: ignore[misc]
    assert requests == [("GET", "/api/export/tv/sync-state")]


def test_fetch_tv_sync_snapshot_rejects_invalid_json() -> None:
    client = WatchlistAppClient(
        base_url="http://watchlist.local",
        http_client=httpx.Client(
            transport=httpx.MockTransport(
                lambda request: httpx.Response(200, content=b"not-json")
            )
        ),
    )

    with pytest.raises(WatchlistAppError, match="TV sync snapshot returned invalid JSON"):
        client.fetch_tv_sync_snapshot()


@pytest.mark.parametrize(
    ("mutate", "reason"),
    [
        (lambda snapshot: snapshot.__setitem__("schemaVersion", "1"), "schemaVersion"),
        (
            lambda snapshot: snapshot.__setitem__(
                "destinationSync", {"capable": False, "blockers": ["blocked"]}
            ),
            "destinationSync incapable",
        ),
        (
            lambda snapshot: snapshot["shows"].append(copy.deepcopy(snapshot["shows"][0])),
            "duplicate Trakt ID",
        ),
        (
            lambda snapshot: snapshot["shows"].append(
                {**copy.deepcopy(snapshot["shows"][0]), "traktId": 102}
            ),
            "duplicate TVDB ID",
        ),
        (lambda snapshot: snapshot.__setitem__("publishedAt", "not-a-timestamp"), "publishedAt"),
        (lambda snapshot: snapshot.__setitem__("generatedAt", "2026-07-24T09:55:00"), "UTC"),
        (lambda snapshot: snapshot["shows"][0].__setitem__("tvdbId", 0), "TVDB ID"),
    ],
)
def test_fetch_tv_sync_snapshot_rejects_unsafe_contracts(mutate, reason: str) -> None:
    payload = valid_snapshot()
    mutate(payload)

    with pytest.raises(WatchlistAppError, match=reason):
        response_client(payload).fetch_tv_sync_snapshot()


def test_fetch_tv_sync_snapshot_preserves_specials_outside_regular_season_candidates() -> None:
    payload = valid_snapshot()
    special_season = copy.deepcopy(payload["shows"][0]["seasons"][0])
    special_season["seasonNumber"] = 0
    special_season["episodes"][0]["seasonNumber"] = 0
    special_season["episodes"][0]["traktEpisodeId"] = 1000
    payload["shows"][0]["seasons"].insert(0, special_season)

    snapshot = response_client(payload).fetch_tv_sync_snapshot()

    assert [season.season_number for season in snapshot.shows[0].seasons] == [1]
    assert snapshot.shows[0].specials == (
        TvEpisode(
            trakt_episode_id=1000,
            season_number=0,
            episode_number=1,
            tvdb_id=4001,
            first_aired=snapshot.shows[0].specials[0].first_aired,
            last_watched_at=snapshot.shows[0].specials[0].last_watched_at,
        ),
    )


@pytest.mark.parametrize(
    "path",
    [
        (),
        ("destinationSync",),
        ("plexHistory",),
        ("shows", 0),
        ("shows", 0, "polandAvailability"),
        ("shows", 0, "seasons", 0),
        ("shows", 0, "seasons", 0, "episodes", 0),
        ("shows", 0, "seasons", 0, "polandAvailability"),
    ],
)
def test_fetch_tv_sync_snapshot_rejects_credential_shaped_keys_at_every_object_depth(path) -> None:
    payload = valid_snapshot()
    target = payload
    for part in path:
        target = target[part]
    target["accessToken"] = "must-not-cross-the-boundary"

    with pytest.raises(WatchlistAppError, match="credential-shaped key"):
        response_client(payload).fetch_tv_sync_snapshot()
