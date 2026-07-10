"""Letterboxd service for fetching and processing watchlist.

Orchestrates Letterboxd client and cache operations.
"""

from typing import List, Dict, Any
import structlog

from src.clients.letterboxd_client import LetterboxdClient
from src.services.cache_service import CacheService
from src.models.movie import Movie

logger = structlog.get_logger(__name__)


class LetterboxdService:
    """Service for managing Letterboxd watchlist operations."""

    def __init__(self, username: str, cache_service: CacheService):
        """Initialize Letterboxd service.

        Args:
            username: Letterboxd username
            cache_service: Cache service instance
        """
        self.username = username
        self.cache_service = cache_service
        self.client = LetterboxdClient(username)
        logger.info("letterboxd_service_initialized", username=username)

    def fetch_watchlist(self) -> List[Dict[str, Any]]:
        """Fetch complete watchlist from Letterboxd.

        Returns:
            List of movie dictionaries with title, year, letterboxd_id

        Raises:
            Exception: If fetching fails
        """
        logger.info("fetching_watchlist", username=self.username)

        try:
            movies = self.client.fetch_watchlist()
            logger.info("watchlist_fetched", username=self.username, movies_count=len(movies))
            return movies

        except Exception as e:
            logger.error("watchlist_fetch_failed", username=self.username, error=str(e))
            raise

    def get_watchlist_with_metadata(self) -> List[Dict[str, Any]]:
        """Fetch watchlist and return with any cached metadata.

        Returns:
            List of movie dictionaries enriched with cached TMDB data if available
        """
        watchlist = self.fetch_watchlist()
        enriched_movies = []

        for movie_data in watchlist:
            # Try to find cached movie by title+year
            # Note: This is a best-effort lookup since we don't have TMDB ID yet
            enriched_movies.append(movie_data)

        return enriched_movies

    def validate_username(self) -> bool:
        """Validate that the Letterboxd username is accessible.

        Returns:
            True if username is valid and watchlist accessible
        """
        logger.info("validating_username", username=self.username)

        try:
            is_valid = self.client.validate_username()
            if is_valid:
                logger.info("username_valid", username=self.username)
            else:
                logger.warning("username_invalid", username=self.username)
            return is_valid

        except Exception as e:
            logger.error("username_validation_error", username=self.username, error=str(e))
            return False


class LetterboxdServiceError(Exception):
    """Raised when Letterboxd service operation fails."""

    pass
