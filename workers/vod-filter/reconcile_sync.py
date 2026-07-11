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
        help="Do not call watchlist-app POST /api/sync/all before reading backend state",
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

    backend_client = WatchlistAppClient(config.watchlist_app_url)
    cache_service = CacheService(database_path=str(config.database_path))
    radarr_client = RadarrClient(
        url=config.radarr_url,
        api_key=config.radarr_api_key,
        root_folder=config.radarr_root_folder,
        quality_profile_id=config.radarr_quality_profile,
    )
    plex_client = PlexClient(url=config.plex_url, token=config.plex_token)

    sync_first = config.watchlist_app_sync_first and not args.skip_watchlist_app_sync
    backend_watchlist = _collect(
        "backend movie watchlist",
        lambda: backend_client.fetch_movie_watchlist(sync_first=sync_first),
        errors,
    )
    backend_radarr_export = _collect(
        "backend Radarr export",
        lambda: backend_client.fetch_radarr_movie_export(sync_first=False),
        errors,
    )
    cache_radarr = _collect(
        "worker cache Radarr candidates",
        cache_service.get_movies_for_radarr,
        errors,
    )
    cache_sync = _collect(
        "worker cache sync states",
        cache_service.get_all_sync_states,
        errors,
    )
    radarr_movies = _collect("Radarr movies", radarr_client.get_all_movies, errors)
    plex_watchlist = _collect("Plex watchlist", plex_client.get_watchlist, errors)
    plex_library = _collect(
        f"Plex library {args.library_name}",
        lambda: plex_client.get_library_movies(args.library_name),
        errors,
    )

    report = reconcile_sync_state(
        backend_watchlist_movies=backend_watchlist,
        backend_radarr_export_movies=backend_radarr_export,
        cache_radarr_movies=cache_radarr,
        cache_sync_states=cache_sync,
        radarr_movies=radarr_movies,
        plex_watchlist_movies=plex_watchlist,
        plex_library_movies=plex_library,
        collection_errors=errors,
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
