"""Generate a read-only sync reconciliation report.

This command reads backend, worker cache, Radarr, and Plex state, then writes a
Markdown report of expected adds, keeps, removals, skips, uncertain cache state,
and collection errors. It does not call mutating endpoints.
"""

from __future__ import annotations

import argparse
import os
import sys
from datetime import datetime
from pathlib import Path
from typing import Any, Callable

from dotenv import load_dotenv

sys.path.insert(0, str(Path(__file__).parent))

from src.clients.plex_client import PlexClient
from src.clients.radarr_client import RadarrClient
from src.clients.watchlist_app_client import WatchlistAppClient
from src.config import ConfigurationError, load_config
from src.services.cache_service import CacheService
from src.services.sync_reconciliation import (
    reconcile_sync_state,
    write_markdown_report,
)
from src.utils.logging import setup_logging


def parse_args(argv=None):
    parser = argparse.ArgumentParser(
        description="Generate a read-only backend/worker/Radarr/Plex sync reconciliation report"
    )
    parser.add_argument("--config", default=None, help="Optional .env path")
    parser.add_argument("--report-file", default=None, help="Output Markdown report path")
    parser.add_argument(
        "--skip-watchlist-app-sync",
        action="store_true",
        help="Do not call watchlist-app POST /api/sync/movies before reading backend state",
    )
    parser.add_argument(
        "--library-name",
        default=os.getenv("PLEX_LIBRARY_NAME", "Filmy"),
        help="Plex library name to read for library-to-watchlist reconciliation",
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
    return parser.parse_args(argv)


def main(argv=None) -> int:
    load_dotenv()
    args = parse_args(argv)
    log_level = (args.log_level or os.getenv("LOG_LEVEL", "INFO")).upper()
    if args.quiet:
        log_level = "WARNING"
    setup_logging(log_level=log_level, log_format="human")

    try:
        config = load_config(args.config)
    except ConfigurationError as e:
        print(f"Configuration error: {e}", file=sys.stderr)
        return 1

    if not config.watchlist_app_url:
        print("WATCHLIST_APP_URL is required for sync reconciliation.", file=sys.stderr)
        return 1

    errors: list[str] = []

    backend_client = WatchlistAppClient(
        config.watchlist_app_url,
        timeout_seconds=config.watchlist_app_timeout_seconds,
        sync_timeout_seconds=config.watchlist_app_sync_timeout_seconds,
        sync_key=config.watchlist_app_sync_key,
    )
    cache_service = CacheService(database_path=str(config.database_path))
    radarr_client = RadarrClient(
        url=config.radarr_url,
        api_key=config.radarr_api_key,
        root_folder=config.radarr_root_folder,
        quality_profile_id=config.radarr_quality_profile,
    )
    plex_client = PlexClient(url=config.plex_url, token=config.plex_token)

    sync_first = config.watchlist_app_sync_first and not args.skip_watchlist_app_sync
    try:
        backend_snapshot = backend_client.fetch_movie_sync_snapshot(
            sync_first=sync_first
        )
    except Exception as error:
        errors.append(f"backend movie snapshot: {error}")
        backend_snapshot = {
            "source_snapshot_id": None,
            "movies": [],
            "watched_movies": [],
            "generated_at": None,
            "last_successful_movie_sync_at": None,
        }
    managed_destinations = _collect(
        "worker managed destinations",
        cache_service.get_managed_destinations,
        errors,
    )
    radarr_observations = _collect(
        "Radarr observations",
        cache_service.get_radarr_observations,
        errors,
    )
    radarr_movies = _collect("Radarr movies", radarr_client.get_all_movies, errors)
    radarr_exclusions = _collect(
        "Radarr exclusions",
        radarr_client.get_exclusions,
        errors,
    )
    plex_watchlist = _collect("Plex watchlist", plex_client.get_watchlist, errors)
    plex_library = _collect(
        f"Plex library {args.library_name}",
        lambda: plex_client.get_library_movies(args.library_name),
        errors,
    )

    report = reconcile_sync_state(
        backend_snapshot_movies=backend_snapshot["movies"],
        backend_watched_movies=backend_snapshot["watched_movies"],
        radarr_movies=radarr_movies,
        radarr_observations=radarr_observations,
        plex_watchlist_movies=plex_watchlist,
        plex_library_movies=plex_library,
        managed_destinations=managed_destinations,
        collection_errors=errors,
        source_snapshot_at=backend_snapshot["generated_at"],
        source_last_successful_sync_at=backend_snapshot[
            "last_successful_movie_sync_at"
        ],
        source_snapshot_id=backend_snapshot["source_snapshot_id"],
        radarr_exclusions=radarr_exclusions,
    )

    report_path = (
        Path(args.report_file)
        if args.report_file
        else Path("data/reports")
        / f"sync-reconciliation-{datetime.now().strftime('%Y%m%d-%H%M%S')}.md"
    )
    written = write_markdown_report(report, report_path)
    print(f"Sync reconciliation report written: {written}")
    print(_summary_line(report.decisions))
    return 2 if errors else 0


def _collect(name: str, operation: Callable[[], list[dict[str, Any]]], errors: list[str]):
    try:
        return operation()
    except Exception as e:
        errors.append(f"{name}: {e}")
        return []


def _summary_line(decisions) -> str:
    counts: dict[tuple[str, str], int] = {}
    for decision in decisions:
        key = (decision.area, decision.action)
        counts[key] = counts.get(key, 0) + 1

    if not counts:
        return "Summary: no decisions"

    parts = [
        f"{area}/{action}={count}"
        for (area, action), count in sorted(counts.items())
    ]
    return "Summary: " + " ".join(parts)


if __name__ == "__main__":
    raise SystemExit(main())
