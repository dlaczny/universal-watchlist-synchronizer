"""Main entry point for VOD Filter service.

Supports both single-run and continuous sync modes.

Usage:
    # Single run (default)
    python main.py

    # Continuous sync mode (run every N seconds)
    python main.py --continuous --interval 3600

    # Dry run mode
    python main.py --dry-run

Environment Variables:
    SYNC_INTERVAL - Sync interval in seconds (default: 3600)
    DRY_RUN - Enable dry-run mode (default: false)
"""

import argparse
import os
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from dotenv import load_dotenv
import structlog

# Load environment variables
load_dotenv()

# Configure logging
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


def parse_args():
    """Parse command line arguments."""
    parser = argparse.ArgumentParser(
        description="VOD Filter - Sync Letterboxd with Plex and Radarr"
    )

    parser.add_argument(
        "--continuous",
        action="store_true",
        help="Run continuously with periodic syncs (default: single run)",
    )

    parser.add_argument(
        "--interval",
        type=int,
        default=int(os.getenv("SYNC_INTERVAL", "3600")),
        help="Sync interval in seconds for continuous mode (default: 3600 = 1 hour)",
    )

    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Preview changes without applying them",
    )

    return parser.parse_args()


def run_sync():
    """Execute a single sync run."""
    from sync_movies import main as sync_movies_main

    logger.info("starting_sync_run")
    exit_code = sync_movies_main([])

    if exit_code == 0:
        logger.info("sync_run_completed_successfully")
    elif exit_code == 2:
        logger.warning("sync_run_completed_with_errors")
    else:
        logger.error("sync_run_failed", exit_code=exit_code)

    return exit_code


def continuous_sync(interval: int):
    """Run syncs continuously at specified interval.

    Args:
        interval: Time to wait between syncs in seconds
    """
    logger.info(
        "starting_continuous_sync_mode",
        interval_seconds=interval,
        interval_human=f"{interval // 3600}h {(interval % 3600) // 60}m"
        if interval >= 3600
        else f"{interval // 60}m {interval % 60}s"
        if interval >= 60
        else f"{interval}s",
    )

    run_count = 0

    while True:
        try:
            run_count += 1
            logger.info("sync_run_starting", run_number=run_count)

            # Run the sync
            exit_code = run_sync()

            if exit_code == 0:
                logger.info("sync_run_succeeded", run_number=run_count)
            else:
                logger.warning(
                    "sync_run_failed_but_continuing",
                    run_number=run_count,
                    exit_code=exit_code,
                )

            # Wait for next sync
            next_run = time.strftime(
                "%Y-%m-%d %H:%M:%S", time.localtime(time.time() + interval)
            )
            logger.info(
                "waiting_for_next_sync",
                wait_seconds=interval,
                next_run_at=next_run,
            )
            time.sleep(interval)

        except KeyboardInterrupt:
            logger.warning("continuous_sync_interrupted_by_user")
            return 130

        except Exception as e:
            logger.error(
                "sync_run_crashed_but_continuing",
                run_number=run_count,
                error=str(e),
                wait_seconds=interval,
            )
            # Wait before retrying
            time.sleep(interval)


def main():
    """Main entry point."""
    args = parse_args()

    # Set dry-run in environment if specified
    if args.dry_run:
        os.environ["DRY_RUN"] = "true"
        os.environ["MOVIE_SYNC_APPLY"] = "false"

    # Print startup banner
    print("\n" + "=" * 80)
    print("  VOD FILTER SERVICE")
    print("=" * 80)
    print(f"  Mode: {'CONTINUOUS' if args.continuous else 'SINGLE RUN'}")

    if args.continuous:
        print(f"  Interval: {args.interval}s", end="")
        if args.interval >= 3600:
            print(
                f" ({args.interval // 3600}h {(args.interval % 3600) // 60}m)",
                end="",
            )
        elif args.interval >= 60:
            print(f" ({args.interval // 60}m {args.interval % 60}s)", end="")
        print()

    print(f"  Dry Run: {'YES' if args.dry_run else 'NO'}")
    print(f"  Started: {time.strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 80 + "\n")

    if args.continuous:
        # Run in continuous mode
        return continuous_sync(args.interval)
    else:
        # Run once and exit
        return run_sync()


if __name__ == "__main__":
    try:
        exit_code = main()
        sys.exit(exit_code)
    except KeyboardInterrupt:
        logger.warning("service_interrupted")
        sys.exit(130)
    except Exception as e:
        logger.error("service_crashed", error=str(e))
        import traceback

        traceback.print_exc()
        sys.exit(1)
