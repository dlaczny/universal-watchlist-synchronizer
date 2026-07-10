"""Validate configured TMDB movie watch providers for the configured region."""

import sys
import logging
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

import structlog

from src.config import ConfigurationError, load_config, load_provider_config
from src.clients.tmdb_client import TMDBClient
from src.utils.logging import setup_logging


logger = structlog.get_logger(__name__)


def main() -> int:
    """Validate VOD provider IDs against TMDB's regional movie provider catalog."""
    setup_logging(log_level="INFO", log_format="human")
    logging.getLogger("httpx").setLevel(logging.WARNING)

    try:
        config = load_config()
        configured = load_provider_config(config)
        client = TMDBClient(api_key=config.tmdb_api_key, region=config.vod_region)
        catalog = client.get_movie_provider_catalog(config.vod_region)
    except ConfigurationError as e:
        logger.error("configuration_error", message=str(e))
        return 1
    except Exception as e:
        logger.error("provider_validation_failed", error=str(e))
        return 2

    catalog_by_id = {provider["provider_id"]: provider for provider in catalog}
    configured_ids = [provider["provider_id"] for provider in configured if provider["enabled"]]
    missing_ids = [provider_id for provider_id in configured_ids if provider_id not in catalog_by_id]

    print(f"TMDB movie providers for region {config.vod_region}: {len(catalog)} available")
    print("Configured providers:")
    for provider in configured:
        provider_id = provider["provider_id"]
        catalog_provider = catalog_by_id.get(provider_id)
        if catalog_provider:
            print(f"  OK      {provider_id}: {catalog_provider['provider_name']}")
        else:
            print(f"  MISSING {provider_id}: {provider['provider_name']}")

    if missing_ids:
        print(f"Missing provider IDs for {config.vod_region}: {missing_ids}")
        return 3

    return 0


if __name__ == "__main__":
    sys.exit(main())
