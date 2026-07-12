"""Configuration management for VOD Filter.

Loads and validates environment variables from .env file.
"""

import os
import sys
import json
from pathlib import Path
from typing import List, Optional, Dict, Any

from dotenv import load_dotenv


class ConfigurationError(Exception):
    """Raised when configuration is invalid or missing."""

    pass


class Config:
    """Application configuration loaded from environment variables."""

    def __init__(self, env_file: Optional[Path] = None):
        """Load configuration from environment variables.

        Args:
            env_file: Path to .env file (default: .env in current directory)

        Raises:
            ConfigurationError: If required configuration is missing or invalid
        """
        if env_file:
            load_dotenv(env_file)
        else:
            load_dotenv()

        self.watchlist_source: str = os.getenv(
            "WATCHLIST_SOURCE", "letterboxd"
        ).lower()
        uses_direct_source = self.watchlist_source == "letterboxd"

        # Letterboxd Configuration
        self.letterboxd_username: str = (
            self._require("LETTERBOXD_USERNAME")
            if uses_direct_source
            else os.getenv("LETTERBOXD_USERNAME", "")
        )

        # TMDB Configuration
        self.tmdb_api_key: str = (
            self._require("TMDB_API_KEY")
            if uses_direct_source
            else os.getenv("TMDB_API_KEY", "")
        )
        self.tmdb_region: str = os.getenv("TMDB_REGION", os.getenv("VOD_REGION", "PL")).upper()

        # Radarr Configuration
        self.radarr_url: str = self._require("RADARR_URL")
        self.radarr_api_key: str = self._require("RADARR_API_KEY")
        self.radarr_quality_profile_id: int = int(
            os.getenv("RADARR_QUALITY_PROFILE_ID", "1")
        )
        self.radarr_quality_profile: int = self.radarr_quality_profile_id  # Alias
        self.radarr_root_folder: str = os.getenv("RADARR_ROOT_FOLDER", "/movies/")

        # Plex Configuration
        self.plex_url: str = self._require("PLEX_URL")
        self.plex_token: str = self._require("PLEX_TOKEN")

        # VOD Provider Configuration
        self.vod_providers: List[int] = self._parse_providers(
            os.getenv("VOD_PROVIDERS", "119,1899,1773")
        )
        self.vod_region: str = os.getenv("VOD_REGION", "PL").upper()

        if len(self.vod_region) != 2:
            raise ConfigurationError(
                f"VOD_REGION must be 2-character ISO code, got: {self.vod_region}"
            )

        # Sync Settings
        self.sync_interval_seconds: int = self._parse_int(
            "SYNC_INTERVAL",
            default=None,
            minimum=1,
        ) or (
            self._parse_int("SYNC_INTERVAL_HOURS", default="6", minimum=1) * 3600
        )
        self.sync_interval_hours: int = self._parse_int(
            "SYNC_INTERVAL_HOURS",
            default=str(max(1, self.sync_interval_seconds // 3600)),
            minimum=1,
        )
        self.dry_run: bool = os.getenv("DRY_RUN", "false").lower() == "true"
        self.force_refresh: bool = os.getenv("FORCE_REFRESH", "false").lower() == "true"

        # Watchlist Source Integration
        self.watchlist_app_url: Optional[str] = (
            os.getenv("WATCHLIST_APP_URL", "").rstrip("/") or None
        )
        self.watchlist_app_sync_first: bool = (
            os.getenv("WATCHLIST_APP_SYNC_FIRST", "false").lower() == "true"
        )
        self.watchlist_app_sync_key: Optional[str] = (
            os.getenv("WATCHLIST_APP_SYNC_KEY", "").strip() or None
        )
        self.watchlist_app_timeout_seconds: int = self._parse_int(
            "WATCHLIST_APP_TIMEOUT_SECONDS",
            default="30",
            minimum=1,
        )
        self.watchlist_app_sync_timeout_seconds: int = self._parse_int(
            "WATCHLIST_APP_SYNC_TIMEOUT_SECONDS",
            default="900",
            minimum=1,
        )
        self.movie_sync_apply: bool = (
            os.getenv("MOVIE_SYNC_APPLY", "false").lower() == "true"
        )
        self.movie_sync_max_source_age_minutes: int = self._parse_int(
            "MOVIE_SYNC_MAX_SOURCE_AGE_MINUTES",
            default="120",
            minimum=1,
        )
        self.movie_sync_max_removal_count: int = self._parse_int(
            "MOVIE_SYNC_MAX_REMOVAL_COUNT",
            default="10",
            minimum=0,
        )
        self.movie_sync_max_removal_percent: float = self._parse_float(
            "MOVIE_SYNC_MAX_REMOVAL_PERCENT",
            default="25",
            minimum=0,
            maximum=100,
        )
        self.movie_sync_allow_watched_file_deletion: bool = (
            os.getenv(
                "MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION",
                "false",
            ).lower()
            == "true"
        )

        # Radarr Removal Settings
        self.radarr_delete_files_on_removal: bool = (
            os.getenv("RADARR_DELETE_FILES_ON_REMOVAL", "false").lower() == "true"
        )
        self.radarr_remove_when_vod_available: bool = (
            os.getenv("RADARR_REMOVE_WHEN_VOD_AVAILABLE", "true").lower() == "true"
        )
        self.radarr_delete_files_when_vod_available: bool = (
            os.getenv("RADARR_DELETE_FILES_WHEN_VOD_AVAILABLE", "false").lower() == "true"
        )

        # Plex Rate Limit Settings
        self.plex_rate_limit_wait_seconds: int = int(
            os.getenv("PLEX_RATE_LIMIT_WAIT_SECONDS", "60")
        )

        # Cache Settings
        self.cache_ttl_hours: int = int(os.getenv("CACHE_TTL_HOURS", "48"))
        self.plex_watchlist_cache_minutes: int = int(
            os.getenv("PLEX_WATCHLIST_CACHE_MINUTES", "30")
        )

        # Logging Configuration
        self.log_level: str = os.getenv("LOG_LEVEL", "INFO").upper()
        self.log_file: Optional[str] = os.getenv("LOG_FILE") or None

        # Database Configuration
        self.database_path: Path = Path(
            os.getenv("DATABASE_PATH", "./data/vod-filter.db")
        )

        # Ensure database directory exists
        self.database_path.parent.mkdir(parents=True, exist_ok=True)

    def _require(self, key: str) -> str:
        """Get required environment variable.

        Args:
            key: Environment variable name

        Returns:
            Value of environment variable

        Raises:
            ConfigurationError: If environment variable is not set
        """
        value = os.getenv(key)
        if not value:
            raise ConfigurationError(
                f"Required environment variable '{key}' is not set. "
                f"Please check your .env file or environment configuration."
            )
        return value

    def _parse_providers(self, providers_str: str) -> List[int]:
        """Parse comma-separated provider IDs.

        Args:
            providers_str: Comma-separated provider IDs (e.g., "119,384,531")

        Returns:
            List of provider IDs

        Raises:
            ConfigurationError: If provider IDs are invalid
        """
        try:
            providers = [int(p.strip()) for p in providers_str.split(",") if p.strip()]
            if not providers:
                raise ConfigurationError("VOD_PROVIDERS must contain at least one provider ID")
            return providers
        except ValueError as e:
            raise ConfigurationError(
                f"Invalid VOD_PROVIDERS format: {providers_str}. "
                f"Must be comma-separated integers."
            ) from e

    def _parse_int(
        self,
        key: str,
        default: Optional[str],
        minimum: Optional[int] = None,
    ) -> Optional[int]:
        """Parse an integer environment variable with configuration errors."""
        value = os.getenv(key, default)
        if value is None or str(value).strip() == "":
            return None

        try:
            parsed = int(str(value))
        except ValueError as e:
            raise ConfigurationError(f"{key} must be an integer, got: {value}") from e

        if minimum is not None and parsed < minimum:
            raise ConfigurationError(f"{key} must be >= {minimum}, got: {parsed}")

        return parsed

    def _parse_float(
        self,
        key: str,
        default: str,
        minimum: Optional[float] = None,
        maximum: Optional[float] = None,
    ) -> float:
        value = os.getenv(key, default)
        try:
            parsed = float(value)
        except (TypeError, ValueError) as e:
            raise ConfigurationError(f"{key} must be a number, got: {value}") from e

        if minimum is not None and parsed < minimum:
            raise ConfigurationError(f"{key} must be >= {minimum}, got: {parsed}")
        if maximum is not None and parsed > maximum:
            raise ConfigurationError(f"{key} must be <= {maximum}, got: {parsed}")
        return parsed

    def validate(self) -> None:
        """Validate configuration and connectivity.

        Raises:
            ConfigurationError: If configuration is invalid
        """
        # Validate Radarr URL format
        if not self.radarr_url.startswith(("http://", "https://")):
            raise ConfigurationError(
                f"RADARR_URL must start with http:// or https://, got: {self.radarr_url}"
            )

        # Validate quality profile ID
        if self.radarr_quality_profile_id < 1:
            raise ConfigurationError(
                f"RADARR_QUALITY_PROFILE_ID must be >= 1, got: {self.radarr_quality_profile_id}"
            )

        # Validate sync intervals
        if self.sync_interval_seconds < 1:
            raise ConfigurationError(
                f"SYNC_INTERVAL must be >= 1, got: {self.sync_interval_seconds}"
            )

        if self.sync_interval_hours < 1:
            raise ConfigurationError(
                f"SYNC_INTERVAL_HOURS must be >= 1, got: {self.sync_interval_hours}"
            )

        if self.watchlist_source not in {"letterboxd", "watchlist_app"}:
            raise ConfigurationError(
                "WATCHLIST_SOURCE must be either 'letterboxd' or 'watchlist_app'"
            )

        if self.watchlist_source == "watchlist_app" and not self.watchlist_app_url:
            raise ConfigurationError(
                "WATCHLIST_APP_URL is required when WATCHLIST_SOURCE=watchlist_app"
            )

        if self.watchlist_source == "watchlist_app" and not self.watchlist_app_sync_key:
            raise ConfigurationError(
                "WATCHLIST_APP_SYNC_KEY is required when WATCHLIST_SOURCE=watchlist_app"
            )

    def __repr__(self) -> str:
        """Return string representation with sensitive data redacted."""
        return (
            f"Config("
            f"letterboxd_username={self.letterboxd_username}, "
            f"tmdb_api_key={'***' if self.tmdb_api_key else 'NOT_SET'}, "
            f"radarr_url={self.radarr_url}, "
            f"radarr_api_key={'***' if self.radarr_api_key else 'NOT_SET'}, "
            f"plex_token={'***' if self.plex_token else 'NOT_SET'}, "
            f"vod_providers={self.vod_providers}, "
            f"vod_region={self.vod_region}, "
            f"dry_run={self.dry_run}"
            f")"
        )


def load_provider_config(config: Config) -> List[Dict[str, Any]]:
    """Load streaming provider configuration.

    Supports two formats:
    1. Simple: VOD_PROVIDERS="119,384,531" (comma-separated provider IDs)
    2. Advanced: VOD_PROVIDERS_JSON='[{"provider_id": 119, "provider_name": "Prime Video", "enabled": true}]'

    Args:
        config: Application configuration

    Returns:
        List of provider dicts with provider_id, provider_name, enabled, region
    """
    # Check for advanced JSON format first
    providers_json = os.getenv("VOD_PROVIDERS_JSON")
    if providers_json:
        try:
            providers = json.loads(providers_json)
            # Validate and add defaults
            result = []
            for provider in providers:
                if "provider_id" not in provider:
                    raise ConfigurationError(
                        "Each provider in VOD_PROVIDERS_JSON must have 'provider_id'"
                    )
                result.append({
                    "provider_id": provider["provider_id"],
                    "provider_name": provider.get("provider_name", f"Provider {provider['provider_id']}"),
                    "enabled": provider.get("enabled", True),
                    "region": provider.get("region", config.vod_region),
                })
            return result
        except json.JSONDecodeError as e:
            raise ConfigurationError(
                f"Invalid VOD_PROVIDERS_JSON format: {e}"
            ) from e

    # Fall back to simple format (comma-separated IDs)
    # Map common provider IDs to names
    provider_names = {
        119: "Amazon Prime Video",
        1899: "HBO Max",
        1773: "SkyShowtime",
        384: "HBO Max (legacy)",
        531: "Paramount Plus",
        8: "Netflix",
        337: "Disney Plus",
        350: "Apple TV Plus",
        # Add more as needed
    }

    result = []
    for provider_id in config.vod_providers:
        result.append({
            "provider_id": provider_id,
            "provider_name": provider_names.get(provider_id, f"Provider {provider_id}"),
            "enabled": True,
            "region": config.vod_region,
        })

    return result


def get_default_providers(region: str = "PL") -> List[Dict[str, Any]]:
    """Get default provider configuration for a region.

    Args:
        region: Two-letter region code

    Returns:
        List of default provider configurations
    """
    if region == "PL":
        return [
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
    else:
        # Generic defaults for other regions
        return [
            {
                "provider_id": 8,
                "provider_name": "Netflix",
                "enabled": True,
                "region": region,
            },
            {
                "provider_id": 119,
                "provider_name": "Amazon Prime Video",
                "enabled": True,
                "region": region,
            },
        ]


def load_config(env_file: Optional[str] = None) -> Config:
    """Load configuration from environment file.

    Args:
        env_file: Path to .env file (optional)

    Returns:
        Config instance

    Raises:
        ConfigurationError: If configuration is invalid
    """
    env_path = Path(env_file) if env_file else None
    config = Config(env_file=env_path)
    config.validate()
    return config


def ensure_directories(config: Config) -> None:
    """Ensure required directories exist.

    Args:
        config: Application configuration
    """
    # Database directory is already created in Config.__init__
    # This function is for any additional directories needed
    pass
