"""Cleanup movies removed from Letterboxd watchlist.

This script removes movies from Plex library and watchlist when they're
removed from Letterboxd watchlist.

Designed to run every 60 minutes or on app start.
"""

import os
import sys
from pathlib import Path
from dataclasses import dataclass
from datetime import datetime

# Add src to path
sys.path.insert(0, str(Path(__file__).parent))

from dotenv import load_dotenv
import structlog

# Load environment variables
load_dotenv()

from src.clients.plex_client import PlexClient
from src.clients.letterboxd_client import LetterboxdClient
from src.clients.radarr_client import RadarrClient
from src.services.cache_service import CacheService
from src.services.tmdb_service import TMDBService
from src.utils.logging import setup_logging

logger = structlog.get_logger(__name__)


class CleanupSafetyError(Exception):
    """Raised when cleanup cannot safely determine current Letterboxd state."""


def destructive_cleanup_confirmed(argv: list[str] | None = None) -> bool:
    """Return whether production destructive cleanup has explicit confirmation."""
    args = argv if argv is not None else sys.argv[1:]
    if "--confirm-destructive-cleanup" in args:
        return True
    return os.getenv("CONFIRM_DESTRUCTIVE_CLEANUP", "false").lower() == "true"


@dataclass(frozen=True)
class CleanupReportMovie:
    """Movie entry included in a cleanup report."""

    tmdb_id: int
    title: str
    year: int | None = None


class CleanupReport:
    """Collect and write cleanup actions grouped by destination."""

    SECTION_TITLES = {
        "delete_from_library": "Delete from Plex library",
        "remove_from_watchlist": "Remove from Plex watchlist",
        "remove_from_radarr": "Remove from Radarr",
        "missing_from_cache": "Skipped because missing from cache",
        "errors": "Errors",
    }

    def __init__(self, dry_run: bool):
        self.dry_run = dry_run
        self.actions: dict[str, list[CleanupReportMovie]] = {
            key: [] for key in self.SECTION_TITLES
        }

    def add(self, action: str, movie: CleanupReportMovie) -> None:
        """Add a movie to a report action bucket."""
        self.actions.setdefault(action, []).append(movie)

    def write(self, path: Path) -> Path:
        """Write a Markdown cleanup report and return the output path."""
        path.parent.mkdir(parents=True, exist_ok=True)

        title = (
            "# VOD Filter cleanup dry-run report"
            if self.dry_run
            else "# VOD Filter cleanup report"
        )
        lines = [
            title,
            "",
            f"Generated: {datetime.now().isoformat(timespec='seconds')}",
            f"Mode: {'dry-run' if self.dry_run else 'production'}",
            "",
        ]

        for action, section_title in self.SECTION_TITLES.items():
            movies = self.actions.get(action, [])
            lines.append(f"## {section_title} ({len(movies)})")
            lines.append("")
            if movies:
                for movie in sorted(movies, key=lambda item: item.title.lower()):
                    year = f" ({movie.year})" if movie.year else ""
                    tmdb_url = f"https://www.themoviedb.org/movie/{movie.tmdb_id}"
                    lines.append(
                        f"- {movie.title}{year} "
                        f"[TMDB {movie.tmdb_id}]({tmdb_url})"
                    )
            else:
                lines.append("- None")
            lines.append("")

        path.write_text("\n".join(lines), encoding="utf-8")
        return path


def _extract_tmdb_id(movie) -> int | None:
    """Extract a TMDB ID from a dict-like or object-like movie value."""
    if isinstance(movie, dict):
        tmdb_id = movie.get("tmdb_id")
    else:
        tmdb_id = getattr(movie, "tmdb_id", None)

    try:
        return int(tmdb_id) if tmdb_id is not None else None
    except (TypeError, ValueError):
        return None


def resolve_current_letterboxd_ids(letterboxd_watchlist, tmdb_service) -> set[int]:
    """Resolve current Letterboxd watchlist entries to TMDB IDs.

    Raw Letterboxd scraper entries contain title/year/slug, not TMDB IDs. Cleanup
    must resolve them before comparing against sync_state.tmdb_id; otherwise a
    non-empty watchlist can look like {None} and every cached movie appears
    removed.
    """
    current_ids: set[int] = set()
    unresolved_entries = []

    for movie in letterboxd_watchlist:
        tmdb_id = _extract_tmdb_id(movie)
        if tmdb_id is not None:
            current_ids.add(tmdb_id)
        else:
            unresolved_entries.append(movie)

    if unresolved_entries:
        resolved_movies = tmdb_service.batch_resolve_movies(unresolved_entries)
        for movie in resolved_movies:
            tmdb_id = _extract_tmdb_id(movie)
            if tmdb_id is not None:
                current_ids.add(tmdb_id)

    if letterboxd_watchlist and not current_ids:
        raise CleanupSafetyError(
            "Fetched a non-empty Letterboxd watchlist but could not resolve any TMDB IDs; "
            "aborting cleanup to avoid treating every cached movie as removed."
        )

    return current_ids


