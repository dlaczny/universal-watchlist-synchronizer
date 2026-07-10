"""Sync Plex Library movies to Plex Watchlist.

This script ensures all movies in your Plex library are also in your Plex watchlist.
Designed to run as a cron job for continuous sync.

IMPORTANT: Uses optimized watchlist caching to avoid Plex API rate limiting.
"""

import os
import sys
from pathlib import Path

# Add src to path
sys.path.insert(0, str(Path(__file__).parent))

from dotenv import load_dotenv
import structlog

# Load environment variables
load_dotenv()

from src.clients.plex_client import PlexClient
from src.services.plex_service import PlexService
from src.services.cache_service import CacheService
from src.utils.logging import setup_logging

# Configure logging
structlog.configure(
    processors=[
        structlog.stdlib.add_log_level,
        structlog.processors.TimeStamper(fmt="iso"),
        structlog.processors.KeyValueRenderer(),
    ],
    wrapper_class=structlog.stdlib.BoundLogger,
    context_class=dict,
    logger_factory=structlog.PrintLoggerFactory(),
    cache_logger_on_first_use=True,
)

logger = structlog.get_logger(__name__)


def main():
    """Main entry point."""
    try:
        log_level = os.getenv("LOG_LEVEL", "INFO").upper()
        if "--quiet" in sys.argv:
            log_level = "WARNING"
        if "--verbose" in sys.argv:
            log_level = "DEBUG"
        if "--log-level" in sys.argv:
            idx = sys.argv.index("--log-level")
            if idx + 1 < len(sys.argv):
                log_level = sys.argv[idx + 1].upper()
        setup_logging(log_level=log_level, log_format="human")

        # Get configuration
        plex_url = os.getenv("PLEX_URL")
        plex_token = os.getenv("PLEX_TOKEN")
        plex_library_name = os.getenv("PLEX_LIBRARY_NAME", "Filmy")
        database_path = os.getenv("DATABASE_PATH", "data/vod-filter.db")
        dry_run = os.getenv("DRY_RUN", "false").lower() == "true"

        # Check for --dry-run flag
        if "--dry-run" in sys.argv:
            dry_run = True

        if not plex_url or not plex_token:
            logger.error("missing_config", error="PLEX_URL and PLEX_TOKEN are required")
            return 1

        logger.info(
            "starting_library_watchlist_sync",
            plex_url=plex_url,
            library_name=plex_library_name,
            dry_run=dry_run,
        )

        # Initialize clients
        plex_client = PlexClient(url=plex_url, token=plex_token)
        cache_service = CacheService(database_path=database_path)
        plex_service = PlexService(
            plex_client=plex_client,
            cache_service=cache_service,
        )

        # Run sync
        stats = plex_service.sync_library_to_watchlist(
            library_name=plex_library_name,
            dry_run=dry_run,
        )

        logger.info("sync_complete", stats=stats)

        # Return success or error
        if stats["errors"] > 0:
            return 2  # Errors occurred
        else:
            return 0  # Success

    except KeyboardInterrupt:
        logger.warning("sync_interrupted")
        return 130
    except Exception as e:
        logger.error("sync_failed", error=str(e))
        import traceback
        traceback.print_exc()
        return 1


if __name__ == "__main__":
    sys.exit(main())
