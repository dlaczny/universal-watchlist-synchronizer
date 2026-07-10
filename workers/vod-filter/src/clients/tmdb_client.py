"""TMDB API client for movie metadata and VOD availability.

Uses tmdbv3api library with rate limiting and retry logic.
"""

from typing import Optional, List, Dict, Any, Tuple
import structlog
import httpx
from tmdbv3api import TMDb, Movie as TMDbMovie

from src.utils.retry import retry_on_api_error, retry_on_rate_limit

logger = structlog.get_logger(__name__)


class TMDBClient:
    """Client for TMDB API operations."""

    def __init__(self, api_key: str, region: str = "PL", language: str = "en-US"):
        """Initialize TMDB client.

        Args:
            api_key: TMDB API key
            region: ISO 3166-1 region code for VOD availability
            language: Language for metadata (default: en-US)
        """
        self.api_key = api_key
        self.region = region
        self.language = language

        # Initialize tmdbv3api
        self.tmdb = TMDb()
        self.tmdb.api_key = api_key
        self.tmdb.language = language
        self.movie_api = TMDbMovie()

        logger.info("tmdb_client_initialized", region=region, language=language)

    @retry_on_api_error(max_attempts=3)
    def search_movie(self, title: str, year: Optional[int] = None) -> Optional[Dict[str, Any]]:
        """Search for a movie by title and year.

        Args:
            title: Movie title
            year: Optional release year for better accuracy

        Returns:
            Dict with movie data (tmdb_id, title, year, imdb_id, poster_url) or None if not found
        """
        logger.debug("tmdb_search", title=title, year=year)

        try:
            # Search for movie (tmdbv3api library doesn't support year parameter directly)
            results = self.movie_api.search(title)

            if not results:
                logger.warning("tmdb_no_results", title=title, year=year)
                return None

            # If year is provided, filter results to match the year
            if year:
                matching_results = []
                for movie in results:
                    if hasattr(movie, "release_date") and movie.release_date:
                        try:
                            movie_year = int(movie.release_date[:4])
                            if movie_year == year:
                                matching_results.append(movie)
                        except (ValueError, TypeError):
                            continue

                # If we have year matches, use them; otherwise use all results
                if matching_results:
                    results = matching_results

            # Get the first result (best match)
            movie = results[0]

            # Extract year from release_date
            release_year = None
            if hasattr(movie, "release_date") and movie.release_date:
                try:
                    release_year = int(movie.release_date[:4])
                except (ValueError, TypeError):
                    pass

            # Get full details including IMDB ID
            details = self.get_movie_details(movie.id)

            result = {
                "tmdb_id": movie.id,
                "title": movie.title,
                "year": release_year or year,
                "imdb_id": details.get("imdb_id") if details else None,
                "poster_url": f"https://image.tmdb.org/t/p/w500{movie.poster_path}"
                if hasattr(movie, "poster_path") and movie.poster_path
                else None,
            }

            logger.info("tmdb_movie_found", title=title, tmdb_id=result["tmdb_id"])
            return result

        except Exception as e:
            logger.error("tmdb_search_error", title=title, year=year, error=str(e))
            raise

    @retry_on_api_error(max_attempts=3)
    def get_movie_details(self, tmdb_id: int) -> Optional[Dict[str, Any]]:
        """Get detailed movie information including IMDB ID.

        Args:
            tmdb_id: TMDB movie ID

        Returns:
            Dict with movie details or None if not found
        """
        logger.debug("tmdb_get_details", tmdb_id=tmdb_id)

        try:
            movie = self.movie_api.details(tmdb_id)

            if not movie:
                logger.warning("tmdb_movie_not_found", tmdb_id=tmdb_id)
                return None

            # Extract year from release_date
            release_year = None
            if hasattr(movie, "release_date") and movie.release_date:
                try:
                    release_year = int(movie.release_date[:4])
                except (ValueError, TypeError):
                    pass

            return {
                "tmdb_id": movie.id,
                "imdb_id": getattr(movie, "imdb_id", None),
                "title": movie.title,
                "year": release_year,
                "poster_url": f"https://image.tmdb.org/t/p/w500{movie.poster_path}"
                if hasattr(movie, "poster_path") and movie.poster_path
                else None,
                "runtime": getattr(movie, "runtime", None),
            }

        except Exception as e:
            logger.error("tmdb_details_error", tmdb_id=tmdb_id, error=str(e))
            raise

    @retry_on_rate_limit(max_attempts=5)
    def get_movie_provider_catalog(
        self,
        region: Optional[str] = None,
    ) -> List[Dict[str, Any]]:
        """Get the movie watch-provider catalog for a region.

        Args:
            region: ISO 3166-1 region code (uses client default if not provided)

        Returns:
            Provider dictionaries from TMDB filtered to the region.
        """
        region = region or self.region
        logger.debug("tmdb_get_movie_provider_catalog", region=region)

        try:
            response = httpx.get(
                "https://api.themoviedb.org/3/watch/providers/movie",
                params={
                    "api_key": self.api_key,
                    "watch_region": region,
                    "language": self.language,
                },
                timeout=30,
            )
            response.raise_for_status()
            providers = response.json().get("results", [])
            return [
                {
                    "provider_id": provider["provider_id"],
                    "provider_name": provider["provider_name"],
                    "display_priority": provider.get("display_priority"),
                    "region": region,
                }
                for provider in providers
            ]
        except Exception as e:
            logger.error("tmdb_provider_catalog_error", region=region, error=str(e))
            raise

    @retry_on_rate_limit(max_attempts=5)
    def get_watch_providers(
        self, tmdb_id: int, region: Optional[str] = None
    ) -> List[Dict[str, Any]]:
        """Get streaming availability for a movie in a specific region.

        Args:
            tmdb_id: TMDB movie ID
            region: ISO 3166-1 region code (uses client default if not provided)

        Returns:
            List of provider dictionaries with provider_id, provider_name, availability_type
        """
        region = region or self.region
        logger.debug("tmdb_get_watch_providers", tmdb_id=tmdb_id, region=region)

        try:
            # Use direct HTTP request instead of tmdbv3api library
            # The library's AsObj wrapper doesn't properly expose dictionary keys
            url = f"https://api.themoviedb.org/3/movie/{tmdb_id}/watch/providers"
            params = {"api_key": self.api_key}

            response = httpx.get(url, params=params, timeout=30)
            response.raise_for_status()
            data = response.json()

            if not data or "results" not in data:
                logger.debug("tmdb_no_providers", tmdb_id=tmdb_id, region=region)
                return []

            # Extract providers for the specified region
            results = data["results"]
            if region not in results:
                logger.debug("tmdb_no_regional_providers", tmdb_id=tmdb_id, region=region)
                return []

            regional_data = results[region]
            providers = []

            # Process each availability type
            for availability_type in ["flatrate", "rent", "buy", "ads"]:
                if availability_type in regional_data:
                    provider_list = regional_data[availability_type]

                    for provider in provider_list:
                        providers.append(
                            {
                                "provider_id": provider["provider_id"],
                                "provider_name": provider["provider_name"],
                                "availability_type": availability_type,
                                "region": region,
                            }
                        )

            logger.info(
                "tmdb_providers_found",
                tmdb_id=tmdb_id,
                region=region,
                providers_count=len(providers),
            )
            return providers

        except Exception as e:
            logger.error(
                "tmdb_watch_providers_error", tmdb_id=tmdb_id, region=region, error=str(e)
            )
            # Don't raise - return empty list so processing can continue
            return []

    def check_vod_availability(
        self, tmdb_id: int, configured_providers: List[int], region: Optional[str] = None
    ) -> Tuple[bool, List[Dict[str, Any]]]:
        """Check if a movie is available on any of the configured VOD providers.

        Args:
            tmdb_id: TMDB movie ID
            configured_providers: List of provider IDs to check against
            region: ISO 3166-1 region code (uses client default if not provided)

        Returns:
            Tuple of (is_available: bool, matching_providers: List[Dict])
        """
        all_providers = self.get_watch_providers(tmdb_id, region)

        # Filter to only "flatrate" (subscription streaming) providers
        streaming_providers = [p for p in all_providers if p["availability_type"] == "flatrate"]

        # Check if any match our configured providers
        matching_providers = [
            p for p in streaming_providers if p["provider_id"] in configured_providers
        ]

        is_available = len(matching_providers) > 0

        if is_available:
            logger.info(
                "vod_available",
                tmdb_id=tmdb_id,
                providers=[p["provider_name"] for p in matching_providers],
            )
        else:
            logger.debug("vod_not_available", tmdb_id=tmdb_id)

        return is_available, matching_providers


class TMDBError(Exception):
    """Raised when TMDB API operation fails."""

    pass
