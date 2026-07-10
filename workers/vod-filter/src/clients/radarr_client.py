"""Radarr API client for managing movie imports.

Uses pyarr library for Radarr API interactions.
"""

from typing import Optional, List, Dict, Any
import structlog
from pyarr import RadarrAPI
from pyarr.exceptions import PyarrError

from src.utils.retry import retry_on_api_error

logger = structlog.get_logger(__name__)


class RadarrClient:
    """Client for Radarr API operations."""

    def __init__(
        self,
        url: str,
        api_key: str,
        root_folder: str = "/movies",
        quality_profile_id: int = 1,
    ):
        """Initialize Radarr client.

        Args:
            url: Radarr server URL (e.g., http://localhost:7878)
            api_key: Radarr API key
            root_folder: Root folder path for movies
            quality_profile_id: Quality profile ID to use
        """
        self.url = url.rstrip("/")
        self.api_key = api_key
        self.root_folder = root_folder
        self.quality_profile_id = quality_profile_id

        # Initialize pyarr client
        self.client = RadarrAPI(url, api_key)

        # Cache for exclusions (fetched once per session)
        self._exclusions_cache = None

        logger.info(
            "radarr_client_initialized",
            url=url,
            root_folder=root_folder,
            quality_profile=quality_profile_id,
        )

    @retry_on_api_error(max_attempts=3)
    def get_movie_by_tmdb_id(self, tmdb_id: int) -> Optional[Dict[str, Any]]:
        """Check if a movie exists in Radarr by TMDB ID.

        Args:
            tmdb_id: TMDB movie ID

        Returns:
            Movie dict if found, None otherwise
        """
        logger.debug("radarr_lookup", tmdb_id=tmdb_id)

        try:
            # Look up movie by TMDB ID
            results = self.client.lookup_movie(term=f"tmdb:{tmdb_id}")

            if results and len(results) > 0:
                movie = results[0]
                logger.debug("radarr_movie_found", tmdb_id=tmdb_id, title=movie.get("title"))
                return movie

            logger.debug("radarr_movie_not_found", tmdb_id=tmdb_id)
            return None

        except PyarrError as e:
            logger.error("radarr_lookup_error", tmdb_id=tmdb_id, error=str(e))
            raise

    @retry_on_api_error(max_attempts=3)
    def is_movie_in_radarr(self, tmdb_id: int) -> bool:
        """Check if a movie is already in Radarr.

        Args:
            tmdb_id: TMDB movie ID

        Returns:
            True if movie exists in Radarr, False otherwise
        """
        try:
            # Get all movies and check if any match the TMDB ID
            movies = self.client.get_movie()

            for movie in movies:
                if movie.get("tmdbId") == tmdb_id:
                    logger.debug("radarr_movie_exists", tmdb_id=tmdb_id, title=movie.get("title"))
                    return True

            return False

        except PyarrError as e:
            logger.error("radarr_check_error", tmdb_id=tmdb_id, error=str(e))
            return False

    @retry_on_api_error(max_attempts=3)
    def get_exclusions(self) -> List[Dict[str, Any]]:
        """Get all import list exclusions (cached per session).

        Returns:
            List of exclusion dictionaries

        Raises:
            RadarrError: If retrieval fails
        """
        # Return cached exclusions if available
        if self._exclusions_cache is not None:
            return self._exclusions_cache

        logger.debug("radarr_get_exclusions")

        try:
            # Use the correct Radarr API endpoint for exclusions
            import httpx
            response = httpx.get(
                f"{self.url}/api/v3/exclusions",
                headers={"X-Api-Key": self.api_key},
                timeout=30,
            )
            response.raise_for_status()
            exclusions = response.json()

            # Cache the result
            self._exclusions_cache = exclusions

            logger.info("radarr_exclusions_retrieved", count=len(exclusions))
            return exclusions

        except Exception as e:
            logger.error("radarr_get_exclusions_error", error=str(e))
            raise RadarrError(f"Failed to get exclusions from Radarr: {e}")

    @retry_on_api_error(max_attempts=3)
    def is_movie_excluded(self, tmdb_id: int) -> bool:
        """Check if a movie is in the import list exclusions.

        Args:
            tmdb_id: TMDB movie ID

        Returns:
            True if movie is excluded, False otherwise
        """
        try:
            exclusions = self.get_exclusions()

            for exclusion in exclusions:
                if exclusion.get("tmdbId") == tmdb_id:
                    logger.debug("radarr_movie_excluded", tmdb_id=tmdb_id, title=exclusion.get("movieTitle"))
                    return True

            return False

        except RadarrError as e:
            logger.error("radarr_exclusion_check_error", tmdb_id=tmdb_id, error=str(e))
            return False

    @retry_on_api_error(max_attempts=3)
    def add_movie(
        self,
        tmdb_id: int,
        title: str,
        year: int,
        monitored: bool = True,
        search_for_movie: bool = True,
    ) -> Optional[Dict[str, Any]]:
        """Add a movie to Radarr.

        Args:
            tmdb_id: TMDB movie ID
            title: Movie title
            year: Release year
            monitored: Whether to monitor the movie for releases
            search_for_movie: Whether to search for the movie immediately

        Returns:
            Added movie dict or None if already exists or excluded

        Raises:
            RadarrError: If addition fails
        """
        logger.info("radarr_add_movie", tmdb_id=tmdb_id, title=title, year=year)

        try:
            # Check if movie is in exclusion list
            if self.is_movie_excluded(tmdb_id):
                logger.info("radarr_movie_excluded", tmdb_id=tmdb_id, title=title)
                return None

            # Check if movie already exists
            if self.is_movie_in_radarr(tmdb_id):
                logger.info("radarr_movie_already_exists", tmdb_id=tmdb_id, title=title)
                return None

            # Look up movie details from TMDB via Radarr
            lookup_results = self.client.lookup_movie(term=f"tmdb:{tmdb_id}")

            if not lookup_results or len(lookup_results) == 0:
                logger.error("radarr_lookup_failed", tmdb_id=tmdb_id, title=title)
                raise RadarrError(f"Could not find movie in Radarr lookup: {title} ({year})")

            movie_data = lookup_results[0]

            # Prepare movie data for addition
            add_options = {
                "searchForMovie": search_for_movie,
            }

            movie_data.update(
                {
                    "rootFolderPath": self.root_folder,
                    "qualityProfileId": self.quality_profile_id,
                    "monitored": monitored,
                    "addOptions": add_options,
                }
            )

            # Add movie using the correct pyarr API signature
            result = self.client.add_movie(
                movie_data,
                root_dir=self.root_folder,
                quality_profile_id=self.quality_profile_id,
            )

            logger.info(
                "radarr_movie_added",
                tmdb_id=tmdb_id,
                title=title,
                radarr_id=result.get("id"),
            )
            return result

        except PyarrError as e:
            logger.error("radarr_add_error", tmdb_id=tmdb_id, title=title, error=str(e))
            raise RadarrError(f"Failed to add movie to Radarr: {e}")

    @retry_on_api_error(max_attempts=3)
    def remove_movie(self, tmdb_id: int, delete_files: bool = False) -> bool:
        """Remove a movie from Radarr.

        Args:
            tmdb_id: TMDB movie ID
            delete_files: Whether to delete movie files

        Returns:
            True if removed successfully, False if not found

        Raises:
            RadarrError: If removal fails
        """
        logger.info("radarr_remove_movie", tmdb_id=tmdb_id, delete_files=delete_files)

        try:
            # Get all movies and find the one with matching TMDB ID
            movies = self.client.get_movie()

            for movie in movies:
                if movie.get("tmdbId") == tmdb_id:
                    radarr_id = movie.get("id")
                    self.client.del_movie(radarr_id, delete_files=delete_files)
                    logger.info("radarr_movie_removed", tmdb_id=tmdb_id, radarr_id=radarr_id)
                    return True

            logger.warning("radarr_movie_not_found_for_removal", tmdb_id=tmdb_id)
            return False

        except PyarrError as e:
            logger.error("radarr_remove_error", tmdb_id=tmdb_id, error=str(e))
            raise RadarrError(f"Failed to remove movie from Radarr: {e}")

    @retry_on_api_error(max_attempts=3)
    def get_all_movies(self) -> List[Dict[str, Any]]:
        """Get all movies from Radarr.

        Returns:
            List of movie dictionaries

        Raises:
            RadarrError: If retrieval fails
        """
        logger.debug("radarr_get_all_movies")

        try:
            movies = self.client.get_movie()
            logger.info("radarr_movies_retrieved", count=len(movies))
            return movies

        except PyarrError as e:
            logger.error("radarr_get_all_error", error=str(e))
            raise RadarrError(f"Failed to get movies from Radarr: {e}")

    @retry_on_api_error(max_attempts=3)
    def get_downloaded_movies(self) -> List[Dict[str, Any]]:
        """Get all movies that have been downloaded (hasFile=True).

        Returns:
            List of downloaded movie dictionaries with tmdb_id, title, year

        Raises:
            RadarrError: If retrieval fails
        """
        logger.debug("radarr_get_downloaded_movies")

        try:
            all_movies = self.client.get_movie()
            downloaded = [
                {
                    "tmdb_id": movie.get("tmdbId"),
                    "title": movie.get("title"),
                    "year": movie.get("year"),
                    "radarr_id": movie.get("id"),
                    "has_file": movie.get("hasFile", False),
                }
                for movie in all_movies
                if movie.get("hasFile", False)
            ]

            logger.info("radarr_downloaded_movies_retrieved", count=len(downloaded))
            return downloaded

        except PyarrError as e:
            logger.error("radarr_get_downloaded_error", error=str(e))
            raise RadarrError(f"Failed to get downloaded movies from Radarr: {e}")


class RadarrError(Exception):
    """Raised when Radarr operation fails."""

    pass
