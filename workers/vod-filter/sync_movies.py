"""Single production entry point for movie plan-and-apply synchronization."""

from __future__ import annotations

import argparse
import os
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from dotenv import load_dotenv

sys.path.insert(0, str(Path(__file__).parent))

from healthcheck import write_heartbeat
from src.clients.plex_client import PlexClient
from src.clients.radarr_client import RadarrClient
from src.clients.watchlist_app_client import WatchlistAppClient
from src.config import ConfigurationError, load_config
from src.services.cache_service import CacheService
from src.services.movie_sync_collector import MovieSyncCollector
from src.services.movie_sync_executor import MovieSyncExecutor
from src.services.movie_sync_policy import SyncPolicy, evaluate_plan
from src.services.movie_sync_report import write_movie_sync_reports
from src.services.sync_reconciliation import reconcile_sync_state
from src.utils.logging import setup_logging


@dataclass(frozen=True)
class MovieSyncRunResult:
    exit_code: int
    blockers: tuple[str, ...]
    json_path: Path
    markdown_path: Path


def execute_movie_sync(
    *,
    collector,
    executor,
    cache_service,
    policy: SyncPolicy,
    sync_first: bool,
    apply: bool,
    report_dir: Path,
    now: datetime | None = None,
) -> MovieSyncRunResult:
    """Collect, plan, policy-check, optionally apply, report, and record one run."""
    current_time = now or datetime.now(timezone.utc)
    run_id = cache_service.start_run(
        workflow="movie_sync",
        dry_run=not apply,
        trigger="sync_movies",
    )

    try:
        state = collector.collect(sync_first=sync_first)
        report = reconcile_sync_state(
            backend_snapshot_movies=state.backend_movies,
            radarr_movies=state.radarr_movies,
            plex_watchlist_movies=state.plex_watchlist_movies,
            plex_library_movies=state.plex_library_movies,
            managed_destinations=state.managed_destinations,
            collection_errors=state.collection_errors,
            source_snapshot_at=state.source_snapshot_at,
            source_last_successful_sync_at=state.source_last_successful_sync_at,
            radarr_exclusions=state.radarr_exclusions,
        )
        blockers = evaluate_plan(report, policy, now=current_time)
        execution = executor.execute(report, blockers, apply)

        effective_blockers = [
            blocker
            for blocker in blockers
            if apply or blocker != "mutation_disabled"
        ]
        if state.collection_errors:
            exit_code = 1
        elif execution.errors:
            exit_code = 2
        elif effective_blockers:
            exit_code = 3
        else:
            exit_code = 0

        paths = write_movie_sync_reports(
            execution.report,
            blockers,
            report_dir,
            run_id,
        )
        cache_service.finish_run(
            run_id,
            status="success" if exit_code == 0 else "failed",
            exit_code=exit_code,
            summary={
                "blockers": blockers,
                "decisions": len(execution.report.decisions),
                "execution_errors": list(execution.errors),
            },
            error="; ".join(execution.errors) or None,
        )
        return MovieSyncRunResult(
            exit_code,
            tuple(blockers),
            paths.json_path,
            paths.markdown_path,
        )
    except Exception as error:
        cache_service.finish_run(
            run_id,
            status="error",
            exit_code=1,
            error=str(error),
        )
        raise


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Plan and apply safe movie synchronization")
    parser.add_argument("--apply", action="store_true", help="Apply policy-approved actions")
    parser.add_argument(
        "--skip-backend-sync",
        action="store_true",
        help="Read the current backend snapshot without triggering movie sync",
    )
    parser.add_argument("--report-dir", default="data/reports")
    parser.add_argument("--quiet", action="store_true")
    parser.add_argument(
        "--log-level",
        choices=["DEBUG", "INFO", "WARNING", "ERROR", "debug", "info", "warning", "error"],
        default=None,
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
        config = load_config()
        config.validate()
    except ConfigurationError as error:
        print(f"Configuration error: {error}", file=sys.stderr)
        return 1

    if config.watchlist_source != "watchlist_app":
        print(
            "Configuration error: sync_movies.py requires WATCHLIST_SOURCE=watchlist_app",
            file=sys.stderr,
        )
        return 1

    cache = CacheService(database_path=str(config.database_path))
    backend = WatchlistAppClient(
        config.watchlist_app_url,
        timeout_seconds=config.watchlist_app_timeout_seconds,
        sync_timeout_seconds=config.watchlist_app_sync_timeout_seconds,
        sync_key=config.watchlist_app_sync_key,
    )
    radarr = RadarrClient(
        url=config.radarr_url,
        api_key=config.radarr_api_key,
        root_folder=config.radarr_root_folder,
        quality_profile_id=config.radarr_quality_profile,
    )
    plex = PlexClient(url=config.plex_url, token=config.plex_token)
    collector = MovieSyncCollector(
        backend_client=backend,
        radarr_client=radarr,
        plex_client=plex,
        cache_service=cache,
        library_name=os.getenv("PLEX_LIBRARY_NAME", "Movies"),
    )
    executor = MovieSyncExecutor(radarr, plex, cache)
    apply = args.apply or config.movie_sync_apply
    policy = SyncPolicy(
        max_source_age_minutes=config.movie_sync_max_source_age_minutes,
        max_removal_count=config.movie_sync_max_removal_count,
        max_removal_percent=config.movie_sync_max_removal_percent,
        allow_mutation=apply,
    )

    try:
        result = execute_movie_sync(
            collector=collector,
            executor=executor,
            cache_service=cache,
            policy=policy,
            sync_first=config.watchlist_app_sync_first and not args.skip_backend_sync,
            apply=apply,
            report_dir=Path(args.report_dir),
        )
    except Exception as error:
        print(f"Movie sync failed: {error}", file=sys.stderr)
        return 1

    heartbeat_status = {
        0: "completed" if apply else "reconciliation",
        2: "partial",
        3: "blocked",
    }.get(result.exit_code, "failed")
    write_heartbeat(
        config.database_path.parent / "last-run.json",
        status=heartbeat_status,
        exit_code=result.exit_code,
    )
    print(f"Movie sync JSON report: {result.json_path}")
    print(f"Movie sync Markdown report: {result.markdown_path}")
    return result.exit_code


if __name__ == "__main__":
    raise SystemExit(main())
