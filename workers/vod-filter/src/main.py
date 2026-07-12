"""Main CLI entry point for VOD Filter application.

Fire-and-forget execution with support for dry-run mode and autonomous operation.
"""

import sys
import argparse
import os
from pathlib import Path
import structlog

from src.config import load_config, ensure_directories, ConfigurationError, load_provider_config, get_default_providers
from src.utils.logging import setup_logging, get_logger
from src.utils.file_lock import exclusive_execution, LockAcquisitionError
from src.services.cache_service import CacheService
from src.services.letterboxd_service import LetterboxdService
from src.services.tmdb_service import TMDBService
from src.services.radarr_service import RadarrService
from src.services.plex_service import PlexService
from src.clients.plex_client import PlexClient, PlexError
from src.clients.watchlist_app_client import WatchlistAppClient


# Exit codes for autonomous operation monitoring
EXIT_SUCCESS = 0
EXIT_CONFIG_ERROR = 1
EXIT_API_ERROR = 2
EXIT_DATA_ERROR = 3
EXIT_LOCK_ERROR = 4


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments.

    Returns:
        Parsed arguments namespace
    """
    parser = argparse.ArgumentParser(
        description="VOD Filter - Smart VOD-aware import list manager for Radarr/Plex",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s                          # Normal run with config from .env
  %(prog)s --dry-run                # Preview changes without applying
  %(prog)s --force-refresh          # Bypass cache and re-check all movies
  %(prog)s --verbose --dry-run      # Dry run with detailed logging
  %(prog)s --config /path/to/.env   # Use specific config file

Exit codes:
  0 - Success
  1 - Configuration error
  2 - External API error
  3 - Data/database error
  4 - Lock acquisition error (another instance running)
        """,
    )

    parser.add_argument(
        "--config",
        type=str,
        default=None,
        help="Path to .env configuration file (default: .env in current directory)",
    )

    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Preview changes without applying them to external services",
    )

    parser.add_argument(
        "--force-refresh",
        action="store_true",
        help="Bypass cache and re-check all movies for VOD availability",
    )

    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Enable verbose (DEBUG) logging",
    )
    parser.add_argument(
        "--quiet",
        action="store_true",
        help="Only show warnings and errors",
    )
    parser.add_argument(
        "--log-level",
        choices=["DEBUG", "INFO", "WARNING", "ERROR", "debug", "info", "warning", "error"],
        default=None,
        help="Set log level explicitly",
    )

    parser.add_argument(
        "--version",
        action="version",
        version="%(prog)s 0.1.0",
    )

    return parser.parse_args()


