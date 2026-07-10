"""Sync state data model."""

from dataclasses import dataclass
from datetime import datetime
from typing import Optional

from src.models import BaseModel, ValidationError, validate_tmdb_id


@dataclass
class SyncState(BaseModel):
    """Represents synchronization state for a movie across all services.

    Attributes:
        tmdb_id: Movie TMDB identifier
        on_letterboxd: Whether movie is on Letterboxd watchlist
        on_plex: Whether movie is on Plex watchlist
        on_radarr: Whether movie is in Radarr
        vod_available: Whether movie is available on any configured VOD service
        last_synced: Timestamp of last synchronization
        sync_error: Optional error message from last sync attempt
    """

    tmdb_id: int
    on_letterboxd: bool = False
    on_plex: bool = False
    on_radarr: bool = False
    vod_available: bool = False
    last_synced: datetime = None
    sync_error: Optional[str] = None

    def __post_init__(self):
        """Initialize with current timestamp if not provided."""
        if self.last_synced is None:
            self.last_synced = datetime.now()
        self.validate()

    def validate(self) -> None:
        """Validate sync state data.

        Raises:
            ValidationError: If validation fails
        """
        # Validate TMDB ID
        validate_tmdb_id(self.tmdb_id)

        # Validate sync_error length if provided
        if self.sync_error is not None and len(self.sync_error) > 500:
            raise ValidationError(
                f"Sync error message too long (max 500 chars): {len(self.sync_error)}"
            )

    def should_add_to_radarr(self) -> bool:
        """Determine if movie should be added to Radarr.

        Returns:
            True if movie should be in Radarr (on Letterboxd and no VOD availability)
        """
        return self.on_letterboxd and not self.vod_available

    def should_add_to_plex(self) -> bool:
        """Determine if movie should be added to Plex watchlist.

        Returns:
            True if movie should be in Plex (on Letterboxd)
        """
        return self.on_letterboxd

    def should_remove_from_plex(self) -> bool:
        """Determine if movie should be removed from Plex watchlist.

        Returns:
            True if movie should be removed from Plex (not on Letterboxd but is on Plex)
        """
        return not self.on_letterboxd and self.on_plex

    def to_dict(self) -> dict:
        """Convert to dictionary representation.

        Returns:
            Dictionary with sync state data
        """
        return {
            "tmdb_id": self.tmdb_id,
            "on_letterboxd": self.on_letterboxd,
            "on_plex": self.on_plex,
            "on_radarr": self.on_radarr,
            "vod_available": self.vod_available,
            "last_synced": self.last_synced.isoformat() if self.last_synced else None,
            "sync_error": self.sync_error,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "SyncState":
        """Create instance from dictionary.

        Args:
            data: Dictionary with sync state data

        Returns:
            SyncState instance
        """
        # Parse datetime if it's a string
        last_synced = data.get("last_synced")
        if isinstance(last_synced, str):
            last_synced = datetime.fromisoformat(last_synced)

        return cls(
            tmdb_id=data["tmdb_id"],
            on_letterboxd=data.get("on_letterboxd", False),
            on_plex=data.get("on_plex", False),
            on_radarr=data.get("on_radarr", False),
            vod_available=data.get("vod_available", False),
            last_synced=last_synced,
            sync_error=data.get("sync_error"),
        )

    def __str__(self) -> str:
        """String representation."""
        flags = []
        if self.on_letterboxd:
            flags.append("Letterboxd")
        if self.on_plex:
            flags.append("Plex")
        if self.on_radarr:
            flags.append("Radarr")
        if self.vod_available:
            flags.append("VOD")

        return f"SyncState[{self.tmdb_id}]: {', '.join(flags) if flags else 'None'}"

    def __repr__(self) -> str:
        """Detailed representation."""
        return (
            f"SyncState(tmdb_id={self.tmdb_id}, on_letterboxd={self.on_letterboxd}, "
            f"on_plex={self.on_plex}, on_radarr={self.on_radarr}, "
            f"vod_available={self.vod_available}, sync_error={self.sync_error})"
        )
