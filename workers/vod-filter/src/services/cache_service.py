"""SQLite-based caching service for movie metadata and VOD availability.

Provides connection management, schema initialization, and CRUD operations.
"""

import sqlite3
import json
from pathlib import Path
from typing import Optional, List, Dict, Any
from datetime import datetime, timedelta
from contextlib import contextmanager
import structlog

from src.models.movie import Movie

logger = structlog.get_logger(__name__)


class CacheService:
    """SQLite-based cache for movie metadata, VOD availability, and sync state."""

    def __init__(self, database_path: str = "data/vod-filter.db"):
        """Initialize cache service.

        Args:
            database_path: Path to SQLite database file
        """
        self.database_path = Path(database_path)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        self._initialize_schema()

    def _initialize_schema(self) -> None:
        """Initialize database schema from schema.sql file."""
        schema_path = Path(__file__).parent.parent / "models" / "schema.sql"

        if not schema_path.exists():
            raise FileNotFoundError(f"Schema file not found: {schema_path}")

        with open(schema_path, "r", encoding="utf-8") as f:
            schema_sql = f.read()

        with self.get_connection() as conn:
            conn.executescript(schema_sql)
            conn.commit()

        logger.info("database_initialized", database_path=str(self.database_path))

    @contextmanager
    def get_connection(self):
        """Get a database connection context manager.

        Yields:
            sqlite3.Connection: Database connection

        Example:
            with cache_service.get_connection() as conn:
                cursor = conn.execute("SELECT * FROM movies")
                results = cursor.fetchall()
        """
        conn = sqlite3.connect(str(self.database_path))
        conn.row_factory = sqlite3.Row  # Enable dict-like access
        try:
            yield conn
        finally:
            conn.close()

    def upsert_movie(
        self,
        tmdb_id: int,
        title: str,
        year: int,
        imdb_id: Optional[str] = None,
        letterboxd_id: Optional[str] = None,
        poster_url: Optional[str] = None,
    ) -> None:
        """Insert or update movie metadata.

        Args:
            tmdb_id: TMDB identifier (primary key)
            title: Movie title
            year: Release year
            imdb_id: Optional IMDB identifier
            letterboxd_id: Optional Letterboxd film slug
            poster_url: Optional poster image URL
        """
        with self.get_connection() as conn:
            conn.execute(
                """
                INSERT INTO movies (tmdb_id, title, year, imdb_id, letterboxd_id, poster_url, last_updated)
                VALUES (?, ?, ?, ?, ?, ?, CURRENT_TIMESTAMP)
                ON CONFLICT(tmdb_id) DO UPDATE SET
                    title = excluded.title,
                    year = excluded.year,
                    imdb_id = excluded.imdb_id,
                    letterboxd_id = excluded.letterboxd_id,
                    poster_url = excluded.poster_url,
                    last_updated = CURRENT_TIMESTAMP
                """,
                (tmdb_id, title, year, imdb_id, letterboxd_id, poster_url),
            )
            conn.commit()

        logger.debug(
            "movie_upserted",
            tmdb_id=tmdb_id,
            title=title,
            year=year,
        )

    def get_movie(self, tmdb_id: int) -> Optional[Movie]:
        """Get movie metadata by TMDB ID.

        Args:
            tmdb_id: TMDB identifier

        Returns:
            Movie object or None if not found
        """
        with self.get_connection() as conn:
            cursor = conn.execute(
                "SELECT * FROM movies WHERE tmdb_id = ?",
                (tmdb_id,),
            )
            row = cursor.fetchone()
            if row:
                row_dict = dict(row)
                return Movie(
                    tmdb_id=row_dict["tmdb_id"],
                    title=row_dict["title"],
                    year=row_dict["year"],
                    imdb_id=row_dict.get("imdb_id"),
                    letterboxd_id=row_dict.get("letterboxd_id"),
                    poster_url=row_dict.get("poster_url"),
                )
            return None

    def get_movie_dict(self, tmdb_id: int) -> Optional[Dict[str, Any]]:
        """Get movie metadata by TMDB ID as dictionary.

        Args:
            tmdb_id: TMDB identifier

        Returns:
            Dict with movie data or None if not found
        """
        with self.get_connection() as conn:
            cursor = conn.execute(
                "SELECT * FROM movies WHERE tmdb_id = ?",
                (tmdb_id,),
            )
            row = cursor.fetchone()
            return dict(row) if row else None

    def upsert_vod_availability(
        self,
        tmdb_id: int,
        provider_id: int,
        provider_name: str,
        region: str,
        availability_type: str,
    ) -> None:
        """Insert or update VOD availability record.

        Args:
            tmdb_id: Movie TMDB ID
            provider_id: TMDB provider ID
            provider_name: Provider name (e.g., 'HBO Max')
            region: Two-letter region code (e.g., 'PL')
            availability_type: Type of availability ('flatrate', 'rent', 'buy', 'ads')
        """
        with self.get_connection() as conn:
            conn.execute(
                """
                INSERT INTO vod_availability
                    (tmdb_id, provider_id, provider_name, region, availability_type, checked_at)
                VALUES (?, ?, ?, ?, ?, CURRENT_TIMESTAMP)
                ON CONFLICT(tmdb_id, provider_id) DO UPDATE SET
                    provider_name = excluded.provider_name,
                    region = excluded.region,
                    availability_type = excluded.availability_type,
                    checked_at = CURRENT_TIMESTAMP
                """,
                (tmdb_id, provider_id, provider_name, region, availability_type),
            )
            conn.commit()

        logger.debug(
            "vod_availability_upserted",
            tmdb_id=tmdb_id,
            provider_id=provider_id,
            provider_name=provider_name,
        )

    def get_vod_availability(
        self, tmdb_id: int, cache_ttl_hours: int = 48
    ) -> Optional[List[Dict[str, Any]]]:
        """Get cached VOD availability for a movie.

        Args:
            tmdb_id: Movie TMDB ID
            cache_ttl_hours: Cache TTL in hours (default: 48)

        Returns:
            List of availability records or None if cache expired/not found
        """
        cutoff_time = datetime.now() - timedelta(hours=cache_ttl_hours)

        with self.get_connection() as conn:
            cursor = conn.execute(
                """
                SELECT * FROM vod_availability
                WHERE tmdb_id = ?
                AND checked_at > ?
                """,
                (tmdb_id, cutoff_time.isoformat()),
            )
            rows = cursor.fetchall()

            if rows:
                logger.debug(
                    "vod_availability_cache_hit",
                    tmdb_id=tmdb_id,
                    providers_count=len(rows),
                )
                return [dict(row) for row in rows]

        logger.debug("vod_availability_cache_miss", tmdb_id=tmdb_id)
        return None

    def clear_vod_availability(self, tmdb_id: int) -> None:
        """Clear all VOD availability records for a movie.

        Args:
            tmdb_id: Movie TMDB ID
        """
        with self.get_connection() as conn:
            conn.execute(
                "DELETE FROM vod_availability WHERE tmdb_id = ?",
                (tmdb_id,),
            )
            conn.commit()

        logger.debug("vod_availability_cleared", tmdb_id=tmdb_id)

    def upsert_sync_state(
        self,
        tmdb_id: int,
        on_letterboxd: bool = False,
        on_plex: bool = False,
        on_radarr: bool = False,
        vod_available: bool = False,
        sync_error: Optional[str] = None,
    ) -> None:
        """Insert or update sync state for a movie.

        Args:
            tmdb_id: Movie TMDB ID
            on_letterboxd: Whether movie is on Letterboxd watchlist
            on_plex: Whether movie is on Plex watchlist
            on_radarr: Whether movie is in Radarr
            vod_available: Whether movie is available on any configured VOD service
            sync_error: Optional error message from last sync
        """
        with self.get_connection() as conn:
            conn.execute(
                """
                INSERT INTO sync_state
                    (tmdb_id, on_letterboxd, on_plex, on_radarr, vod_available, last_synced, sync_error)
                VALUES (?, ?, ?, ?, ?, CURRENT_TIMESTAMP, ?)
                ON CONFLICT(tmdb_id) DO UPDATE SET
                    on_letterboxd = excluded.on_letterboxd,
                    on_plex = excluded.on_plex,
                    on_radarr = excluded.on_radarr,
                    vod_available = excluded.vod_available,
                    last_synced = CURRENT_TIMESTAMP,
                    sync_error = excluded.sync_error
                """,
                (tmdb_id, on_letterboxd, on_plex, on_radarr, vod_available, sync_error),
            )
            conn.commit()

        logger.debug(
            "sync_state_upserted",
            tmdb_id=tmdb_id,
            on_letterboxd=on_letterboxd,
            on_plex=on_plex,
            on_radarr=on_radarr,
            vod_available=vod_available,
        )

    def get_sync_state(self, tmdb_id: int) -> Optional[Dict[str, Any]]:
        """Get sync state for a movie.

        Args:
            tmdb_id: Movie TMDB ID

        Returns:
            Dict with sync state or None if not found
        """
        with self.get_connection() as conn:
            cursor = conn.execute(
                "SELECT * FROM sync_state WHERE tmdb_id = ?",
                (tmdb_id,),
            )
            row = cursor.fetchone()
            return dict(row) if row else None

    def update_sync_state(
        self,
        tmdb_id: int,
        on_letterboxd: Optional[bool] = None,
        on_plex: Optional[bool] = None,
        on_radarr: Optional[bool] = None,
        vod_available: Optional[bool] = None,
        sync_error: Optional[str] = None,
    ) -> None:
        """Update specific fields in sync state for a movie (partial update).

        Args:
            tmdb_id: Movie TMDB ID
            on_letterboxd: Whether movie is on Letterboxd watchlist (None = no change)
            on_plex: Whether movie is on Plex watchlist (None = no change)
            on_radarr: Whether movie is in Radarr (None = no change)
            vod_available: Whether movie is available on VOD (None = no change)
            sync_error: Optional error message (None = no change)
        """
        # Get current state
        current_state = self.get_sync_state(tmdb_id)

        if current_state is None:
            # Create new sync state with provided values (defaulting to False)
            self.upsert_sync_state(
                tmdb_id=tmdb_id,
                on_letterboxd=on_letterboxd if on_letterboxd is not None else False,
                on_plex=on_plex if on_plex is not None else False,
                on_radarr=on_radarr if on_radarr is not None else False,
                vod_available=vod_available if vod_available is not None else False,
                sync_error=sync_error,
            )
        else:
            # Update only provided fields
            self.upsert_sync_state(
                tmdb_id=tmdb_id,
                on_letterboxd=on_letterboxd if on_letterboxd is not None else current_state["on_letterboxd"],
                on_plex=on_plex if on_plex is not None else current_state["on_plex"],
                on_radarr=on_radarr if on_radarr is not None else current_state["on_radarr"],
                vod_available=vod_available if vod_available is not None else current_state["vod_available"],
                sync_error=sync_error if sync_error is not None else current_state.get("sync_error"),
            )

    def get_all_sync_states(self) -> List[Dict[str, Any]]:
        """Get all sync states from the database.

        Returns:
            List of all sync state dictionaries
        """
        with self.get_connection() as conn:
            cursor = conn.execute("SELECT * FROM sync_state")
            rows = cursor.fetchall()
            return [dict(row) for row in rows]

    def get_movies_for_radarr(self) -> List[Dict[str, Any]]:
        """Get all movies that should be in Radarr import list.

        Returns movies where vod_available=false (not on any configured streaming service).

        Returns:
            List of movie dictionaries with metadata and sync state
        """
        with self.get_connection() as conn:
            cursor = conn.execute(
                """
                SELECT m.*, s.vod_available
                FROM movies m
                INNER JOIN sync_state s ON m.tmdb_id = s.tmdb_id
                WHERE s.vod_available = 0 AND s.on_letterboxd = 1
                ORDER BY m.year DESC, m.title ASC
                """
            )
            rows = cursor.fetchall()
            return [dict(row) for row in rows]

    def get_enabled_providers(self, region: str = "PL") -> List[Dict[str, Any]]:
        """Get all enabled streaming providers for a region.

        Args:
            region: Two-letter region code

        Returns:
            List of enabled provider dictionaries
        """
        with self.get_connection() as conn:
            cursor = conn.execute(
                "SELECT * FROM streaming_providers WHERE enabled = 1 AND region = ?",
                (region,),
            )
            rows = cursor.fetchall()
            return [dict(row) for row in rows]

    def get_all_providers(self, region: Optional[str] = None) -> List[Dict[str, Any]]:
        """Get all streaming providers (enabled and disabled).

        Args:
            region: Optional region filter. If None, returns providers for all regions

        Returns:
            List of provider dictionaries
        """
        with self.get_connection() as conn:
            if region:
                cursor = conn.execute(
                    "SELECT * FROM streaming_providers WHERE region = ?",
                    (region,),
                )
            else:
                cursor = conn.execute("SELECT * FROM streaming_providers")

            rows = cursor.fetchall()
            return [dict(row) for row in rows]

    def upsert_provider(
        self,
        provider_id: int,
        provider_name: str,
        enabled: bool = True,
        region: str = "PL",
    ) -> None:
        """Insert or update streaming provider configuration.

        Args:
            provider_id: TMDB provider ID (primary key)
            provider_name: Human-readable provider name
            enabled: Whether provider is enabled for VOD checks
            region: Two-letter region code
        """
        with self.get_connection() as conn:
            conn.execute(
                """
                INSERT INTO streaming_providers (provider_id, provider_name, enabled, region)
                VALUES (?, ?, ?, ?)
                ON CONFLICT(provider_id) DO UPDATE SET
                    provider_name = excluded.provider_name,
                    enabled = excluded.enabled,
                    region = excluded.region
                """,
                (provider_id, provider_name, enabled, region),
            )
            conn.commit()

        logger.debug(
            "provider_upserted",
            provider_id=provider_id,
            provider_name=provider_name,
            enabled=enabled,
            region=region,
        )

    def update_provider_status(self, provider_id: int, enabled: bool) -> None:
        """Update provider enabled/disabled status.

        Args:
            provider_id: TMDB provider ID
            enabled: New enabled status
        """
        with self.get_connection() as conn:
            conn.execute(
                "UPDATE streaming_providers SET enabled = ? WHERE provider_id = ?",
                (enabled, provider_id),
            )
            conn.commit()

        logger.debug(
            "provider_status_updated",
            provider_id=provider_id,
            enabled=enabled,
        )

    def delete_provider(self, provider_id: int) -> None:
        """Delete a streaming provider configuration.

        Args:
            provider_id: TMDB provider ID
        """
        with self.get_connection() as conn:
            conn.execute(
                "DELETE FROM streaming_providers WHERE provider_id = ?",
                (provider_id,),
            )
            conn.commit()

        logger.debug("provider_deleted", provider_id=provider_id)

    def initialize_default_providers(
        self, providers: List[Dict[str, Any]]
    ) -> None:
        """Initialize default streaming providers if none exist.

        Args:
            providers: List of provider dicts with provider_id, provider_name, enabled, region
        """
        # Check if any providers exist
        existing = self.get_all_providers()

        if not existing:
            logger.info(
                "initializing_default_providers",
                count=len(providers),
            )

            for provider in providers:
                self.upsert_provider(
                    provider_id=provider["provider_id"],
                    provider_name=provider["provider_name"],
                    enabled=provider.get("enabled", True),
                    region=provider.get("region", "PL"),
                )

            logger.info("default_providers_initialized", count=len(providers))
        else:
            logger.debug(
                "providers_already_exist",
                count=len(existing),
            )

    def get_database_stats(self) -> Dict[str, int]:
        """Get database statistics.

        Returns:
            Dict with counts of movies, VOD records, and sync states
        """
        with self.get_connection() as conn:
            stats = {}

            # Movie count
            cursor = conn.execute("SELECT COUNT(*) as count FROM movies")
            stats["movies"] = cursor.fetchone()["count"]

            # VOD availability count
            cursor = conn.execute("SELECT COUNT(*) as count FROM vod_availability")
            stats["vod_records"] = cursor.fetchone()["count"]

            # Sync state count
            cursor = conn.execute("SELECT COUNT(*) as count FROM sync_state")
            stats["sync_states"] = cursor.fetchone()["count"]

            cursor = conn.execute("SELECT COUNT(*) as count FROM run_history")
            stats["run_history"] = cursor.fetchone()["count"]

            # Movies on Letterboxd
            cursor = conn.execute(
                "SELECT COUNT(*) as count FROM sync_state WHERE on_letterboxd = 1"
            )
            stats["on_letterboxd"] = cursor.fetchone()["count"]

            # Movies on Plex
            cursor = conn.execute(
                "SELECT COUNT(*) as count FROM sync_state WHERE on_plex = 1"
            )
            stats["on_plex"] = cursor.fetchone()["count"]

            # Movies on Radarr
            cursor = conn.execute(
                "SELECT COUNT(*) as count FROM sync_state WHERE on_radarr = 1"
            )
            stats["on_radarr"] = cursor.fetchone()["count"]

            # VOD available
            cursor = conn.execute(
                "SELECT COUNT(*) as count FROM sync_state WHERE vod_available = 1"
            )
            stats["vod_available"] = cursor.fetchone()["count"]

            return stats

    def start_run(self, workflow: str, dry_run: bool, trigger: str = "manual") -> int:
        """Record the start of an operational workflow run."""
        with self.get_connection() as conn:
            cursor = conn.execute(
                """
                INSERT INTO run_history (workflow, status, dry_run, trigger, started_at)
                VALUES (?, 'running', ?, ?, CURRENT_TIMESTAMP)
                """,
                (workflow, dry_run, trigger),
            )
            conn.commit()
            run_id = int(cursor.lastrowid)

        logger.info("run_history_started", run_id=run_id, workflow=workflow, dry_run=dry_run)
        return run_id

    def finish_run(
        self,
        run_id: int,
        status: str,
        exit_code: Optional[int],
        summary: Optional[Dict[str, Any]] = None,
        error: Optional[str] = None,
    ) -> None:
        """Record workflow completion details."""
        summary_json = json.dumps(summary, sort_keys=True) if summary is not None else None
        with self.get_connection() as conn:
            conn.execute(
                """
                UPDATE run_history
                SET status = ?,
                    finished_at = CURRENT_TIMESTAMP,
                    exit_code = ?,
                    summary = ?,
                    error = ?
                WHERE id = ?
                """,
                (status, exit_code, summary_json, error, run_id),
            )
            conn.commit()

        logger.info("run_history_finished", run_id=run_id, status=status, exit_code=exit_code)

    def get_recent_runs(self, limit: int = 20) -> List[Dict[str, Any]]:
        """Return recent workflow run history rows."""
        with self.get_connection() as conn:
            cursor = conn.execute(
                """
                SELECT *
                FROM run_history
                ORDER BY started_at DESC, id DESC
                LIMIT ?
                """,
                (limit,),
            )
            return [dict(row) for row in cursor.fetchall()]

    def get_cache_metadata(self) -> List[Dict[str, Any]]:
        """Return cache metadata rows."""
        with self.get_connection() as conn:
            cursor = conn.execute(
                """
                SELECT cache_name, last_refresh, ttl_minutes
                FROM cache_metadata
                ORDER BY cache_name ASC
                """
            )
            return [dict(row) for row in cursor.fetchall()]

    def get_recent_vod_availability(self, limit: int = 20) -> List[Dict[str, Any]]:
        """Return recent VOD availability cache rows with movie titles."""
        with self.get_connection() as conn:
            cursor = conn.execute(
                """
                SELECT v.tmdb_id, m.title, m.year, v.provider_id, v.provider_name,
                       v.region, v.availability_type, v.checked_at
                FROM vod_availability v
                LEFT JOIN movies m ON m.tmdb_id = v.tmdb_id
                ORDER BY v.checked_at DESC
                LIMIT ?
                """,
                (limit,),
            )
            return [dict(row) for row in cursor.fetchall()]

    # Plex Watchlist Cache Methods

    def is_cache_valid(self, cache_name: str, ttl_minutes: int = 30) -> bool:
        """Check if cache is still valid based on TTL.

        Args:
            cache_name: Name of the cache (e.g., 'plex_watchlist', 'plex_library')
            ttl_minutes: Time-to-live in minutes

        Returns:
            True if cache is valid, False if expired or doesn't exist
        """
        with self.get_connection() as conn:
            cursor = conn.execute(
                """
                SELECT last_refresh
                FROM cache_metadata
                WHERE cache_name = ?
                AND datetime(last_refresh, '+' || ? || ' minutes') > datetime('now')
                """,
                (cache_name, ttl_minutes),
            )
            result = cursor.fetchone()
            return result is not None

    def update_cache_metadata(self, cache_name: str, ttl_minutes: int = 30) -> None:
        """Update cache metadata with current timestamp.

        Args:
            cache_name: Name of the cache
            ttl_minutes: Time-to-live in minutes
        """
        with self.get_connection() as conn:
            conn.execute(
                """
                INSERT INTO cache_metadata (cache_name, last_refresh, ttl_minutes)
                VALUES (?, CURRENT_TIMESTAMP, ?)
                ON CONFLICT(cache_name) DO UPDATE SET
                    last_refresh = CURRENT_TIMESTAMP,
                    ttl_minutes = excluded.ttl_minutes
                """,
                (cache_name, ttl_minutes),
            )
            conn.commit()

    def get_plex_watchlist_cache(self) -> List[Dict[str, Any]]:
        """Get cached Plex watchlist.

        Returns:
            List of movies from cache
        """
        with self.get_connection() as conn:
            cursor = conn.execute(
                """
                SELECT tmdb_id, title, year, cached_at
                FROM plex_watchlist_cache
                ORDER BY cached_at DESC
                """
            )
            return [dict(row) for row in cursor.fetchall()]

    def refresh_plex_watchlist_cache(self, movies: List[Dict[str, Any]]) -> None:
        """Refresh Plex watchlist cache with new data.

        Args:
            movies: List of movie dicts with tmdb_id, title, year
        """
        with self.get_connection() as conn:
            # Clear old cache
            conn.execute("DELETE FROM plex_watchlist_cache")

            # Insert new cache
            for movie in movies:
                conn.execute(
                    """
                    INSERT INTO plex_watchlist_cache (tmdb_id, title, year, cached_at)
                    VALUES (?, ?, ?, CURRENT_TIMESTAMP)
                    """,
                    (movie["tmdb_id"], movie["title"], movie["year"]),
                )

            conn.commit()

        # Update metadata
        self.update_cache_metadata("plex_watchlist", ttl_minutes=30)

        logger.info("plex_watchlist_cache_refreshed", count=len(movies))

    def get_plex_library_cache(self) -> List[Dict[str, Any]]:
        """Get cached Plex library.

        Returns:
            List of movies from cache
        """
        with self.get_connection() as conn:
            cursor = conn.execute(
                """
                SELECT tmdb_id, title, year, cached_at
                FROM plex_library_cache
                ORDER BY cached_at DESC
                """
            )
            return [dict(row) for row in cursor.fetchall()]

    def refresh_plex_library_cache(self, movies: List[Dict[str, Any]]) -> None:
        """Refresh Plex library cache with new data.

        Args:
            movies: List of movie dicts with tmdb_id, title, year
        """
        with self.get_connection() as conn:
            # Clear old cache
            conn.execute("DELETE FROM plex_library_cache")

            # Insert new cache
            for movie in movies:
                conn.execute(
                    """
                    INSERT INTO plex_library_cache (tmdb_id, title, year, cached_at)
                    VALUES (?, ?, ?, CURRENT_TIMESTAMP)
                    """,
                    (movie["tmdb_id"], movie["title"], movie["year"]),
                )

            conn.commit()

        # Update metadata
        self.update_cache_metadata("plex_library", ttl_minutes=60)

        logger.info("plex_library_cache_refreshed", count=len(movies))