def detect_removed_movie_states(previous_letterboxd, current_letterboxd_ids: set[int]):
    """Return previous sync-state rows missing from the current Letterboxd ID set."""
    removed_movies = []
    for row in previous_letterboxd:
        tmdb_id = row["tmdb_id"]
        if tmdb_id not in current_letterboxd_ids:
            removed_movies.append(dict(row))
    return removed_movies


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
        letterboxd_username = os.getenv("LETTERBOXD_USERNAME")
        plex_url = os.getenv("PLEX_URL")
        plex_token = os.getenv("PLEX_TOKEN")
        plex_library_name = os.getenv("PLEX_LIBRARY_NAME", "Filmy")
        tmdb_api_key = os.getenv("TMDB_API_KEY")
        tmdb_region = os.getenv("TMDB_REGION", os.getenv("VOD_REGION", "PL")).upper()
        radarr_url = os.getenv("RADARR_URL")
        radarr_api_key = os.getenv("RADARR_API_KEY")
        database_path = os.getenv("DATABASE_PATH", "data/vod-filter.db")
        dry_run = os.getenv("DRY_RUN", "false").lower() == "true"
        delete_files_on_removal = (
            os.getenv("RADARR_DELETE_FILES_ON_REMOVAL", "true").lower() == "true"
        )

        # Check for --dry-run flag
        if "--dry-run" in sys.argv:
            dry_run = True

        report_path_arg = None
        if "--report-file" in sys.argv:
            idx = sys.argv.index("--report-file")
            if idx + 1 < len(sys.argv):
                report_path_arg = sys.argv[idx + 1]

        report_path = (
            Path(report_path_arg)
            if report_path_arg
            else Path("data/reports")
            / f"cleanup-{'dry-run' if dry_run else 'run'}-{datetime.now().strftime('%Y%m%d-%H%M%S')}.md"
        )

        if (
            not letterboxd_username
            or not plex_url
            or not plex_token
            or not tmdb_api_key
            or not radarr_url
            or not radarr_api_key
        ):
            logger.error(
                "missing_config",
                error=(
                    "LETTERBOXD_USERNAME, PLEX_URL, PLEX_TOKEN, TMDB_API_KEY, "
                    "RADARR_URL, and RADARR_API_KEY are required"
                ),
            )
            return 1

        logger.info(
            "starting_cleanup",
            letterboxd_username=letterboxd_username,
            plex_url=plex_url,
            library_name=plex_library_name,
            dry_run=dry_run,
            delete_files_on_removal=delete_files_on_removal,
        )

        # Initialize clients
        letterboxd_client = LetterboxdClient(username=letterboxd_username)
        plex_client = PlexClient(url=plex_url, token=plex_token)
        radarr_client = RadarrClient(url=radarr_url, api_key=radarr_api_key)
        cache_service = CacheService(database_path=database_path)
        tmdb_service = TMDBService(
            api_key=tmdb_api_key,
            region=tmdb_region,
            cache_service=cache_service,
        )

        # Get current Letterboxd watchlist
        logger.info("fetching_letterboxd_watchlist")
        letterboxd_watchlist = letterboxd_client.fetch_watchlist()
        letterboxd_ids = resolve_current_letterboxd_ids(letterboxd_watchlist, tmdb_service)

        logger.info(
            "letterboxd_watchlist_fetched",
            raw_count=len(letterboxd_watchlist),
            resolved_tmdb_count=len(letterboxd_ids),
        )

        # Get movies from sync_state that were on Letterboxd before
        with cache_service.get_connection() as conn:
            cursor = conn.execute(
                """
                SELECT tmdb_id, on_letterboxd, on_plex, on_radarr
                FROM sync_state
                WHERE on_letterboxd = 1
                """
            )
            previous_letterboxd = list(cursor.fetchall())

        logger.info("previous_letterboxd_state_loaded", count=len(previous_letterboxd))

        # Find movies removed from Letterboxd
        removed_movies = detect_removed_movie_states(previous_letterboxd, letterboxd_ids)

        logger.info("removed_movies_detected", count=len(removed_movies))

        if removed_movies and not dry_run and not destructive_cleanup_confirmed():
            logger.error(
                "destructive_cleanup_confirmation_required",
                removed_count=len(removed_movies),
                message=(
                    "Refusing production cleanup with removals unless "
                    "--confirm-destructive-cleanup or CONFIRM_DESTRUCTIVE_CLEANUP=true is set."
                ),
            )
            return 3

        stats = {
            "deleted_from_library": 0,
            "removed_from_watchlist": 0,
            "removed_from_radarr": 0,
            "errors": 0,
        }
        report = CleanupReport(dry_run=dry_run)

        # Process each removed movie
        for movie_state in removed_movies:
            tmdb_id = movie_state["tmdb_id"]

            # Get movie details from cache
            movie_data = cache_service.get_movie(tmdb_id)
            if not movie_data:
                logger.warning("movie_not_found_in_cache", tmdb_id=tmdb_id)
                report.add(
                    "missing_from_cache",
                    CleanupReportMovie(tmdb_id=tmdb_id, title="Unknown", year=None),
                )
                continue

            title = movie_data.title
            year = movie_data.year
            report_movie = CleanupReportMovie(tmdb_id=tmdb_id, title=title, year=year)

            logger.info(
                "processing_removed_movie",
                tmdb_id=tmdb_id,
                title=title,
                on_plex=movie_state["on_plex"],
                on_radarr=movie_state["on_radarr"],
            )

            # Step 1: Delete from Plex library (if present)
            if movie_state["on_plex"]:
                if dry_run:
                    logger.info(
                        "dry_run_would_delete_from_library",
                        tmdb_id=tmdb_id,
                        title=title,
                    )
                    report.add("delete_from_library", report_movie)
                    stats["deleted_from_library"] += 1
                else:
                    try:
                        deleted = plex_client.delete_from_library(
                            tmdb_id=tmdb_id,
                            title=title,
                            library_name=plex_library_name,
                        )
                        if deleted:
                            stats["deleted_from_library"] += 1
                            report.add("delete_from_library", report_movie)
                            logger.info(
                                "deleted_from_library",
                                tmdb_id=tmdb_id,
                                title=title,
                            )
                    except Exception as e:
                        logger.error(
                            "failed_to_delete_from_library",
                            tmdb_id=tmdb_id,
                            title=title,
                            error=str(e),
                        )
                        report.add("errors", report_movie)
                        stats["errors"] += 1

            # Step 2: Remove from Plex watchlist (if present)
            if movie_state["on_plex"]:
                if dry_run:
                    logger.info(
                        "dry_run_would_remove_from_watchlist",
                        tmdb_id=tmdb_id,
                        title=title,
                    )
                    report.add("remove_from_watchlist", report_movie)
                    stats["removed_from_watchlist"] += 1
                else:
                    try:
                        removed = plex_client.remove_from_watchlist(
                            tmdb_id=tmdb_id,
                            title=title,
                        )
                        if removed:
                            stats["removed_from_watchlist"] += 1
                            report.add("remove_from_watchlist", report_movie)
                            logger.info(
                                "removed_from_watchlist",
                                tmdb_id=tmdb_id,
                                title=title,
                            )
                    except Exception as e:
                        logger.error(
                            "failed_to_remove_from_watchlist",
                            tmdb_id=tmdb_id,
                            title=title,
                            error=str(e),
                        )
                        report.add("errors", report_movie)
                        stats["errors"] += 1

            # Step 3: Remove from Radarr (if present)
            if movie_state["on_radarr"]:
                if dry_run:
                    logger.info(
                        "dry_run_would_remove_from_radarr",
                        tmdb_id=tmdb_id,
                        title=title,
                    )
                    report.add("remove_from_radarr", report_movie)
                    stats["removed_from_radarr"] += 1
                else:
                    try:
                        removed = radarr_client.remove_movie(
                            tmdb_id=tmdb_id,
                            delete_files=delete_files_on_removal,
                        )
                        if removed:
                            stats["removed_from_radarr"] += 1
                            report.add("remove_from_radarr", report_movie)
                            logger.info(
                                "removed_from_radarr",
                                tmdb_id=tmdb_id,
                                title=title,
                            )
                    except Exception as e:
                        logger.error(
                            "failed_to_remove_from_radarr",
                            tmdb_id=tmdb_id,
                            title=title,
                            error=str(e),
                        )
                        report.add("errors", report_movie)
                        stats["errors"] += 1

            # Step 4: Update sync state to mark as removed from Letterboxd
            if not dry_run:
                cache_service.update_sync_state(
                    tmdb_id=tmdb_id,
                    on_letterboxd=False,
                    on_plex=False,  # Removed from Plex
                    on_radarr=False,  # Removed from Radarr
                )

        logger.info(
            "cleanup_complete",
            deleted_from_library=stats["deleted_from_library"],
            removed_from_watchlist=stats["removed_from_watchlist"],
            removed_from_radarr=stats["removed_from_radarr"],
            errors=stats["errors"],
            dry_run=dry_run,
        )
        written_report = report.write(report_path)
        logger.info("cleanup_report_written", path=str(written_report))
        print(f"Cleanup report written: {written_report}")

        # Return success or error
        if stats["errors"] > 0:
            return 2  # Errors occurred
        else:
            return 0  # Success

    except KeyboardInterrupt:
        logger.warning("cleanup_interrupted")
        return 130
    except Exception as e:
        logger.error("cleanup_failed", error=str(e))
        import traceback
        traceback.print_exc()
        return 1


if __name__ == "__main__":
    sys.exit(main())
