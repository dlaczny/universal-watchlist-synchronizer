"""Movie data model."""

from dataclasses import dataclass
from datetime import datetime
from typing import Optional

from src.models import (
    BaseModel,
    ValidationError,
    validate_tmdb_id,
    validate_imdb_id,
    validate_year,
)


@dataclass
class Movie(BaseModel):
    """Represents a movie from Letterboxd with metadata from TMDB.

    Attributes:
        tmdb_id: Primary identifier from TMDB
        title: Movie title
        year: Release year
        imdb_id: Optional IMDB identifier (e.g., 'tt0137523')
        letterboxd_id: Optional Letterboxd film slug
        poster_url: Optional poster image URL from TMDB
        last_updated: Timestamp of last metadata update
    """

    tmdb_id: int
    title: str
    year: int
    imdb_id: Optional[str] = None
    letterboxd_id: Optional[str] = None
    poster_url: Optional[str] = None
    last_updated: datetime = None

    def __post_init__(self):
        """Initialize with current timestamp if not provided."""
        if self.last_updated is None:
            self.last_updated = datetime.now()
        self.validate()

    def validate(self) -> None:
        """Validate movie data.

        Raises:
            ValidationError: If validation fails
        """
        # Validate TMDB ID
        validate_tmdb_id(self.tmdb_id)

        # Validate title
        if not self.title or len(self.title.strip()) == 0:
            raise ValidationError("Movie title cannot be empty")

        # Validate year
        validate_year(self.year)

        # Validate IMDB ID if provided
        validate_imdb_id(self.imdb_id)

        # Validate letterboxd_id if provided
        if self.letterboxd_id is not None and len(self.letterboxd_id.strip()) == 0:
            raise ValidationError("Letterboxd ID cannot be empty string")

    def to_dict(self) -> dict:
        """Convert movie to dictionary representation.

        Returns:
            Dictionary with movie data suitable for serialization
        """
        return {
            "tmdb_id": self.tmdb_id,
            "title": self.title,
            "year": self.year,
            "imdb_id": self.imdb_id,
            "letterboxd_id": self.letterboxd_id,
            "poster_url": self.poster_url,
            "last_updated": self.last_updated.isoformat() if self.last_updated else None,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "Movie":
        """Create Movie instance from dictionary.

        Args:
            data: Dictionary with movie data

        Returns:
            Movie instance
        """
        # Parse datetime if it's a string
        last_updated = data.get("last_updated")
        if isinstance(last_updated, str):
            last_updated = datetime.fromisoformat(last_updated)

        return cls(
            tmdb_id=data["tmdb_id"],
            title=data["title"],
            year=data["year"],
            imdb_id=data.get("imdb_id"),
            letterboxd_id=data.get("letterboxd_id"),
            poster_url=data.get("poster_url"),
            last_updated=last_updated,
        )

    def __str__(self) -> str:
        """String representation of movie."""
        return f"{self.title} ({self.year}) [TMDB: {self.tmdb_id}]"

    def __repr__(self) -> str:
        """Detailed representation of movie."""
        return (
            f"Movie(tmdb_id={self.tmdb_id}, title='{self.title}', year={self.year}, "
            f"imdb_id='{self.imdb_id}', letterboxd_id='{self.letterboxd_id}')"
        )
