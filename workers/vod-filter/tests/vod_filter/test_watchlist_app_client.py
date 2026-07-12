from __future__ import annotations

import sys
from pathlib import Path

import httpx
import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.clients.watchlist_app_client import WatchlistAppClient, WatchlistAppError


def test_watchlist_app_client_maps_radarr_export_to_watchlist_entries():
    requests = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append((request.method, request.url.path))
        if request.url.path == "/api/export/radarr/movies":
            return httpx.Response(
                200,
                json=[
                    {
                        "id": 1297842,
                        "imdb_id": "tt1234567",
                        "title": "Needs Download",
                        "release_year": "2024",
                        "clean_title": "needs-download",
                        "adult": False,
                    }
                ],
            )
        return httpx.Response(404)

    client = WatchlistAppClient(
        base_url="http://watchlist.local/",
        http_client=httpx.Client(transport=httpx.MockTransport(handler)),
    )

    assert client.fetch_radarr_movie_export() == [
        {
            "title": "Needs Download",
            "year": 2024,
            "tmdb_id": 1297842,
            "imdb_id": "tt1234567",
            "letterboxd_id": "needs-download",
            "watchlist_app_id": 1297842,
        }
    ]
    assert requests == [("GET", "/api/export/radarr/movies")]


def test_watchlist_app_client_can_sync_first():
    requests = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append((request.method, request.url.path))
        if request.url.path == "/api/sync/movies":
            assert request.headers["X-Watchlist-Sync-Key"] == "sync-secret"
            return httpx.Response(200, json={"status": "completed"})
        if request.url.path == "/api/export/radarr/movies":
            return httpx.Response(200, json=[])
        return httpx.Response(404)

    client = WatchlistAppClient(
        base_url="http://watchlist.local",
        http_client=httpx.Client(transport=httpx.MockTransport(handler)),
        sync_key="sync-secret",
    )

    assert client.fetch_radarr_movie_export(sync_first=True) == []
    assert requests == [
        ("POST", "/api/sync/movies"),
        ("GET", "/api/export/radarr/movies"),
    ]


def test_watchlist_app_client_uses_long_timeout_only_for_backend_sync():
    timeouts = []

    def handler(request: httpx.Request) -> httpx.Response:
        timeouts.append((request.method, request.extensions["timeout"]["read"]))
        if request.url.path == "/api/sync/movies":
            return httpx.Response(200, json={"status": "completed"})
        if request.url.path == "/api/export/movies/sync-state":
            return httpx.Response(
                200,
                json={
                    "generatedAt": "2026-07-11T08:00:00+00:00",
                    "lastSuccessfulMovieSyncAt": "2026-07-11T07:55:00+00:00",
                    "movies": [],
                },
            )
        return httpx.Response(404)

    client = WatchlistAppClient(
        base_url="http://watchlist.local",
        http_client=httpx.Client(
            transport=httpx.MockTransport(handler),
            timeout=17,
        ),
        sync_timeout_seconds=321,
    )

    client.fetch_movie_sync_snapshot(sync_first=True)

    assert timeouts == [("POST", 321), ("GET", 17)]


