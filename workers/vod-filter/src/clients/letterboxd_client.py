"""Letterboxd client for scraping watchlist data.

Uses letterboxdpy library with BeautifulSoup fallback.
"""

from typing import List, Dict, Any, Optional
import httpx
from bs4 import BeautifulSoup
import structlog

from src.utils.retry import retry_on_api_error

logger = structlog.get_logger(__name__)


class LetterboxdClient:
    """Client for scraping Letterboxd watchlist."""

    BASE_URL = "https://letterboxd.com"

    def __init__(self, username: str, timeout: int = 30):
        """Initialize Letterboxd client.

        Args:
            username: Letterboxd username
            timeout: Request timeout in seconds
        """
        self.username = username
        self.timeout = timeout
        # Add User-Agent header to get full HTML content
        headers = {
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        }
        self.client = httpx.Client(timeout=timeout, headers=headers)
        logger.info("letterboxd_client_initialized", username=username)

    def __del__(self):
        """Cleanup HTTP client."""
        if hasattr(self, "client"):
            self.client.close()

    @retry_on_api_error(max_attempts=3)
    def fetch_watchlist(self) -> List[Dict[str, Any]]:
        """Fetch complete watchlist for the configured user.

        Returns:
            List of movie dictionaries with title, year, letterboxd_id

        Raises:
            httpx.HTTPError: If request fails
            LetterboxdParseError: If HTML parsing fails
        """
        logger.info("fetching_letterboxd_watchlist", username=self.username)

        # Use BeautifulSoup scraper directly
        movies = self._fetch_with_beautifulsoup()
        logger.info(
            "letterboxd_watchlist_fetched",
            username=self.username,
            movies_count=len(movies),
            method="BeautifulSoup",
        )
        return movies

    def _fetch_with_beautifulsoup(self) -> List[Dict[str, Any]]:
        """Fetch watchlist using BeautifulSoup scraping.

        Returns:
            List of movie dictionaries

        Raises:
            httpx.HTTPError: If request fails
            LetterboxdParseError: If parsing fails
        """
        all_movies = []
        page = 1

        while True:
            movies = self._scrape_watchlist_page(page)

            if not movies:
                # Empty page means we've reached the end
                break

            all_movies.extend(movies)
            logger.debug(
                "letterboxd_page_scraped",
                username=self.username,
                page=page,
                movies_count=len(movies),
            )
            page += 1

        return all_movies

    @retry_on_api_error(max_attempts=3)
    def _scrape_watchlist_page(self, page: int = 1) -> List[Dict[str, Any]]:
        """Scrape a single watchlist page.

        Args:
            page: Page number (1-indexed)

        Returns:
            List of movie dictionaries from this page

        Raises:
            httpx.HTTPError: If request fails
            LetterboxdParseError: If parsing fails
        """
        # Build URL
        if page == 1:
            url = f"{self.BASE_URL}/{self.username}/watchlist/"
        else:
            url = f"{self.BASE_URL}/{self.username}/watchlist/page/{page}/"

        logger.debug("scraping_letterboxd_page", url=url)

        # Fetch page
        response = self.client.get(url)

        # 404 means we've reached the end or invalid username
        if response.status_code == 404:
            if page == 1:
                raise LetterboxdParseError(
                    f"User '{self.username}' not found or watchlist is private"
                )
            return []  # End of pagination

        response.raise_for_status()

        # Parse HTML
        soup = BeautifulSoup(response.content, "html.parser")
        movies = []

        # Find all film poster elements (React components)
        film_posters = soup.select(".react-component[data-item-name]")

        if not film_posters and page == 1:
            raise LetterboxdParseError("No movies found - watchlist may be empty or private")

        for poster in film_posters:
            try:
                # Extract from data attributes
                item_name = poster.get("data-item-name")
                slug = poster.get("data-item-slug")

                if item_name and slug:
                    # Parse title and year from item_name (e.g., "Resurrection (2025)")
                    import re
                    match = re.match(r"^(.+?)\s*\((\d{4})\)$", item_name)

                    if match:
                        title = match.group(1).strip()
                        year = int(match.group(2))

                        movies.append(
                            {
                                "title": title,
                                "year": year,
                                "letterboxd_id": slug,
                            }
                        )
                    else:
                        # Fallback: just use the title without year
                        logger.warning(
                            "letterboxd_no_year_parsed",
                            item_name=item_name,
                            slug=slug,
                            message="Could not extract year from item name",
                        )

            except Exception as e:
                logger.warning(
                    "letterboxd_parse_error",
                    poster_html=str(poster)[:200],
                    error=str(e),
                )
                continue

        return movies

    def validate_username(self) -> bool:
        """Validate that the username exists and watchlist is accessible.

        Returns:
            True if username is valid and watchlist accessible

        Raises:
            httpx.HTTPError: If request fails
        """
        url = f"{self.BASE_URL}/{self.username}/watchlist/"

        try:
            response = self.client.get(url)
            return response.status_code == 200
        except Exception as e:
            logger.error("letterboxd_validation_failed", username=self.username, error=str(e))
            return False


class LetterboxdParseError(Exception):
    """Raised when Letterboxd HTML parsing fails."""

    pass