def run_movie_sync_workflow(
    config,
    letterboxd_service,
    tmdb_service,
    radarr_service,
    plex_service,
    logger,
) -> dict:
    """Run the core movie sync workflow with injected service dependencies.

    This function contains the network-facing decision path but does not create
    clients itself. Production passes real services; tests pass fake services to
    safely exercise the full workflow without external calls.
    """
    logger.info("services_initialized")

    logger.info("step_1_fetching_watchlist")
    watchlist = letterboxd_service.fetch_watchlist()
    logger.info("watchlist_fetched", count=len(watchlist))

    empty_summary = {
        "watchlist_source": getattr(config, "watchlist_source", "letterboxd"),
        "watchlist_total": len(watchlist),
        "resolved": 0,
        "vod_available": 0,
        "radarr_candidates": 0,
        "radarr_added": 0,
        "radarr_removed": 0,
        "radarr_vod_cleanup": 0,
        "plex_added": 0,
        "plex_removed": 0,
        "plex_skipped": 0,
        "radarr_plex_added": 0,
        "radarr_plex_skipped": 0,
    }

    if not watchlist:
        logger.warning("empty_watchlist", message="No movies found in watchlist")
        return {"exit_code": EXIT_SUCCESS, "summary": empty_summary}

    logger.info("step_2_resolving_tmdb_ids", count=len(watchlist))
    movies = tmdb_service.batch_resolve_movies(watchlist)
    resolved_movies = [m for m in movies if m is not None]
    logger.info(
        "tmdb_resolution_complete",
        resolved=len(resolved_movies),
        failed=len(watchlist) - len(resolved_movies),
    )

    if not resolved_movies:
        logger.error("no_movies_resolved", message="Could not resolve any movies")
        return {"exit_code": EXIT_API_ERROR, "summary": empty_summary}

    logger.info("step_3_checking_vod_availability", count=len(resolved_movies))
    vod_status = {}
    unavailable_movies = []

    for movie in resolved_movies:
        is_available, _vod_records = tmdb_service.check_vod_availability(
            tmdb_id=movie.tmdb_id,
            configured_providers=config.vod_providers,
            force_refresh=config.force_refresh,
        )

        vod_status[movie.tmdb_id] = is_available

        if not is_available:
            unavailable_movies.append(movie)
            logger.info(
                "movie_not_on_vod",
                tmdb_id=movie.tmdb_id,
                title=movie.title,
                year=movie.year,
            )
        else:
            logger.debug(
                "movie_on_vod",
                tmdb_id=movie.tmdb_id,
                title=movie.title,
                year=movie.year,
            )

    logger.info(
        "vod_check_complete",
        total=len(resolved_movies),
        available_on_vod=len(resolved_movies) - len(unavailable_movies),
        unavailable=len(unavailable_movies),
    )

    radarr_stats = {}
    if unavailable_movies:
        logger.info("step_4_syncing_to_radarr", count=len(unavailable_movies))
        radarr_stats = radarr_service.sync_movies(
            movies=unavailable_movies,
            vod_status=vod_status,
            dry_run=config.dry_run,
        )
        logger.info("radarr_sync_complete", stats=radarr_stats)
    else:
        logger.info("no_movies_to_add", message="All movies available on VOD")

    logger.info("step_4b_detecting_radarr_removals")
    movies_to_remove_from_radarr = radarr_service.detect_removals(resolved_movies)

    radarr_removal_stats = {}
    if movies_to_remove_from_radarr:
        logger.info(
            "movies_to_remove_from_radarr",
            count=len(movies_to_remove_from_radarr),
            delete_files=config.radarr_delete_files_on_removal,
        )
        radarr_removal_stats = radarr_service.sync_removals(
            movies_to_remove=movies_to_remove_from_radarr,
            delete_files=config.radarr_delete_files_on_removal,
            dry_run=config.dry_run,
        )
        logger.info("radarr_removal_sync_complete", stats=radarr_removal_stats)
    else:
        logger.info("no_radarr_removals_needed")

    vod_cleanup_stats = {}
    if config.radarr_remove_when_vod_available:
        logger.info("step_4c_checking_vod_availability_for_downloads")

        def vod_check(tmdb_id, force_refresh=False):
            return tmdb_service.check_vod_availability(
                tmdb_id=tmdb_id,
                configured_providers=config.vod_providers,
                force_refresh=force_refresh,
            )

        vod_available_movies = radarr_service.detect_vod_available_movies(vod_check)

        if vod_available_movies:
            logger.info(
                "downloaded_movies_now_on_vod",
                count=len(vod_available_movies),
            )
            vod_cleanup_stats = radarr_service.sync_removals(
                movies_to_remove=vod_available_movies,
                delete_files=config.radarr_delete_files_when_vod_available,
                dry_run=config.dry_run,
            )
            logger.info("vod_cleanup_complete", stats=vod_cleanup_stats)
        else:
            logger.info("no_vod_cleanup_needed")
    else:
        logger.info("vod_availability_monitoring_disabled")

    logger.info("step_5_syncing_to_plex", count=len(resolved_movies))
    plex_stats = plex_service.sync_to_plex(
        letterboxd_movies=resolved_movies,
        vod_status=vod_status,
        dry_run=config.dry_run,
    )
    logger.info("plex_sync_complete", stats=plex_stats)

    logger.info("step_6_syncing_downloaded_radarr_movies")
    radarr_plex_stats = plex_service.sync_downloaded_radarr_movies(
        radarr_client=radarr_service.client,
        dry_run=config.dry_run,
    )
    logger.info("radarr_plex_sync_complete", stats=radarr_plex_stats)

    summary = {
        "watchlist_source": getattr(config, "watchlist_source", "letterboxd"),
        "watchlist_total": len(watchlist),
        "resolved": len(resolved_movies),
        "vod_available": len(resolved_movies) - len(unavailable_movies),
        "radarr_candidates": len(unavailable_movies),
        "radarr_added": radarr_stats.get("added", 0),
        "radarr_removed": radarr_removal_stats.get("removed", 0),
        "radarr_vod_cleanup": vod_cleanup_stats.get("removed", 0),
        "plex_added": plex_stats.get("added", 0),
        "plex_removed": plex_stats.get("removed", 0),
        "plex_skipped": plex_stats.get("skipped", 0),
        "radarr_plex_added": radarr_plex_stats.get("added", 0),
        "radarr_plex_skipped": radarr_plex_stats.get("skipped", 0),
    }

    logger.info("sync_summary", **summary)

    if config.dry_run:
        logger.info("dry_run_complete", message="No changes were applied")
    else:
        logger.info("sync_complete", message="All operations completed successfully")

    return {"exit_code": EXIT_SUCCESS, "summary": summary}