def test_watchlist_app_client_fetches_complete_movie_sync_snapshot():
    requests = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append((request.method, request.url.path))
        if request.url.path == "/api/sync/movies":
            assert request.headers["X-Watchlist-Sync-Key"] == "sync-secret"
            return httpx.Response(200, json={"status": "completed"})
        if request.url.path == "/api/export/movies/sync-state":
            return httpx.Response(
                200,
                json={
                    "generatedAt": "2026-07-11T08:00:00+00:00",
                    "lastSuccessfulMovieSyncAt": "2026-07-11T07:55:00+00:00",
                    "movies": [
                        {
                            "tmdbId": 1297842,
                            "imdbId": "tt27613895",
                            "title": "GOAT",
                            "year": 2026,
                            "sourceId": "1297842",
                            "metadataStatus": "enriched",
                            "availabilityStatus": "not_on_plex",
                            "ownedServiceAvailability": [],
                            "radarrEligible": True,
                            "radarrEligibilityReason": "no_owned_service",
                        }
                    ],
                },
            )
        return httpx.Response(404)

    client = WatchlistAppClient(
        base_url="http://watchlist.local",
        http_client=httpx.Client(transport=httpx.MockTransport(handler)),
        sync_key="sync-secret",
    )

    snapshot = client.fetch_movie_sync_snapshot(sync_first=True)

    assert snapshot["generated_at"].isoformat() == "2026-07-11T08:00:00+00:00"
    assert snapshot["last_successful_movie_sync_at"].isoformat() == (
        "2026-07-11T07:55:00+00:00"
    )
    assert snapshot["movies"] == [
        {
            "tmdb_id": 1297842,
            "imdb_id": "tt27613895",
            "title": "GOAT",
            "year": 2026,
            "source_id": "1297842",
            "metadata_status": "enriched",
            "availability_status": "not_on_plex",
            "owned_service_availability": [],
            "radarr_eligible": True,
            "radarr_eligibility_reason": "no_owned_service",
        }
    ]
    assert requests == [
        ("POST", "/api/sync/movies"),
        ("GET", "/api/export/movies/sync-state"),
    ]


def test_watchlist_app_client_rejects_malformed_movie_sync_snapshot():
    client = WatchlistAppClient(
        base_url="http://watchlist.local",
        http_client=httpx.Client(
            transport=httpx.MockTransport(
                lambda request: httpx.Response(
                    200,
                    json={"generatedAt": "bad", "movies": "not-a-list"},
                )
            )
        ),
    )

    with pytest.raises(WatchlistAppError):
        client.fetch_movie_sync_snapshot()


def test_watchlist_app_client_fetches_movie_watchlist_without_plex_only_items():
    requests = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append((request.method, request.url.path, str(request.url.query, "utf-8")))
        if request.url.path == "/api/watchlist":
            return httpx.Response(
                200,
                json=[
                    {
                        "id": "movie-letterboxd-1297842",
                        "mediaType": "movie",
                        "source": "letterboxd",
                        "sourceId": "1297842",
                        "title": "Backend Movie",
                        "year": 2024,
                        "availabilityStatus": "not_on_plex",
                        "libraryMembership": "watchlist_only",
                        "ownedServiceAvailability": [],
                    },
                    {
                        "id": "plex-movie-1",
                        "mediaType": "movie",
                        "source": "plex",
                        "sourceId": "1",
                        "title": "Plex Only",
                        "year": 1962,
                        "availabilityStatus": "available_on_plex",
                        "libraryMembership": "plex_only",
                        "ownedServiceAvailability": ["plex"],
                    },
                ],
            )
        return httpx.Response(404)

    client = WatchlistAppClient(
        base_url="http://watchlist.local",
        http_client=httpx.Client(transport=httpx.MockTransport(handler)),
    )

    assert client.fetch_movie_watchlist() == [
        {
            "title": "Backend Movie",
            "year": 2024,
            "tmdb_id": 1297842,
            "imdb_id": None,
            "letterboxd_id": "movie-letterboxd-1297842",
            "watchlist_app_id": "movie-letterboxd-1297842",
            "source": "letterboxd",
            "source_id": "1297842",
            "availability_status": "not_on_plex",
            "library_membership": "watchlist_only",
            "owned_service_availability": [],
        }
    ]
    assert requests == [
        (
            "GET",
            "/api/watchlist",
            "collection=movie&availability=plex%2Cnot_on_plex%2Cunreleased%2Cunknown_match&sort=title_asc",
        )
    ]


def test_watchlist_app_client_raises_on_bad_export_shape():
    client = WatchlistAppClient(
        base_url="http://watchlist.local",
        http_client=httpx.Client(transport=httpx.MockTransport(lambda request: httpx.Response(200, json=[{}]))),
    )

    with pytest.raises(WatchlistAppError):
        client.fetch_radarr_movie_export()

