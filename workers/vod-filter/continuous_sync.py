"""Run movie and optional TV synchronization on independent schedules."""

from __future__ import annotations

import argparse
import os
import time
from collections.abc import Callable

from dotenv import load_dotenv
import structlog


load_dotenv()

structlog.configure(
    processors=[
        structlog.stdlib.add_log_level,
        structlog.processors.TimeStamper(fmt="%Y-%m-%d %H:%M:%S"),
        structlog.dev.ConsoleRenderer(colors=True),
    ],
    wrapper_class=structlog.stdlib.BoundLogger,
    context_class=dict,
    logger_factory=structlog.PrintLoggerFactory(),
    cache_logger_on_first_use=True,
)
logger = structlog.get_logger(__name__)


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Run independent movie and TV worker schedules")
    parser.add_argument("--continuous", action="store_true", help="Run continuously (default: single run)")
    parser.add_argument(
        "--interval",
        type=int,
        default=int(os.getenv("SYNC_INTERVAL", "3600")),
        help="Movie sync interval in seconds",
    )
    parser.add_argument(
        "--tv-interval",
        type=int,
        default=int(os.getenv("TV_SYNC_INTERVAL_SECONDS", "900")),
        help="TV sync interval in seconds",
    )
    parser.add_argument("--dry-run", action="store_true", help="Disable movie and TV apply gates")
    return parser.parse_args(argv)


def run_sync() -> int:
    """Execute the backwards-compatible movie entry point."""
    from sync_movies import main as sync_movies_main

    return sync_movies_main([])


def run_tv_sync() -> int:
    """Lazily execute TV work only when the TV schedule is enabled."""
    from sync_tv import main as sync_tv_main

    apply_args = ["--apply"] if os.getenv("TV_SYNC_APPLY", "false").lower() == "true" else []
    return sync_tv_main(apply_args)


def _advance_deadline(deadline: float, interval: int, now: float) -> float:
    while deadline <= now:
        deadline += interval
    return deadline


def _run_workflow(name: str, operation: Callable[[], int]) -> None:
    try:
        exit_code = operation()
        if exit_code == 0:
            logger.info("sync_run_succeeded", workflow=name)
        else:
            logger.warning("sync_run_failed_but_continuing", workflow=name, exit_code=exit_code)
    except Exception as error:
        logger.error("sync_run_crashed_but_continuing", workflow=name, error=str(error))


def run_scheduled_syncs(
    *,
    movie_interval: int,
    tv_interval: int,
    tv_enabled: bool,
    run_movie: Callable[[], int] = run_sync,
    run_tv: Callable[[], int] = run_tv_sync,
    monotonic: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
    stop_after: float | None = None,
) -> int:
    """Run due workflows without allowing one workflow to shift the other's deadline."""
    if movie_interval < 1 or tv_interval < 1:
        raise ValueError("sync intervals must be positive")

    started_at = monotonic()
    next_movie = started_at
    next_tv = started_at if tv_enabled else None
    while True:
        now = monotonic()
        if stop_after is not None and now > started_at + stop_after:
            return 0

        if now >= next_movie:
            _run_workflow("movie_sync", run_movie)
            next_movie = _advance_deadline(next_movie, movie_interval, monotonic())

        if next_tv is not None and monotonic() >= next_tv:
            _run_workflow("tv_sync", run_tv)
            next_tv = _advance_deadline(next_tv, tv_interval, monotonic())

        deadlines = [next_movie]
        if next_tv is not None:
            deadlines.append(next_tv)
        delay = max(0.0, min(deadlines) - monotonic())
        sleep(delay)


def continuous_sync(interval: int, tv_interval: int | None = None) -> int:
    """Keep the legacy continuous_sync API while scheduling TV independently."""
    return run_scheduled_syncs(
        movie_interval=interval,
        tv_interval=tv_interval or int(os.getenv("TV_SYNC_INTERVAL_SECONDS", "900")),
        tv_enabled=os.getenv("TV_SYNC_ENABLED", "false").lower() == "true",
    )


def main(argv=None) -> int:
    args = parse_args(argv)
    if args.dry_run:
        os.environ["DRY_RUN"] = "true"
        os.environ["MOVIE_SYNC_APPLY"] = "false"
        os.environ["TV_SYNC_APPLY"] = "false"

    if args.continuous:
        return continuous_sync(args.interval, args.tv_interval)
    return run_sync()


if __name__ == "__main__":
    raise SystemExit(main())
