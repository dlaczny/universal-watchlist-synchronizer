"""Plex service for watchlist synchronization.

Handles bidirectional sync between Letterboxd and Plex watchlists.
"""

from typing import List, Dict, Any, Set, Optional
import structlog

from src.clients.plex_client import PlexClient, PlexError
from src.models.movie import Movie
from src.services.cache_service import CacheService

logger = structlog.get_logger(__name__)


class PlexService:
    """Service for Plex watchlist operations."""

    def __init__(self, plex_client: PlexClient, cache_service: CacheService, cache_ttl_minutes: int = 30):
        """Initialize Plex service.

        Args:
            plex_client: Plex API client
            cache_service: Cache service for sync state tracking
            cache_ttl_minutes: Cache TTL for Plex watchlist in minutes (0 to disable)
        """
        self.client = plex_client
        self.cache = cache_service
        self.cache_ttl_minutes = cache_ttl_minutes

        logger.info("plex_service_initialized", cache_ttl_minutes=cache_ttl_minutes)

    def get_plex_watchlist(self, use_cache: bool = True) -> List[Dict[str, Any]]:
        """Get all movies from Plex watchlist with optional caching.

        Args:
            use_cache: If True, use cached data if available and fresh

        Returns:
            List of movie dictionaries with title, year, tmdb_id

        Raises:
            PlexError: If retrieval fails
        """
        # Check if cache is enabled and should be used
        if use_cache and self.cache_ttl_minutes > 0:
            # Check if cache is fresh
            if self.cache.is_cache_valid("plex_watchlist", self.cache_ttl_minutes):
                cached_watchlist = self.cache.get_plex_watchlist_cache()
                if cached_watchlist:
                    logger.info(
                        "plex_watchlist_from_cache",
                        count=len(cached_watchlist),
                        ttl_minutes=self.cache_ttl_minutes,
                    )
                    return cached_watchlist

        # Cache miss or disabled - fetch from Plex API
        logger.info("fetching_plex_watchlist_from_api")

        try:
            watchlist = self.client.get_watchlist()
            logger.info("plex_watchlist_fetched_from_api", count=len(watchlist))

            # Update cache if caching is enabled
            if self.cache_ttl_minutes > 0:
                self.cache.refresh_plex_watchlist_cache(watchlist)
                logger.debug("plex_watchlist_cache_updated")

            return watchlist

        except PlexError as e:
            logger.error("plex_watchlist_fetch_failed", error=str(e))
            raise

    def add_to_watchlist(self, movie: Movie) -> bool:
        """Add a movie to Plex watchlist.

        Args:
            movie: Movie to add

        Returns:
            True if added successfully, False otherwise
        """
        try:
            # Check if already in watchlist
            if self.client.is_in_watchlist(movie.tmdb_id, movie.title):
                logger.debug(
                    "plex_movie_already_in_watchlist",
                    tmdb_id=movie.tmdb_id,
                    title=movie.title,
                )
                return False

            # Add to Plex watchlist
            result = self.client.add_to_watchlist(
                tmdb_id=movie.tmdb_id,
                title=movie.title,
                year=movie.year,
            )

            if result:
                # Update sync state
                self.cache.update_sync_state(
                    tmdb_id=movie.tmdb_id,
                    on_plex=True,
                )
                logger.info(
                    "plex_movie_added",
                    tmdb_id=movie.tmdb_id,
                    title=movie.title,
                )

            return result

        except PlexError as e:
            logger.error(
                "plex_add_failed",
                tmdb_id=movie.tmdb_id,
                title=movie.title,
                error=str(e),
            )
            return False

    def remove_from_watchlist(self, movie: Movie) -> bool:
        """Remove a movie from Plex watchlist.

        Args:
            movie: Movie to remove

        Returns:
            True if removed successfully, False otherwise
        """
        try:
            # Remove from Plex watchlist
            result = self.client.remove_from_watchlist(
                tmdb_id=movie.tmdb_id,
                title=movie.title,
            )

            if result:
                # Update sync state
                self.cache.update_sync_state(
                    tmdb_id=movie.tmdb_id,
                    on_plex=False,
                )
                logger.info(
                    "plex_movie_removed",
                    tmdb_id=movie.tmdb_id,
                    title=movie.title,
                )

            return result

        except PlexError as e:
            logger.error(
                "plex_remove_failed",
                tmdb_id=movie.tmdb_id,
                title=movie.title,
                error=str(e),
            )
            return False

    def detect_removals(
        self, current_letterboxd: List[Movie], previous_state: List[Dict[str, Any]]
    ) -> List[Movie]:
        """Detect movies that were removed from Letterboxd watchlist.

        Args:
            current_letterboxd: Current Letterboxd watchlist
            previous_state: Previous sync state from cache

        Returns:
            List of movies that should be removed from Plex
        """
        logger.debug("detecting_letterboxd_removals")

        # Create set of current TMDB IDs
        current_ids = {movie.tmdb_id for movie in current_letterboxd}

        # Find movies that were on Letterboxd before but not now
        removed_movies = []
        for prev_movie in previous_state:
            if prev_movie.get("on_letterboxd") and prev_movie["tmdb_id"] not in current_ids:
                # This movie was removed from Letterboxd
                # Get the movie details from cache
                cached_movie = self.cache.get_movie(prev_movie["tmdb_id"])
                if cached_movie:
                    removed_movies.append(cached_movie)
                    logger.debug(
                        "letterboxd_removal_detected",
                        tmdb_id=prev_movie["tmdb_id"],
                        title=cached_movie.title,
                    )

        logger.info("removals_detected", count=len(removed_movies))
        return removed_movies

    def sync_to_plex(
        self,
        letterboxd_movies: List[Movie],
        vod_status: Dict[int, bool] = None,
        dry_run: bool = False,
    ) -> Dict[str, int]:
        """Synchronize Letterboxd watchlist to Plex (bidirectional).

        This performs full bidirectional sync:
        - Adds movies from Letterboxd that are AVAILABLE on VOD to Plex immediately
        - Movies NOT available on VOD are skipped (will be added after Radarr download)
        - Removes movies from Plex that are NOT on Letterboxd

        Args:
            letterboxd_movies: Movies from Letterboxd watchlist
            vod_status: Dict mapping tmdb_id to VOD availability (True if available)
            dry_run: If True, preview changes without applying

        Returns:
            Dictionary with sync statistics (added, removed, skipped)
        """
        logger.info(
            "syncing_to_plex",
            letterboxd_count=len(letterboxd_movies),
            dry_run=dry_run,
        )

        stats = {
            "added": 0,
            "removed": 0,
            "skipped": 0,
            "skipped_unavailable": 0,
            "errors": 0,
        }

        # Create set of Letterboxd TMDB IDs for quick lookup
        letterboxd_tmdb_ids = {movie.tmdb_id for movie in letterboxd_movies}
        logger.debug(
            "letterboxd_movie_ids",
            count=len(letterboxd_tmdb_ids),
        )

        # Get current Plex watchlist
        try:
            plex_watchlist = self.get_plex_watchlist()
            logger.info(
                "plex_current_watchlist_fetched",
                count=len(plex_watchlist),
            )
        except PlexError as e:
            logger.error("plex_watchlist_fetch_failed_during_sync", error=str(e))
            plex_watchlist = []

        plex_watchlist_ids = {
            movie.get("tmdb_id")
            for movie in plex_watchlist
            if movie.get("tmdb_id")
        }
        plex_watchlist_titles = {
            movie.get("title")
            for movie in plex_watchlist
            if movie.get("title")
        }

        # Find movies on Plex that are NOT on Letterboxd
        movies_to_remove = []
        for plex_movie in plex_watchlist:
            plex_tmdb_id = plex_movie.get("tmdb_id")
            if plex_tmdb_id and plex_tmdb_id not in letterboxd_tmdb_ids:
                # This movie is on Plex but NOT on Letterboxd - remove it
                movie = Movie(
                    tmdb_id=plex_tmdb_id,
                    title=plex_movie.get("title", "Unknown"),
                    year=plex_movie.get("year", 0),
                )
                movies_to_remove.append(movie)
                logger.debug(
                    "plex_movie_not_on_letterboxd",
                    tmdb_id=plex_tmdb_id,
                    title=plex_movie.get("title"),
                )

        logger.info(
            "movies_to_remove_from_plex",
            count=len(movies_to_remove),
        )

        # Remove movies from Plex that are NOT on Letterboxd
        for movie in movies_to_remove:
            if dry_run:
                logger.info(
                    "dry_run_would_remove_from_plex",
                    tmdb_id=movie.tmdb_id,
                    title=movie.title,
                    reason="not_on_letterboxd",
                )
                stats["removed"] += 1
            else:
                if self.remove_from_watchlist(movie):
                    stats["removed"] += 1
                    # Update sync state to mark as not on Letterboxd
                    self.cache.update_sync_state(
                        tmdb_id=movie.tmdb_id,
                        on_letterboxd=False,
                        on_plex=False,
                    )
                else:
                    stats["errors"] += 1

        # Add movies from Letterboxd to Plex (ONLY VOD-available ones)
        for movie in letterboxd_movies:
            # Update sync state to mark as on Letterboxd
            self.cache.update_sync_state(
                tmdb_id=movie.tmdb_id,
                on_letterboxd=True,
            )

            # Check if movie is available on VOD
            is_vod_available = vod_status.get(movie.tmdb_id, False) if vod_status else True

            if not is_vod_available:
                # Skip movies not on VOD - they will be added after Radarr downloads them
                logger.info(
                    "plex_skip_unavailable_movie",
                    tmdb_id=movie.tmdb_id,
                    title=movie.title,
                    reason="not_on_vod_will_add_after_download",
                )
                stats["skipped_unavailable"] += 1
                continue

            if dry_run:
                # Check if would be added
                if (
                    movie.tmdb_id not in plex_watchlist_ids
                    and movie.title not in plex_watchlist_titles
                ):
                    logger.info(
                        "dry_run_would_add_to_plex",
                        tmdb_id=movie.tmdb_id,
                        title=movie.title,
                    )
                    stats["added"] += 1
                else:
                    stats["skipped"] += 1
            else:
                if (
                    movie.tmdb_id in plex_watchlist_ids
                    or movie.title in plex_watchlist_titles
                ):
                    stats["skipped"] += 1
                elif self.add_to_watchlist(movie):
                    stats["added"] += 1
                    plex_watchlist_ids.add(movie.tmdb_id)
                    plex_watchlist_titles.add(movie.title)
                else:
                    stats["errors"] += 1

        logger.info(
            "plex_sync_complete",
            added=stats["added"],
            removed=stats["removed"],
            skipped=stats["skipped"],
            skipped_unavailable=stats["skipped_unavailable"],
            errors=stats["errors"],
            dry_run=dry_run,
        )

        return stats

    def sync_downloaded_radarr_movies(
        self,
        radarr_client,
        dry_run: bool = False
    ) -> Dict[str, int]:
        """Sync downloaded movies from Radarr to Plex watchlist.

        This checks all movies in Radarr that have been downloaded (hasFile=True)
        and adds them to the Plex watchlist so they appear in Plex.

        OPTIMIZATION: Caches the Plex watchlist once at the start to avoid rate limiting
        when checking many movies (avoids making 170+ API calls).

        Args:
            radarr_client: RadarrClient instance
            dry_run: If True, only log what would be done

        Returns:
            Dict with counts: added, skipped, errors
        """
        logger.info("syncing_downloaded_radarr_movies", dry_run=dry_run)

        stats = {
            "added": 0,
            "skipped": 0,
            "errors": 0,
        }

        try:
            # Get all downloaded movies from Radarr
            downloaded_movies = radarr_client.get_downloaded_movies()

            logger.info(
                "downloaded_movies_found",
                count=len(downloaded_movies),
            )

            # OPTIMIZATION: Fetch Plex watchlist ONCE and cache it
            # This prevents making an API call for each movie (rate limiting issue)
            logger.info("fetching_plex_watchlist_for_cache")
            try:
                plex_watchlist = self.client.get_watchlist()
                # Create a set of TMDB IDs for fast O(1) lookup
                plex_watchlist_ids = {
                    movie.get("tmdb_id")
                    for movie in plex_watchlist
                    if movie.get("tmdb_id")
                }

                logger.info(
                    "plex_watchlist_cached",
                    plex_count=len(plex_watchlist_ids),
                    radarr_count=len(downloaded_movies),
                )

            except Exception as e:
                logger.error("failed_to_fetch_plex_watchlist", error=str(e))
                plex_watchlist_ids = set()

            # Count comparison logging
            already_in_watchlist = 0
            to_be_added = 0

            for movie in downloaded_movies:
                tmdb_id = movie.get("tmdb_id")
                title = movie.get("title")
                year = movie.get("year")

                if not tmdb_id or not title or not year:
                    logger.warning(
                        "incomplete_movie_data",
                        movie=movie,
                    )
                    continue

                # Check if already in Plex watchlist using cached set (fast O(1) lookup)
                if tmdb_id in plex_watchlist_ids:
                    stats["skipped"] += 1
                    already_in_watchlist += 1
                    continue

                to_be_added += 1

                # Add to Plex watchlist
                if dry_run:
                    logger.info(
                        "dry_run_would_add_downloaded",
                        tmdb_id=tmdb_id,
                        title=title,
                        year=year,
                    )
                    stats["added"] += 1
                else:
                    try:
                        added = self.client.add_to_watchlist(tmdb_id, title, year)
                        if added:
                            stats["added"] += 1
                            # Update cached set so subsequent checks don't re-add
                            plex_watchlist_ids.add(tmdb_id)
                            logger.info(
                                "downloaded_movie_added_to_plex",
                                tmdb_id=tmdb_id,
                                title=title,
                            )
                        else:
                            stats["skipped"] += 1
                    except Exception as e:
                        logger.error(
                            "failed_to_add_downloaded_movie",
                            tmdb_id=tmdb_id,
                            title=title,
                            error=str(e),
                        )
                        stats["errors"] += 1

            # Count comparison summary
            logger.info(
                "radarr_plex_sync_complete",
                radarr_downloaded=len(downloaded_movies),
                already_in_watchlist=already_in_watchlist,
                to_be_added=to_be_added,
                added=stats["added"],
                skipped=stats["skipped"],
                errors=stats["errors"],
                dry_run=dry_run,
            )

        except Exception as e:
            logger.error("radarr_plex_sync_error", error=str(e))
            stats["errors"] += 1

        return stats

    def sync_library_to_watchlist(
        self,
        library_name: str = "Movies",
        dry_run: bool = False
    ) -> Dict[str, int]:
        """Sync movies from Plex library to Plex watchlist.

        Ensures all movies you have in your Plex library are also in your
        Plex watchlist for tracking purposes.

        OPTIMIZATION: Caches the Plex watchlist once at the start to avoid
        rate limiting when checking many movies.

        Args:
            library_name: Name of the Plex library section (default: "Movies")
            dry_run: If True, only log what would be done

        Returns:
            Dict with counts: added, skipped, errors
        """
        logger.info("syncing_library_to_watchlist", library_name=library_name, dry_run=dry_run)

        stats = {
            "added": 0,
            "skipped": 0,
            "errors": 0,
        }

        try:
            # Get all movies from Plex library
            library_movies = self.client.get_library_movies(library_name)

            logger.info(
                "library_movies_found",
                count=len(library_movies),
                library=library_name,
            )

            # OPTIMIZATION: Fetch Plex watchlist ONCE and cache it
            # This prevents making an API call for each movie (rate limiting issue)
            logger.info("fetching_plex_watchlist_for_cache")
            try:
                plex_watchlist = self.client.get_watchlist()
                # Create a set of TMDB IDs for fast O(1) lookup
                plex_watchlist_ids = {
                    movie.get("tmdb_id")
                    for movie in plex_watchlist
                    if movie.get("tmdb_id")
                }

                logger.info(
                    "plex_watchlist_cached",
                    watchlist_count=len(plex_watchlist_ids),
                    library_count=len(library_movies),
                )

            except Exception as e:
                logger.error("failed_to_fetch_plex_watchlist", error=str(e))
                plex_watchlist_ids = set()

            # Count comparison logging
            already_in_watchlist = 0
            to_be_added = 0

            for movie in library_movies:
                tmdb_id = movie.get("tmdb_id")
                title = movie.get("title")
                year = movie.get("year")

                if not tmdb_id or not title or not year:
                    logger.warning(
                        "incomplete_movie_data",
                        movie=movie,
                    )
                    continue

                # Check if already in Plex watchlist using cached set (fast O(1) lookup)
                if tmdb_id in plex_watchlist_ids:
                    stats["skipped"] += 1
                    already_in_watchlist += 1
                    continue

                to_be_added += 1

                # Add to Plex watchlist
                if dry_run:
                    logger.info(
                        "dry_run_would_add_library_movie",
                        tmdb_id=tmdb_id,
                        title=title,
                        year=year,
                    )
                    stats["added"] += 1
                else:
                    try:
                        added = self.client.add_to_watchlist(tmdb_id, title, year)
                        if added:
                            stats["added"] += 1
                            # Update cached set so subsequent checks don't re-add
                            plex_watchlist_ids.add(tmdb_id)
                            logger.info(
                                "library_movie_added_to_watchlist",
                                tmdb_id=tmdb_id,
                                title=title,
                            )
                        else:
                            stats["skipped"] += 1
                    except Exception as e:
                        logger.error(
                            "failed_to_add_library_movie",
                            tmdb_id=tmdb_id,
                            title=title,
                            error=str(e),
                        )
                        stats["errors"] += 1

            # Count comparison summary
            logger.info(
                "library_watchlist_sync_complete",
                library_movies=len(library_movies),
                already_in_watchlist=already_in_watchlist,
                to_be_added=to_be_added,
                added=stats["added"],
                skipped=stats["skipped"],
                errors=stats["errors"],
                dry_run=dry_run,
            )

        except Exception as e:
            logger.error("library_watchlist_sync_error", error=str(e))
            stats["errors"] += 1

        return stats
