"""TMDB service for resolving movie IDs and checking VOD availability.

Orchestrates TMDB client and cache operations with intelligent caching.
"""

from typing import Optional, List, Dict, Any, Tuple
import structlog

from src.clients.tmdb_client import TMDBClient
from src.services.cache_service import CacheService
from src.models.movie import Movie
from src.models.vod_availability import VODAvailability

logger = structlog.get_logger(__name__)


class TMDBService:
    """Service for TMDB operations with caching."""

    def __init__(
        self,
        api_key: str,
        region: str,
        cache_service: CacheService,
        cache_ttl_hours: int = 48,
    ):
        """Initialize TMDB service.

        Args:
            api_key: TMDB API key
            region: ISO 3166-1 region code
            cache_service: Cache service instance
            cache_ttl_hours: Cache TTL in hours for VOD availability
        """
        self.cache_service = cache_service
        self.cache_ttl_hours = cache_ttl_hours
        self.client = TMDBClient(api_key, region)
        logger.info("tmdb_service_initialized", region=region, cache_ttl_hours=cache_ttl_hours)

    def resolve_movie(self, title: str, year: int, letterboxd_id: Optional[str] = None) -> Optional[Movie]:
        """Resolve movie to TMDB ID and fetch metadata.

        Args:
            title: Movie title
            year: Release year
            letterboxd_id: Optional Letterboxd film slug

        Returns:
            Movie instance or None if not found
        """
        logger.debug("resolving_movie", title=title, year=year)

        try:
            # Search TMDB for movie
            result = self.client.search_movie(title, year)

            if not result:
                logger.warning("movie_not_found_in_tmdb", title=title, year=year)
                return None

            # Create Movie instance
            movie = Movie(
                tmdb_id=result["tmdb_id"],
                title=result["title"],
                year=result["year"],
                imdb_id=result.get("imdb_id"),
                letterboxd_id=letterboxd_id,
                poster_url=result.get("poster_url"),
            )

            # Cache movie metadata
            self.cache_service.upsert_movie(
                tmdb_id=movie.tmdb_id,
                title=movie.title,
                year=movie.year,
                imdb_id=movie.imdb_id,
                letterboxd_id=movie.letterboxd_id,
                poster_url=movie.poster_url,
            )

            logger.info("movie_resolved", title=title, tmdb_id=movie.tmdb_id)
            return movie

        except Exception as e:
            logger.error("movie_resolution_error", title=title, year=year, error=str(e))
            return None

    def check_vod_availability(
        self,
        tmdb_id: int,
        configured_providers: Optional[List[int]] = None,
        force_refresh: bool = False,
    ) -> Tuple[bool, List[VODAvailability]]:
        """Check if movie is available on configured VOD providers.

        Uses cache unless force_refresh is True.

        Args:
            tmdb_id: TMDB movie ID
            configured_providers: List of provider IDs to check (if None, uses all enabled from DB)
            force_refresh: If True, bypass cache and re-check

        Returns:
            Tuple of (is_available: bool, vod_records: List[VODAvailability])
        """
        logger.debug("checking_vod_availability", tmdb_id=tmdb_id, force_refresh=force_refresh)

        # If no providers specified, get enabled providers from cache service
        if configured_providers is None:
            enabled_providers = self.cache_service.get_enabled_providers(self.client.region)
            configured_providers = [p["provider_id"] for p in enabled_providers]
            logger.debug(
                "using_enabled_providers",
                provider_ids=configured_providers,
                count=len(configured_providers),
            )

        # Check cache first unless force refresh
        if not force_refresh:
            cached_vod = self.cache_service.get_vod_availability(tmdb_id, self.cache_ttl_hours)
            if cached_vod:
                logger.debug("vod_cache_hit", tmdb_id=tmdb_id)

                # Convert to VODAvailability instances
                vod_records = []
                for record in cached_vod:
                    vod_records.append(VODAvailability.from_dict(record))

                # Check if any match configured providers
                matching = [v for v in vod_records if v.provider_id in configured_providers]
                is_available = len(matching) > 0

                return is_available, vod_records

        # Cache miss or force refresh - fetch from TMDB
        logger.debug("vod_cache_miss", tmdb_id=tmdb_id)

        try:
            # Clear old cache entries
            if force_refresh:
                self.cache_service.clear_vod_availability(tmdb_id)

            # Fetch providers once, then derive both availability and cache
            # records from that same response. Calling
            # client.check_vod_availability() here would fetch providers and
            # then this service used to fetch them again for caching.
            all_providers = self.client.get_watch_providers(tmdb_id)
            streaming_providers = [
                provider
                for provider in all_providers
                if provider["availability_type"] == "flatrate"
            ]
            matching_providers = [
                provider
                for provider in streaming_providers
                if provider["provider_id"] in configured_providers
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

            vod_records = []

            for provider_data in streaming_providers:
                self.cache_service.upsert_vod_availability(
                    tmdb_id=tmdb_id,
                    provider_id=provider_data["provider_id"],
                    provider_name=provider_data["provider_name"],
                    region=provider_data["region"],
                    availability_type=provider_data["availability_type"],
                )

                vod_records.append(
                    VODAvailability(
                        tmdb_id=tmdb_id,
                        provider_id=provider_data["provider_id"],
                        provider_name=provider_data["provider_name"],
                        region=provider_data["region"],
                        availability_type=provider_data["availability_type"],
                    )
                )

            logger.info(
                "vod_availability_checked",
                tmdb_id=tmdb_id,
                is_available=is_available,
                providers_count=len(vod_records),
            )

            return is_available, vod_records

        except Exception as e:
            logger.error("vod_check_error", tmdb_id=tmdb_id, error=str(e))
            # Return unavailable on error to be safe
            return False, []

    def batch_resolve_movies(
        self, movies: List[Dict[str, Any]]
    ) -> List[Optional[Movie]]:
        """Resolve multiple movies to TMDB IDs.

        Args:
            movies: List of movie dicts with title, year, letterboxd_id

        Returns:
            List of Movie instances (None for unresolved movies)
        """
        logger.info("batch_resolving_movies", count=len(movies))

        resolved = []
        for movie_data in movies:
            movie = self.resolve_movie(
                title=movie_data["title"],
                year=movie_data["year"],
                letterboxd_id=movie_data.get("letterboxd_id"),
            )
            resolved.append(movie)

        resolved_count = sum(1 for m in resolved if m is not None)
        logger.info(
            "batch_resolution_complete",
            total=len(movies),
            resolved=resolved_count,
            failed=len(movies) - resolved_count,
        )

        return resolved


class TMDBServiceError(Exception):
    """Raised when TMDB service operation fails."""

    pass