def main() -> int:
    """Main entry point for the application.

    Returns:
        Exit code (0 = success, non-zero = error)
    """
    args = parse_args()

    # Configure logging
    log_level = (args.log_level or os.getenv("LOG_LEVEL", "INFO")).upper()
    if args.verbose:
        log_level = "DEBUG"
    if args.quiet:
        log_level = "WARNING"
    try:
        setup_logging(log_level=log_level, log_format="human")  # Use human format for CLI
    except Exception as e:
        print(f"Failed to configure logging: {e}", file=sys.stderr)
        return EXIT_CONFIG_ERROR

    logger = get_logger(__name__)

    logger.info(
        "vod_filter_started",
        version="0.1.0",
        dry_run=args.dry_run,
        force_refresh=args.force_refresh,
    )

    try:
        # Load configuration
        logger.info("loading_configuration", config_file=args.config or ".env")
        config = load_config(env_file=args.config)

        # Override config with CLI flags
        if args.dry_run:
            config.dry_run = True
        if args.force_refresh:
            config.force_refresh = True

        # Ensure required directories exist
        ensure_directories(config)

        logger.info(
            "configuration_loaded",
            letterboxd_user=config.letterboxd_username,
            tmdb_region=config.tmdb_region,
            dry_run=config.dry_run,
            force_refresh=config.force_refresh,
        )

    except ConfigurationError as e:
        logger.error("configuration_error", message=str(e))
        return EXIT_CONFIG_ERROR
    except Exception as e:
        logger.error("unexpected_configuration_error", message=str(e), exc_info=True)
        return EXIT_CONFIG_ERROR

    try:
        # Use file locking to prevent concurrent execution
        with exclusive_execution(lock_file=f"{config.database_path}.lock"):
            logger.info("lock_acquired", lock_file=f"{config.database_path}.lock")

            # Initialize cache service
            try:
                cache_service = CacheService(database_path=config.database_path)
                stats = cache_service.get_database_stats()
                logger.info("cache_initialized", database_stats=stats)
            except Exception as e:
                logger.error("database_initialization_error", message=str(e), exc_info=True)
                return EXIT_DATA_ERROR

            # User Story 3: Initialize streaming provider configuration
            try:
                logger.info("initializing_provider_configuration")

                # Load provider configuration from environment
                provider_configs = load_provider_config(config)

                # Initialize default providers if none exist in database
                cache_service.initialize_default_providers(provider_configs)

                # Get enabled providers from database
                enabled_providers = cache_service.get_enabled_providers(config.vod_region)

                logger.info(
                    "provider_configuration_loaded",
                    region=config.vod_region,
                    total_providers=len(provider_configs),
                    enabled_providers=len(enabled_providers),
                    providers=[
                        f"{p['provider_name']} (ID: {p['provider_id']})"
                        for p in enabled_providers
                    ],
                )

                # Update config.vod_providers with enabled provider IDs for compatibility
                config.vod_providers = [p["provider_id"] for p in enabled_providers]

            except Exception as e:
                logger.error("provider_configuration_error", message=str(e), exc_info=True)
                return EXIT_CONFIG_ERROR

            # User Story 1: Filter Letterboxd watchlist by VOD availability
            try:
                # Initialize services
                if config.watchlist_source == "watchlist_app":
                    watchlist_app_client = WatchlistAppClient(
                        base_url=config.watchlist_app_url,
                        timeout_seconds=config.watchlist_app_timeout_seconds,
                        sync_timeout_seconds=config.watchlist_app_sync_timeout_seconds,
                        sync_key=config.watchlist_app_sync_key,
                    )

                    class WatchlistAppSourceService:
                        def fetch_watchlist(self):
                            return watchlist_app_client.fetch_radarr_movie_export(
                                sync_first=config.watchlist_app_sync_first,
                            )

                    letterboxd_service = WatchlistAppSourceService()
                else:
                    letterboxd_service = LetterboxdService(
                        username=config.letterboxd_username,
                        cache_service=cache_service,
                    )

                tmdb_service = TMDBService(
                    api_key=config.tmdb_api_key,
                    region=config.tmdb_region,
                    cache_service=cache_service,
                    cache_ttl_hours=config.cache_ttl_hours,
                )

                radarr_service = RadarrService(
                    url=config.radarr_url,
                    api_key=config.radarr_api_key,
                    root_folder=config.radarr_root_folder,
                    quality_profile_id=config.radarr_quality_profile,
                    cache_service=cache_service,
                )

                # Initialize Plex client and service (User Story 2)
                plex_client = PlexClient(
                    url=config.plex_url,
                    token=config.plex_token,
                )

                plex_service = PlexService(
                    plex_client=plex_client,
                    cache_service=cache_service,
                    cache_ttl_minutes=config.plex_watchlist_cache_minutes,
                )

                workflow_result = run_movie_sync_workflow(
                    config=config,
                    letterboxd_service=letterboxd_service,
                    tmdb_service=tmdb_service,
                    radarr_service=radarr_service,
                    plex_service=plex_service,
                    logger=logger,
                )
                return workflow_result["exit_code"]

            except Exception as e:
                logger.error("workflow_error", message=str(e), exc_info=True)
                return EXIT_API_ERROR

            return EXIT_SUCCESS

    except LockAcquisitionError as e:
        logger.error("lock_acquisition_failed", message=str(e))
        logger.info(
            "concurrent_execution_prevented",
            message="Another instance may be running. Check lock file.",
        )
        return EXIT_LOCK_ERROR
    except Exception as e:
        logger.error("unexpected_error", message=str(e), exc_info=True)
        return EXIT_API_ERROR


if __name__ == "__main__":
    sys.exit(main())
