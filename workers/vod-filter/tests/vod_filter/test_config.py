from __future__ import annotations

import sys
from pathlib import Path

import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.config import Config, ConfigurationError
from src.config import get_default_providers, load_provider_config


REQUIRED_ENV = {
    "LETTERBOXD_USERNAME": "user",
    "TMDB_API_KEY": "tmdb",
    "RADARR_URL": "http://radarr.local",
    "RADARR_API_KEY": "radarr",
    "PLEX_URL": "http://plex.local",
    "PLEX_TOKEN": "plex",
    "VOD_PROVIDERS": "119,384",
}


@pytest.fixture(autouse=True)
def clean_env(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    keys = set(REQUIRED_ENV) | {
        "SYNC_INTERVAL",
        "SYNC_INTERVAL_HOURS",
        "SYNC_INTERVAL_MAIN",
        "SYNC_INTERVAL_LIBRARY",
        "SYNC_INTERVAL_CLEANUP",
        "DATABASE_PATH",
        "VOD_PROVIDERS_JSON",
        "WATCHLIST_SOURCE",
        "WATCHLIST_APP_URL",
        "WATCHLIST_APP_SYNC_FIRST",
        "WATCHLIST_APP_SYNC_KEY",
        "MOVIE_SYNC_APPLY",
        "MOVIE_SYNC_MAX_SOURCE_AGE_MINUTES",
        "MOVIE_SYNC_MAX_REMOVAL_COUNT",
        "MOVIE_SYNC_MAX_REMOVAL_PERCENT",
    }
    for key in keys:
        monkeypatch.delenv(key, raising=False)

    for key, value in REQUIRED_ENV.items():
        monkeypatch.setenv(key, value)

    monkeypatch.setenv("DATABASE_PATH", str(tmp_path / "vod-filter.db"))


def test_sync_interval_seconds_prefers_sync_interval(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("SYNC_INTERVAL", "900")
    monkeypatch.setenv("SYNC_INTERVAL_HOURS", "6")

    config = Config()

    assert config.sync_interval_seconds == 900
    assert config.sync_interval_hours == 6


def test_sync_interval_seconds_falls_back_to_legacy_hours(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv("SYNC_INTERVAL", raising=False)
    monkeypatch.setenv("SYNC_INTERVAL_HOURS", "2")

    config = Config()

    assert config.sync_interval_seconds == 7200
    assert config.sync_interval_hours == 2


def test_cleanup_deletion_defaults_match_safety_policy() -> None:
    config = Config()

    assert config.radarr_delete_files_on_removal is False
    assert config.radarr_remove_when_vod_available is True
    assert config.radarr_delete_files_when_vod_available is False


def test_watchlist_source_defaults_to_letterboxd() -> None:
    config = Config()

    assert config.watchlist_source == "letterboxd"
    assert config.watchlist_app_url is None
    assert config.watchlist_app_sync_first is False


def test_watchlist_app_source_requires_url(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("WATCHLIST_SOURCE", "watchlist_app")

    with pytest.raises(ConfigurationError):
        Config().validate()


def test_watchlist_app_source_config(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("WATCHLIST_SOURCE", "watchlist_app")
    monkeypatch.setenv("WATCHLIST_APP_URL", "http://watchlist.local:5000/")
    monkeypatch.setenv("WATCHLIST_APP_SYNC_FIRST", "true")
    monkeypatch.setenv("WATCHLIST_APP_SYNC_KEY", "sync-secret")
    monkeypatch.setenv("MOVIE_SYNC_APPLY", "true")
    monkeypatch.setenv("MOVIE_SYNC_MAX_SOURCE_AGE_MINUTES", "90")
    monkeypatch.setenv("MOVIE_SYNC_MAX_REMOVAL_COUNT", "4")
    monkeypatch.setenv("MOVIE_SYNC_MAX_REMOVAL_PERCENT", "15.5")

    config = Config()
    config.validate()

    assert config.watchlist_source == "watchlist_app"
    assert config.watchlist_app_url == "http://watchlist.local:5000"
    assert config.watchlist_app_sync_first is True
    assert config.watchlist_app_sync_key == "sync-secret"
    assert config.movie_sync_apply is True
    assert config.movie_sync_max_source_age_minutes == 90
    assert config.movie_sync_max_removal_count == 4
    assert config.movie_sync_max_removal_percent == 15.5


def test_watchlist_app_source_requires_sync_key(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("WATCHLIST_SOURCE", "watchlist_app")
    monkeypatch.setenv("WATCHLIST_APP_URL", "http://watchlist.local:5000")

    with pytest.raises(ConfigurationError, match="WATCHLIST_APP_SYNC_KEY"):
        Config().validate()


@pytest.mark.parametrize(
    ("key", "value"),
    [
        ("MOVIE_SYNC_MAX_SOURCE_AGE_MINUTES", "0"),
        ("MOVIE_SYNC_MAX_REMOVAL_COUNT", "-1"),
        ("MOVIE_SYNC_MAX_REMOVAL_PERCENT", "101"),
    ],
)
def test_invalid_movie_sync_limits_raise_configuration_error(
    monkeypatch: pytest.MonkeyPatch,
    key: str,
    value: str,
) -> None:
    monkeypatch.setenv(key, value)

    with pytest.raises(ConfigurationError):
        Config()


def test_default_pl_provider_ids_match_current_tmdb_catalog(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    import src.config as config_module

    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(config_module, "load_dotenv", lambda *args, **kwargs: None)
    monkeypatch.delenv("VOD_PROVIDERS", raising=False)

    config = Config()

    assert config.vod_providers == [119, 1899, 1773]
    assert get_default_providers("PL") == [
        {
            "provider_id": 119,
            "provider_name": "Amazon Prime Video",
            "enabled": True,
            "region": "PL",
        },
        {
            "provider_id": 1899,
            "provider_name": "HBO Max",
            "enabled": True,
            "region": "PL",
        },
        {
            "provider_id": 1773,
            "provider_name": "SkyShowtime",
            "enabled": True,
            "region": "PL",
        },
    ]


def test_simple_provider_config_uses_known_current_names(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("VOD_PROVIDERS", "119,1899,1773")

    config = Config()
    providers = load_provider_config(config)

    assert providers == [
        {
            "provider_id": 119,
            "provider_name": "Amazon Prime Video",
            "enabled": True,
            "region": "PL",
        },
        {
            "provider_id": 1899,
            "provider_name": "HBO Max",
            "enabled": True,
            "region": "PL",
        },
        {
            "provider_id": 1773,
            "provider_name": "SkyShowtime",
            "enabled": True,
            "region": "PL",
        },
    ]


@pytest.mark.parametrize("key", ["SYNC_INTERVAL", "SYNC_INTERVAL_HOURS"])
def test_invalid_sync_intervals_raise_configuration_error(
    monkeypatch: pytest.MonkeyPatch,
    key: str,
) -> None:
    monkeypatch.setenv(key, "0")

    with pytest.raises(ConfigurationError):
        Config().validate()

