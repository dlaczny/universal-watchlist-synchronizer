"""Plex API client for managing watchlist.

Uses PlexAPI library for Plex server interactions.
"""

from typing import Optional, List, Dict, Any
import unicodedata
import structlog
from plexapi.server import PlexServer
from plexapi.myplex import MyPlexAccount
from plexapi.exceptions import NotFound, BadRequest, Unauthorized

from src.utils.retry import (
    is_plex_rate_limit_error,
    retry_on_api_error,
    retry_on_plex_rate_limit,
)

logger = structlog.get_logger(__name__)


class PlexClient:
    """Client for Plex API operations."""

    def __init__(self, url: str, token: str):
        """Initialize Plex client.

        Args:
            url: Plex server URL (e.g., http://localhost:32400)
            token: Plex authentication token
        """
        self.url = url.rstrip("/")
        self.token = token

        # Initialize PlexAPI client
        try:
            self.server = PlexServer(url, token)
            # Get MyPlex account for watchlist operations
            self.account = MyPlexAccount(token=token)
            logger.info(
                "plex_client_initialized",
                url=url,
                server_name=self.server.friendlyName,
            )
        except Unauthorized as e:
            logger.error("plex_auth_failed", url=url, error=str(e))
            raise PlexError(f"Failed to authenticate with Plex server: {e}")
        except Exception as e:
            logger.error("plex_connection_failed", url=url, error=str(e))
            raise PlexError(f"Failed to connect to Plex server: {e}")

    @retry_on_plex_rate_limit(max_attempts=5, min_wait=15, max_wait=180, multiplier=2)
    def get_watchlist(self) -> List[Dict[str, Any]]:
        """Get all movies from the user's Plex watchlist.

        Returns:
            List of movie dictionaries with title, year, tmdb_id

        Raises:
            PlexError: If retrieval fails
        """
        logger.debug("plex_get_watchlist")

        try:
            # Get watchlist from MyPlex account
            watchlist = self.account.watchlist()

            movies = []
            for item in watchlist:
                # Filter only movie items
                if item.type != "movie":
                    continue

                # Extract TMDB ID from guid
                tmdb_id = self._extract_tmdb_id(item)
                if not tmdb_id:
                    logger.warning(
                        "plex_no_tmdb_id",
                        title=item.title,
                        year=getattr(item, "year", None),
                        guid=getattr(item, "guid", None),
                    )
                    continue

                movies.append(
                    {
                        "title": item.title,
                        "year": getattr(item, "year", None),
                        "tmdb_id": tmdb_id,
                    }
                )

            logger.info("plex_watchlist_retrieved", count=len(movies))
            return movies

        except Exception as e:
            logger.error("plex_get_watchlist_error", error=str(e))
            raise PlexError(f"Failed to get watchlist from Plex: {e}")

    def _search_plex_discovery(self, title: str, year: int, tmdb_id: int):
        """Search for a movie using PlexAPI's searchDiscover method.

        Args:
            title: Movie title
            year: Release year
            tmdb_id: Expected TMDB identity

        Returns:
            Movie object if found, None otherwise
        """
        try:
            for query in self._discovery_queries(title, year):
                results = self.account.searchDiscover(query=query, limit=50)

                for result in results:
                    if result.type != "movie":
                        continue

                    # Search text is only discovery; exact TMDB identity authorizes mutation.
                    if self._extract_tmdb_id(result) == tmdb_id:
                        logger.debug(
                            "plex_discovery_found",
                            title=title,
                            year=year,
                            plex_title=result.title,
                            plex_year=getattr(result, "year", None),
                            query=query,
                        )
                        return result

            logger.debug("plex_discovery_not_found", title=title, year=year)
            return None

        except Exception as e:
            if is_plex_rate_limit_error(e):
                raise
            logger.error("plex_discovery_error", title=title, year=year, error=str(e))
            raise PlexError(f"Plex discovery search failed: {e}") from e

    @staticmethod
    def _discovery_queries(title: str, year: int) -> list[str]:
        ascii_title = (
            unicodedata.normalize("NFKD", title)
            .encode("ascii", "ignore")
            .decode("ascii")
        )
        candidates = [title, ascii_title, f"{title} {year}", f"{ascii_title} {year}"]
        return [
            query
            for index, query in enumerate(candidates)
            if query and query not in candidates[:index]
        ]

    @retry_on_api_error(max_attempts=3)
    def add_to_watchlist(self, tmdb_id: int, title: str, year: int) -> bool:
        """Add a movie to Plex watchlist using PlexAPI's searchDiscover and addToWatchlist.

        Args:
            tmdb_id: TMDB movie ID
            title: Movie title
            year: Release year

        Returns:
            True if added or already present, False if exact identity is not found

        Raises:
            PlexError: If addition fails
        """
        logger.info("plex_add_to_watchlist", tmdb_id=tmdb_id, title=title, year=year)

        try:
            # Search for movie using PlexAPI's searchDiscover (searches entire Plex database)
            movie = self._search_plex_discovery(title, year, tmdb_id)

            if not movie:
                logger.warning(
                    "plex_movie_not_found",
                    tmdb_id=tmdb_id,
                    title=title,
                    year=year,
                )
                return False

            # Check if already in watchlist
            if hasattr(movie, 'onWatchlist') and movie.onWatchlist(self.account):
                logger.debug("plex_movie_already_in_watchlist", tmdb_id=tmdb_id, title=title)
                return True

            # Add to watchlist using PlexAPI's built-in method
            movie.addToWatchlist()
            logger.info("plex_movie_added_to_watchlist", tmdb_id=tmdb_id, title=title)
            return True

        except Exception as e:
            logger.error("plex_add_error", tmdb_id=tmdb_id, title=title, error=str(e))
            raise PlexError(f"Failed to add movie to Plex watchlist: {e}")

    @retry_on_api_error(max_attempts=3)
    def remove_from_watchlist(self, tmdb_id: int, title: str) -> bool:
        """Remove a movie from Plex watchlist.

        Args:
            tmdb_id: TMDB movie ID
            title: Movie title

        Returns:
            True if removed successfully, False if not in watchlist

        Raises:
            PlexError: If removal fails
        """
        logger.info("plex_remove_from_watchlist", tmdb_id=tmdb_id, title=title)

        try:
            # Get current watchlist
            watchlist = self.account.watchlist()

            # Find the movie in watchlist
            for item in watchlist:
                if item.type != "movie":
                    continue

                # Check if this is the movie we want to remove
                item_tmdb_id = self._extract_tmdb_id(item)
                if item_tmdb_id == tmdb_id:
                    item.removeFromWatchlist()
                    logger.info("plex_movie_removed_from_watchlist", tmdb_id=tmdb_id, title=title)
                    return True

            logger.debug("plex_movie_not_in_watchlist", tmdb_id=tmdb_id, title=title)
            return False

        except Exception as e:
            logger.error("plex_remove_error", tmdb_id=tmdb_id, title=title, error=str(e))
            raise PlexError(f"Failed to remove movie from Plex watchlist: {e}")

    @retry_on_api_error(max_attempts=3)
    def is_in_watchlist(self, tmdb_id: int, title: str) -> bool:
        """Check if a movie is in Plex watchlist.

        Args:
            tmdb_id: TMDB movie ID
            title: Movie title

        Returns:
            True if movie is in watchlist, False otherwise
        """
        try:
            watchlist = self.get_watchlist()

            for movie in watchlist:
                if movie.get("tmdb_id") == tmdb_id or movie.get("title") == title:
                    return True

            return False

        except PlexError as e:
            logger.error("plex_watchlist_check_error", tmdb_id=tmdb_id, error=str(e))
            return False

    @retry_on_api_error(max_attempts=3)
    def get_library_movies(self, library_name: str = "Movies") -> List[Dict[str, Any]]:
        """Get all movies from Plex library.

        Args:
            library_name: Name of the Plex library section (default: "Movies")

        Returns:
            List of movie dictionaries with title, year, tmdb_id

        Raises:
            PlexError: If retrieval fails
        """
        logger.debug("plex_get_library_movies", library_name=library_name)

        try:
            # Get the Movies library section
            movies_section = self.server.library.section(library_name)

            # Get all movies from the library
            all_movies = movies_section.all()

            movies = []
            for item in all_movies:
                # Extract TMDB ID
                tmdb_id = self._extract_tmdb_id(item)
                if not tmdb_id:
                    logger.debug(
                        "plex_library_movie_no_tmdb_id",
                        title=item.title,
                        year=getattr(item, "year", None),
                    )
                    continue

                movies.append(
                    {
                        "title": item.title,
                        "year": getattr(item, "year", None),
                        "tmdb_id": tmdb_id,
                    }
                )

            logger.info("plex_library_movies_retrieved", count=len(movies))
            return movies

        except NotFound:
            logger.error("plex_library_not_found", library_name=library_name)
            raise PlexError(f"Plex library '{library_name}' not found")
        except Exception as e:
            logger.error("plex_get_library_error", library_name=library_name, error=str(e))
            raise PlexError(f"Failed to get movies from Plex library: {e}")

    @retry_on_api_error(max_attempts=3)
    def delete_from_library(self, tmdb_id: int, title: str, library_name: str = "Filmy") -> bool:
        """Delete a movie from Plex library.

        Args:
            tmdb_id: TMDB movie ID
            title: Movie title (for logging)
            library_name: Name of the Plex library section

        Returns:
            True if deleted successfully, False if not found

        Raises:
            PlexError: If deletion fails
        """
        logger.info("plex_delete_from_library", tmdb_id=tmdb_id, title=title, library=library_name)

        try:
            # Get the Movies library section
            movies_section = self.server.library.section(library_name)

            # Get all movies and find the one to delete
            all_movies = movies_section.all()

            for item in all_movies:
                # Check if this is the movie we want to delete
                item_tmdb_id = self._extract_tmdb_id(item)
                if item_tmdb_id == tmdb_id or (item.title == title and getattr(item, "year", None)):
                    # Delete the movie
                    item.delete()
                    logger.info("plex_movie_deleted", tmdb_id=tmdb_id, title=title)
                    return True

            logger.debug("plex_movie_not_found_in_library", tmdb_id=tmdb_id, title=title)
            return False

        except NotFound:
            logger.error("plex_library_not_found", library_name=library_name)
            raise PlexError(f"Plex library '{library_name}' not found")
        except Exception as e:
            logger.error("plex_delete_error", tmdb_id=tmdb_id, title=title, error=str(e))
            raise PlexError(f"Failed to delete movie from Plex library: {e}")

    def _extract_tmdb_id(self, item) -> Optional[int]:
        """Extract TMDB ID from Plex item guid.

        Args:
            item: Plex media item

        Returns:
            TMDB ID if found, None otherwise
        """
        try:
            # Plex guid format: plex://movie/5d776b7696b655001fdc6b4f
            # Or: tmdb://12345
            # Or: imdb://tt1234567

            if not hasattr(item, "guids"):
                return None

            for guid in item.guids:
                guid_str = str(guid.id) if hasattr(guid, "id") else str(guid)

                # Check for TMDB guid
                if "tmdb://" in guid_str:
                    tmdb_id_str = guid_str.split("tmdb://")[1].split("?")[0]
                    return int(tmdb_id_str)

            return None

        except Exception as e:
            logger.debug("plex_tmdb_extraction_failed", error=str(e))
            return None


class PlexError(Exception):
    """Raised when Plex operation fails."""

    pass
