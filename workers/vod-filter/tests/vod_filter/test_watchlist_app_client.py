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
        if request.url.path == "/api/sync/all":
            return httpx.Response(200, json={"status": "completed"})
        if request.url.path == "/api/export/radarr/movies":
            return httpx.Response(200, json=[])
        return httpx.Response(404)

    client = WatchlistAppClient(
        base_url="http://watchlist.local",
        http_client=httpx.Client(transport=httpx.MockTransport(handler)),
    )

    assert client.fetch_radarr_movie_export(sync_first=True) == []
    assert requests == [
        ("POST", "/api/sync/all"),
        ("GET", "/api/export/radarr/movies"),
    ]


def test_watchlist_app_client_raises_on_bad_export_shape():
    client = WatchlistAppClient(
        base_url="http://watchlist.local",
        http_client=httpx.Client(transport=httpx.MockTransport(lambda request: httpx.Response(200, json=[{}]))),
    )

    with pytest.raises(WatchlistAppError):
        client.fetch_radarr_movie_export()

