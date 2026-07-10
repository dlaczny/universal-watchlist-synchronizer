"""Radarr service for managing movie import operations.

Orchestrates Radarr client and cache operations.
"""

from typing import List, Dict, Any
import structlog

from src.clients.radarr_client import RadarrClient, RadarrError
from src.services.cache_service import CacheService
from src.models.movie import Movie
from src.models.sync_state import SyncState

logger = structlog.get_logger(__name__)


class RadarrService:
    """Service for Radarr operations with sync state tracking."""

    def __init__(
        self,
        url: str,
        api_key: str,
        root_folder: str,
        quality_profile_id: int,
        cache_service: CacheService,
    ):
        """Initialize Radarr service.

        Args:
            url: Radarr server URL
            api_key: Radarr API key
            root_folder: Root folder path for movies
            quality_profile_id: Quality profile ID
            cache_service: Cache service instance
        """
        self.cache_service = cache_service
        self.client = RadarrClient(url, api_key, root_folder, quality_profile_id)
        logger.info("radarr_service_initialized", url=url)

    def add_movie_if_needed(
        self, movie: Movie, vod_available: bool, dry_run: bool = False
    ) -> bool:
        """Add movie to Radarr if it's not available on VOD and not already in Radarr.

        Args:
            movie: Movie instance
            vod_available: Whether movie is available on VOD
            dry_run: If True, only log what would be done

        Returns:
            True if movie was added (or would be added in dry-run), False otherwise
        """
        # Don't add if VOD available
        if vod_available:
            logger.debug(
                "movie_skipped_vod_available",
                tmdb_id=movie.tmdb_id,
                title=movie.title,
            )
            return False

        # Check if already in Radarr
        try:
            exists = self.client.is_movie_in_radarr(movie.tmdb_id)

            if exists:
                logger.debug(
                    "movie_already_in_radarr",
                    tmdb_id=movie.tmdb_id,
                    title=movie.title,
                )

                # Update sync state
                self._update_sync_state(movie.tmdb_id, on_radarr=True, vod_available=vod_available)
                return False

        except Exception as e:
            logger.error(
                "radarr_check_failed",
                tmdb_id=movie.tmdb_id,
                title=movie.title,
                error=str(e),
            )
            return False

        # Add to Radarr
        if dry_run:
            logger.info(
                "dry_run_would_add_to_radarr",
                tmdb_id=movie.tmdb_id,
                title=movie.title,
                year=movie.year,
            )
            return True

        try:
            result = self.client.add_movie(
                tmdb_id=movie.tmdb_id,
                title=movie.title,
                year=movie.year,
                monitored=True,
                search_for_movie=True,
            )

            if result:
                logger.info(
                    "movie_added_to_radarr",
                    tmdb_id=movie.tmdb_id,
                    title=movie.title,
                    radarr_id=result.get("id"),
                )

                # Update sync state
                self._update_sync_state(movie.tmdb_id, on_radarr=True, vod_available=vod_available)
                return True
            else:
                # Movie already existed
                self._update_sync_state(movie.tmdb_id, on_radarr=True, vod_available=vod_available)
                return False

        except RadarrError as e:
            logger.error(
                "movie_add_failed",
                tmdb_id=movie.tmdb_id,
                title=movie.title,
                error=str(e),
            )

            # Update sync state with error
            self._update_sync_state(
                movie.tmdb_id,
                on_radarr=False,
                vod_available=vod_available,
                sync_error=f"Radarr add failed: {str(e)[:200]}",
            )
            return False

    def remove_movie_if_needed(
        self, tmdb_id: int, title: str, delete_files: bool = False, dry_run: bool = False
    ) -> bool:
        """Remove movie from Radarr if it exists.

        Args:
            tmdb_id: TMDB movie ID
            title: Movie title (for logging)
            delete_files: Whether to delete downloaded files
            dry_run: If True, only log what would be done

        Returns:
            True if movie was removed (or would be removed in dry-run), False otherwise
        """
        # Check if in Radarr
        try:
            exists = self.client.is_movie_in_radarr(tmdb_id)

            if not exists:
                logger.debug(
                    "movie_not_in_radarr_skip_removal",
                    tmdb_id=tmdb_id,
                    title=title,
                )
                return False

        except Exception as e:
            logger.error(
                "radarr_check_failed_for_removal",
                tmdb_id=tmdb_id,
                title=title,
                error=str(e),
            )
            return False

        # Remove from Radarr
        if dry_run:
            logger.info(
                "dry_run_would_remove_from_radarr",
                tmdb_id=tmdb_id,
                title=title,
                delete_files=delete_files,
            )
            return True

        try:
            result = self.client.remove_movie(tmdb_id=tmdb_id, delete_files=delete_files)

            if result:
                logger.info(
                    "movie_removed_from_radarr",
                    tmdb_id=tmdb_id,
                    title=title,
                    delete_files=delete_files,
                )

                # Update sync state
                self._update_sync_state(tmdb_id, on_radarr=False, vod_available=False)
                return True
            else:
                logger.warning(
                    "movie_removal_failed_not_found",
                    tmdb_id=tmdb_id,
                    title=title,
                )
                return False

        except RadarrError as e:
            logger.error(
                "movie_removal_failed",
                tmdb_id=tmdb_id,
                title=title,
                error=str(e),
            )
            return False

    def sync_movies(
        self, movies: List[Movie], vod_status: Dict[int, bool], dry_run: bool = False
    ) -> Dict[str, int]:
        """Sync a list of movies to Radarr based on VOD availability.

        Args:
            movies: List of Movie instances
            vod_status: Dict mapping tmdb_id to vod_available bool
            dry_run: If True, only log what would be done

        Returns:
            Dict with counts: added, skipped_vod, skipped_exists, failed
        """
        logger.info("syncing_movies_to_radarr", count=len(movies), dry_run=dry_run)

        stats = {
            "added": 0,
            "skipped_vod": 0,
            "skipped_exists": 0,
            "failed": 0,
        }

        for movie in movies:
            vod_available = vod_status.get(movie.tmdb_id, False)

            if vod_available:
                stats["skipped_vod"] += 1
                continue

            try:
                # Check if exists
                exists = self.client.is_movie_in_radarr(movie.tmdb_id)

                if exists:
                    stats["skipped_exists"] += 1
                    self._update_sync_state(movie.tmdb_id, on_radarr=True, vod_available=vod_available)
                    continue

                # Add movie
                if dry_run:
                    logger.info(
                        "dry_run_would_add",
                        tmdb_id=movie.tmdb_id,
                        title=movie.title,
                    )
                    stats["added"] += 1
                else:
                    added = self.add_movie_if_needed(movie, vod_available, dry_run=False)
                    if added:
                        stats["added"] += 1

            except Exception as e:
                logger.error(
                    "movie_sync_error",
                    tmdb_id=movie.tmdb_id,
                    title=movie.title,
                    error=str(e),
                )
                stats["failed"] += 1

        logger.info("radarr_sync_complete", stats=stats)
        return stats

    def detect_removals(
        self, current_letterboxd_movies: List[Movie]
    ) -> List[Dict[str, Any]]:
        """Detect movies that should be removed from Radarr.

        Compares current Letterboxd watchlist with movies in Radarr.
        Returns movies that are in Radarr but NOT in current Letterboxd watchlist.

        Args:
            current_letterboxd_movies: Current Letterboxd watchlist movies

        Returns:
            List of dicts with tmdb_id, title to remove from Radarr
        """
        logger.info("detecting_radarr_removals")

        # Get current TMDB IDs from Letterboxd
        current_tmdb_ids = {movie.tmdb_id for movie in current_letterboxd_movies}

        # Get all movies from Radarr
        try:
            radarr_movies = self.client.get_all_movies()
        except Exception as e:
            logger.error("failed_to_get_radarr_movies", error=str(e))
            return []

        # Find movies in Radarr but not in Letterboxd
        to_remove = []
        for radarr_movie in radarr_movies:
            tmdb_id = radarr_movie.get("tmdbId")
            if tmdb_id and tmdb_id not in current_tmdb_ids:
                to_remove.append({
                    "tmdb_id": tmdb_id,
                    "title": radarr_movie.get("title", "Unknown"),
                })

        logger.info("radarr_removals_detected", count=len(to_remove))
        return to_remove

    def detect_vod_available_movies(self, vod_check_callback) -> List[Dict[str, Any]]:
        """Detect downloaded movies in Radarr that are now available on VOD.

        This allows automatic cleanup of movies that were downloaded but later
        became available on streaming services.

        Args:
            vod_check_callback: Callable that takes (tmdb_id, force_refresh) and returns
                               tuple of (is_available: bool, vod_records: List)

        Returns:
            List of dicts with tmdb_id, title for movies now available on VOD
        """
        logger.info("detecting_vod_available_movies_in_radarr")

        # Get downloaded movies from Radarr
        try:
            downloaded_movies = self.client.get_downloaded_movies()
            logger.info("radarr_downloaded_movies_found", count=len(downloaded_movies))
        except Exception as e:
            logger.error("failed_to_get_downloaded_movies", error=str(e))
            return []

        # Check VOD availability for each downloaded movie
        vod_available = []
        for movie in downloaded_movies:
            tmdb_id = movie.get("tmdb_id")
            if not tmdb_id:
                continue

            try:
                # Force refresh VOD check to get latest availability
                is_available, vod_records = vod_check_callback(tmdb_id, force_refresh=True)

                if is_available:
                    logger.info(
                        "downloaded_movie_now_on_vod",
                        tmdb_id=tmdb_id,
                        title=movie.get("title"),
                        providers_count=len(vod_records),
                    )
                    vod_available.append({
                        "tmdb_id": tmdb_id,
                        "title": movie.get("title", "Unknown"),
                    })

            except Exception as e:
                logger.error(
                    "vod_check_failed_for_movie",
                    tmdb_id=tmdb_id,
                    title=movie.get("title"),
                    error=str(e),
                )
                continue

        logger.info("vod_available_movies_detected", count=len(vod_available))
        return vod_available

    def sync_removals(
        self, movies_to_remove: List[Dict[str, Any]], delete_files: bool = False, dry_run: bool = False
    ) -> Dict[str, int]:
        """Remove movies from Radarr that are no longer in Letterboxd.

        Args:
            movies_to_remove: List of dicts with tmdb_id, title
            delete_files: Whether to delete downloaded files
            dry_run: If True, only log what would be done

        Returns:
            Dict with counts: removed, skipped, failed
        """
        logger.info(
            "syncing_radarr_removals",
            count=len(movies_to_remove),
            delete_files=delete_files,
            dry_run=dry_run,
        )

        stats = {
            "removed": 0,
            "skipped": 0,
            "failed": 0,
        }

        for movie in movies_to_remove:
            tmdb_id = movie["tmdb_id"]
            title = movie["title"]

            try:
                removed = self.remove_movie_if_needed(
                    tmdb_id=tmdb_id,
                    title=title,
                    delete_files=delete_files,
                    dry_run=dry_run,
                )

                if removed:
                    stats["removed"] += 1
                else:
                    stats["skipped"] += 1

            except Exception as e:
                logger.error(
                    "movie_removal_error",
                    tmdb_id=tmdb_id,
                    title=title,
                    error=str(e),
                )
                stats["failed"] += 1

        logger.info("radarr_removal_sync_complete", stats=stats)
        return stats

    def _update_sync_state(
        self,
        tmdb_id: int,
        on_radarr: bool,
        vod_available: bool,
        sync_error: str = None,
    ) -> None:
        """Update sync state in cache.

        Args:
            tmdb_id: TMDB movie ID
            on_radarr: Whether movie is in Radarr
            vod_available: Whether movie is available on VOD
            sync_error: Optional error message
        """
        # Get existing sync state if any
        existing = self.cache_service.get_sync_state(tmdb_id)

        self.cache_service.upsert_sync_state(
            tmdb_id=tmdb_id,
            on_letterboxd=existing.get("on_letterboxd", True) if existing else True,
            on_plex=existing.get("on_plex", False) if existing else False,
            on_radarr=on_radarr,
            vod_available=vod_available,
            sync_error=sync_error,
        )


class RadarrServiceError(Exception):
    """Raised when Radarr service operation fails."""

    pass
