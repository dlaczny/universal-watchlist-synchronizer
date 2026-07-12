from __future__ import annotations

import importlib
import sys
from pathlib import Path

import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))


@pytest.fixture()
def reconcile_sync(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.chdir(VOD_FILTER_ROOT)
    module = importlib.import_module("reconcile_sync")
    return importlib.reload(module)


def test_reconcile_sync_cli_writes_read_only_report(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    reconcile_sync,
):
    calls = []

    class FakeConfig:
        watchlist_app_url = "http://watchlist.local"
        watchlist_app_sync_first = True
        watchlist_app_sync_key = "sync-secret"
        watchlist_app_timeout_seconds = 30
        watchlist_app_sync_timeout_seconds = 900
        database_path = tmp_path / "vod-filter.db"
        radarr_url = "http://radarr.local"
        radarr_api_key = "radarr-key"
        radarr_root_folder = "/movies"
        radarr_quality_profile = 1
        plex_url = "http://plex.local"
        plex_token = "plex-token"

    class FakeWatchlistAppClient:
        def __init__(
            self,
            base_url,
            timeout_seconds=None,
            sync_timeout_seconds=None,
            sync_key=None,
        ):
            self.base_url = base_url
            assert timeout_seconds == 30
            assert sync_timeout_seconds == 900
            assert sync_key == "sync-secret"

        def fetch_movie_sync_snapshot(self, sync_first=False):
            calls.append(("backend_snapshot", sync_first))
            return {
                "generated_at": None,
                "last_successful_movie_sync_at": None,
                "movies": [
                    {
                        "title": "Backend Movie",
                        "year": 2024,
                        "tmdb_id": 101,
                        "metadata_status": "enriched",
                        "radarr_eligible": True,
                        "availability_status": "not_on_plex",
                    }
                ],
            }

    class FakeCacheService:
        def __init__(self, database_path):
            self.database_path = database_path

        def get_managed_destinations(self):
            calls.append(("managed_destinations", None))
            return []

    class FakeRadarrClient:
        def __init__(self, *args, **kwargs):
            pass

        def get_all_movies(self):
            calls.append(("radarr", None))
            return []

    class FakePlexClient:
        def __init__(self, *args, **kwargs):
            pass

        def get_watchlist(self):
            calls.append(("plex_watchlist", None))
            return []

        def get_library_movies(self, library_name):
            calls.append(("plex_library", library_name))
            return []

    report_path = tmp_path / "sync-report.md"
    monkeypatch.setattr(reconcile_sync, "load_config", lambda env_file=None: FakeConfig())
    monkeypatch.setattr(reconcile_sync, "WatchlistAppClient", FakeWatchlistAppClient)
    monkeypatch.setattr(reconcile_sync, "CacheService", FakeCacheService)
    monkeypatch.setattr(reconcile_sync, "RadarrClient", FakeRadarrClient)
    monkeypatch.setattr(reconcile_sync, "PlexClient", FakePlexClient)

    exit_code = reconcile_sync.main(["--report-file", str(report_path), "--library-name", "Movies"])

    assert exit_code == 0
    assert report_path.exists()
    assert "- backend_snapshot: 1" in report_path.read_text(encoding="utf-8")
    assert ("backend_snapshot", True) in calls
    assert ("managed_destinations", None) in calls
    assert ("plex_library", "Movies") in calls
