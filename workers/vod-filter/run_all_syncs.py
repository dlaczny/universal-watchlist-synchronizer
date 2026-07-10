"""Run all VOD Filter sync operations.

This is the unified entry point that runs:
1. Cleanup (remove movies deleted from Letterboxd)
2. Main Sync (Letterboxd → Plex/Radarr)
3. Library Sync (Plex Library → Watchlist)

Operations run sequentially by default for safer production behavior. Parallel
execution remains available with --parallel for manual use.

Designed to be compiled into a single executable or run directly.
"""

import os
import sys
import argparse
from pathlib import Path
import time
from concurrent.futures import ThreadPoolExecutor, as_completed

# Add src to path
sys.path.insert(0, str(Path(__file__).parent))

from dotenv import load_dotenv
import structlog
from src.utils.logging import setup_logging
from src.services.cache_service import CacheService

# Load environment variables
load_dotenv()

# Configure logging
structlog.configure(
    processors=[
        structlog.stdlib.add_log_level,
        structlog.processors.TimeStamper(fmt="%H:%M:%S"),
        structlog.dev.ConsoleRenderer(colors=True),
    ],
    wrapper_class=structlog.stdlib.BoundLogger,
    context_class=dict,
    logger_factory=structlog.PrintLoggerFactory(),
    cache_logger_on_first_use=True,
)

logger = structlog.get_logger(__name__)


def run_with_history(workflow: str, operation):
    """Run one operation and persist start/finish status to run_history."""
    database_path = os.getenv("DATABASE_PATH", "data/vod-filter.db")
    dry_run = os.getenv("DRY_RUN", "false").lower() == "true"
    cache = CacheService(database_path=database_path)
    run_id = cache.start_run(workflow=workflow, dry_run=dry_run, trigger="run_all_syncs")

    try:
        result = operation()
        name, success, error = result
        cache.finish_run(
            run_id=run_id,
            status="success" if success else "failed",
            exit_code=0 if success else 1,
            summary={"name": name},
            error=error,
        )
        return result
    except KeyboardInterrupt:
        cache.finish_run(run_id=run_id, status="interrupted", exit_code=130)
        raise
    except Exception as e:
        cache.finish_run(run_id=run_id, status="error", exit_code=1, error=str(e))
        raise


def parse_args(argv=None):
    """Parse combined sync runner arguments."""
    parser = argparse.ArgumentParser(
        description="Run VOD Filter cleanup, main sync, and library sync"
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Preview changes without applying them in child syncs",
    )
    parser.add_argument(
        "--parallel",
        action="store_true",
        help="Run all sync operations concurrently instead of sequentially",
    )
    parser.add_argument(
        "--quiet",
        action="store_true",
        help="Only show warnings and errors in parent and child syncs",
    )
    parser.add_argument(
        "--log-level",
        choices=["DEBUG", "INFO", "WARNING", "ERROR", "debug", "info", "warning", "error"],
        default=None,
        help="Set log level for parent and child syncs",
    )
    return parser.parse_args(argv)


def run_cleanup():
    """Run cleanup operation."""
    logger.info("starting_cleanup_sync")

    try:
        # Import and run cleanup
        from cleanup_removed_movies import main as cleanup_main
        exit_code = cleanup_main()

        if exit_code == 0:
            logger.info("cleanup_completed_successfully")
            return ("Cleanup", True, None)
        else:
            logger.error("cleanup_failed", exit_code=exit_code)
            return ("Cleanup", False, f"Exit code: {exit_code}")
    except Exception as e:
        logger.error("cleanup_exception", error=str(e))
        return ("Cleanup", False, str(e))


def run_main_sync():
    """Run main sync operation."""
    logger.info("starting_main_sync")

    try:
        # Import and run main sync
        # Save original sys.argv and clear it to avoid argument conflicts
        original_argv = sys.argv.copy()
        sys.argv = [sys.argv[0]]  # Keep only the script name

        try:
            from src.main import main as main_sync
            exit_code = main_sync()
        finally:
            # Restore original sys.argv even if the child sync raises.
            sys.argv = original_argv

        if exit_code == 0:
            logger.info("main_sync_completed_successfully")
            return ("Main Sync", True, None)
        else:
            logger.error("main_sync_failed", exit_code=exit_code)
            return ("Main Sync", False, f"Exit code: {exit_code}")
    except Exception as e:
        logger.error("main_sync_exception", error=str(e))
        return ("Main Sync", False, str(e))


