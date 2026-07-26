from __future__ import annotations

import json
import sys
from pathlib import Path

import httpx
import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.clients.sonarr_tv_client import (
    SonarrTvClient,
    SonarrTvError,
    SonarrSeries,
    SonarrSeriesLookup,
)


def sonarr_client(handler) -> SonarrTvClient:
    return SonarrTvClient(
        "http://sonarr.local",
        "test-key",
        http_client=httpx.Client(transport=httpx.MockTransport(handler)),
    )


def series_payload(series_id: int = 44, tvdb_id: int = 123) -> dict:
    return {
        "id": series_id,
        "tvdbId": tvdb_id,
        "title": "Exact Show",
        "monitored": False,
        "monitorNewItems": "none",
        "seasons": [
            {"seasonNumber": 0, "monitored": False},
            {"seasonNumber": 1, "monitored": False},
        ],
    }


def test_lookup_uses_tvdb_term_and_rejects_mismatched_result() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.method == "GET"
        assert request.url.path == "/api/v3/series/lookup"
        assert request.url.params == httpx.QueryParams({"term": "tvdb:123"})
        return httpx.Response(200, json=[series_payload(tvdb_id=999)])

    with pytest.raises(SonarrTvError, match="TVDB identity mismatch"):
        sonarr_client(handler).lookup_by_tvdb(123)


def test_get_series_by_tvdb_reads_the_list_and_matches_only_exact_identity() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.method == "GET"
        assert request.url.path == "/api/v3/series"
        return httpx.Response(200, json=[series_payload(10, 999), series_payload(11, 123)])

    result = sonarr_client(handler).get_series_by_tvdb(123)

    assert result == SonarrSeries(
        series_id=11,
        tvdb_id=123,
        title="Exact Show",
        monitored=False,
        seasons={0: False, 1: False},
        resource=series_payload(11, 123),
    )


def test_add_series_posts_only_an_exact_tvdb_lookup_result() -> None:
    requests: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        if request.method == "GET":
            return httpx.Response(200, json=[series_payload(tvdb_id=123)])

        assert request.method == "POST"
        assert request.url.path == "/api/v3/series"
        assert json.loads(request.content) == {
            **series_payload(tvdb_id=123),
            "rootFolderPath": "/tv",
            "qualityProfileId": 7,
            "monitored": True,
            "monitorNewItems": "all",
            "addOptions": {"searchForMissingEpisodes": False},
        }
        return httpx.Response(201, json=series_payload(55, 123))

    lookup = sonarr_client(handler).lookup_by_tvdb(123)
    added = sonarr_client(handler).add_series(lookup, "/tv", 7)

    assert added.series_id == 55
    assert [request.url.path for request in requests] == [
        "/api/v3/series/lookup",
        "/api/v3/series",
    ]


def test_monitoring_updates_preserve_exact_series_identity_and_other_seasons() -> None:
    requests: list[httpx.Request] = []
    series = SonarrSeries(
        series_id=44,
        tvdb_id=123,
        title="Exact Show",
        monitored=False,
        seasons={0: False, 1: False, 2: False},
        resource={
            **series_payload(44, 123),
            "seasons": [
                {"seasonNumber": 0, "monitored": False},
                {"seasonNumber": 1, "monitored": False},
                {"seasonNumber": 2, "monitored": False},
            ],
        },
    )

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        body = json.loads(request.content)
        assert request.method == "PUT"
        assert request.url.path == "/api/v3/series/44"
        assert body["tvdbId"] == 123
        assert body["monitored"] is True
        assert body["monitorNewItems"] == "all"
        if len(requests) == 1:
            assert [season["monitored"] for season in body["seasons"]] == [False, False, False]
        else:
            assert [season["monitored"] for season in body["seasons"]] == [False, True, False]
        return httpx.Response(202, json=body)

    client = sonarr_client(handler)
    monitored = client.set_series_monitored(series, True)
    season_monitored = client.set_season_monitored(monitored, 1)

    assert monitored.monitored is True
    assert season_monitored.seasons == {0: False, 1: True, 2: False}


def test_set_season_monitored_rejects_a_mismatched_series_resource_id() -> None:
    series = SonarrSeries(
        series_id=44,
        tvdb_id=123,
        title="Exact Show",
        monitored=True,
        seasons={1: False},
        resource=series_payload(series_id=999, tvdb_id=123),
    )

    with pytest.raises(SonarrTvError, match="resource ID mismatch"):
        sonarr_client(lambda _request: pytest.fail("must not issue a PUT")).set_season_monitored(
            series,
            1,
        )


def test_search_episode_ids_sends_only_unique_positive_episode_ids() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.method == "POST"
        assert request.url.path == "/api/v3/command"
        assert json.loads(request.content) == {
            "name": "EpisodeSearch",
            "episodeIds": [3, 4],
        }
        return httpx.Response(201, json={"id": 89, "name": "EpisodeSearch"})

    sonarr_client(handler).search_episode_ids(44, [4, 3, 4])


def test_sonarr_client_has_no_delete_surface() -> None:
    forbidden = {"delete_series", "delete_episode_file", "remove_series"}

    assert forbidden.isdisjoint(dir(SonarrTvClient))