def run_library_sync():
    """Run library sync operation."""
    logger.info("starting_library_sync")

    try:
        # Import and run library sync
        from sync_library_to_watchlist import main as library_main
        exit_code = library_main()

        if exit_code == 0:
            logger.info("library_sync_completed_successfully")
            return ("Library Sync", True, None)
        else:
            logger.error("library_sync_failed", exit_code=exit_code)
            return ("Library Sync", False, f"Exit code: {exit_code}")
    except Exception as e:
        logger.error("library_sync_exception", error=str(e))
        return ("Library Sync", False, str(e))


def run_operations_sequentially():
    """Run all sync operations in production-safe order."""
    results = {}
    operations = [
        ("cleanup", run_cleanup),
        ("main_sync", run_main_sync),
        ("library_sync", run_library_sync),
    ]

    for task_name, operation in operations:
        try:
            result = run_with_history(task_name, operation)
            results[task_name] = result
            logger.info(f"{task_name}_finished", success=result[1])
        except Exception as e:
            logger.error(f"{task_name}_crashed", error=str(e))
            results[task_name] = (task_name.replace("_", " ").title(), False, str(e))

    return results


def run_operations_parallel():
    """Run all sync operations concurrently for manual fast runs."""
    with ThreadPoolExecutor(max_workers=3) as executor:
        future_cleanup = executor.submit(run_with_history, "cleanup", run_cleanup)
        future_main = executor.submit(run_with_history, "main_sync", run_main_sync)
        future_library = executor.submit(run_with_history, "library_sync", run_library_sync)

        futures = {
            future_cleanup: "cleanup",
            future_main: "main_sync",
            future_library: "library_sync",
        }

        results = {}
        for future in as_completed(futures):
            task_name = futures[future]
            try:
                result = future.result()
                results[task_name] = result
                logger.info(f"{task_name}_finished", success=result[1])
            except Exception as e:
                logger.error(f"{task_name}_crashed", error=str(e))
                results[task_name] = (task_name.replace("_", " ").title(), False, str(e))

    return results


def main(argv=None):
    """Main entry point for the combined sync runner."""
    try:
        args = parse_args(argv)
        dry_run = args.dry_run or os.getenv("DRY_RUN", "false").lower() == "true"
        log_level = (args.log_level or os.getenv("LOG_LEVEL", "INFO")).upper()
        if args.quiet:
            log_level = "WARNING"
        os.environ["LOG_LEVEL"] = log_level
        setup_logging(log_level=log_level, log_format="human")

        if dry_run:
            os.environ["DRY_RUN"] = "true"

        print("\n" + "=" * 80)
        print(
            "  VOD FILTER - Complete Sync "
            f"({'PARALLEL' if args.parallel else 'SEQUENTIAL'} EXECUTION)"
        )
        print("=" * 80)
        print(f"  Mode: {'DRY RUN' if dry_run else 'PRODUCTION'}")
        print(f"  Started: {time.strftime('%Y-%m-%d %H:%M:%S')}")
        print(
            "  Running cleanup, main sync, and library sync "
            f"{'in parallel' if args.parallel else 'sequentially'}"
        )
        print("=" * 80 + "\n")

        start_time = time.time()

        if args.parallel:
            results = run_operations_parallel()
        else:
            results = run_operations_sequentially()

        # Summary
        elapsed_time = time.time() - start_time

        print("\n" + "=" * 80)
        print(f"  SYNC SUMMARY ({'Parallel' if args.parallel else 'Sequential'} Execution)")
        print("=" * 80)

        for key, (name, success, error) in results.items():
            status = "✓ SUCCESS" if success else "✗ FAILED"
            print(f"  {name:15} {status}")
            if error:
                print(f"                  Error: {error}")

        print("-" * 80)
        print(
            f"  Total Time: {elapsed_time:.1f} seconds "
            f"({'parallel' if args.parallel else 'sequential'} execution)"
        )
        print(f"  Completed: {time.strftime('%Y-%m-%d %H:%M:%S')}")
        print("=" * 80 + "\n")

        # Return exit code
        success_count = sum(1 for _, (_, success, _) in results.items() if success)

        if success_count == 3:
            logger.info("all_syncs_completed_successfully")
            return 0
        elif success_count > 0:
            logger.warning("some_syncs_failed", success=success_count, total=3)
            return 2
        else:
            logger.error("all_syncs_failed")
            return 1

    except KeyboardInterrupt:
        logger.warning("sync_interrupted_by_user")
        return 130
    except Exception as e:
        logger.error("unexpected_error", error=str(e))
        import traceback
        traceback.print_exc()
        return 1


if __name__ == "__main__":
    # Keep console open if double-clicked
    exit_code = main()

    if not sys.stdin.isatty():
        input("\nPress Enter to exit...")

    sys.exit(exit_code)
