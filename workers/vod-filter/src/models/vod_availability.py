"""VOD availability data model."""

from dataclasses import dataclass
from datetime import datetime
from typing import Optional

from src.models import (
    BaseModel,
    ValidationError,
    validate_tmdb_id,
    validate_provider_id,
    validate_region_code,
    validate_availability_type,
)


@dataclass
class VODAvailability(BaseModel):
    """Represents streaming availability for a movie on a specific provider.

    Attributes:
        tmdb_id: Movie TMDB identifier
        provider_id: TMDB provider ID (e.g., 384 for HBO Max)
        provider_name: Human-readable provider name
        region: Two-letter ISO 3166-1 region code
        availability_type: Type of availability ('flatrate', 'rent', 'buy', 'ads')
        checked_at: Timestamp when availability was last checked
    """

    tmdb_id: int
    provider_id: int
    provider_name: str
    region: str
    availability_type: str
    checked_at: datetime = None

    def __post_init__(self):
        """Initialize with current timestamp if not provided."""
        if self.checked_at is None:
            self.checked_at = datetime.now()
        self.validate()

    def validate(self) -> None:
        """Validate VOD availability data.

        Raises:
            ValidationError: If validation fails
        """
        # Validate TMDB ID
        validate_tmdb_id(self.tmdb_id)

        # Validate provider ID
        validate_provider_id(self.provider_id)

        # Validate provider name
        if not self.provider_name or len(self.provider_name.strip()) == 0:
            raise ValidationError("Provider name cannot be empty")

        # Validate region code
        validate_region_code(self.region)

        # Validate availability type
        validate_availability_type(self.availability_type)

    def to_dict(self) -> dict:
        """Convert to dictionary representation.

        Returns:
            Dictionary with VOD availability data
        """
        return {
            "tmdb_id": self.tmdb_id,
            "provider_id": self.provider_id,
            "provider_name": self.provider_name,
            "region": self.region,
            "availability_type": self.availability_type,
            "checked_at": self.checked_at.isoformat() if self.checked_at else None,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "VODAvailability":
        """Create instance from dictionary.

        Args:
            data: Dictionary with VOD availability data

        Returns:
            VODAvailability instance
        """
        # Parse datetime if it's a string
        checked_at = data.get("checked_at")
        if isinstance(checked_at, str):
            checked_at = datetime.fromisoformat(checked_at)

        return cls(
            tmdb_id=data["tmdb_id"],
            provider_id=data["provider_id"],
            provider_name=data["provider_name"],
            region=data["region"],
            availability_type=data["availability_type"],
            checked_at=checked_at,
        )

    def __str__(self) -> str:
        """String representation."""
        return f"{self.provider_name} ({self.availability_type}) in {self.region}"

    def __repr__(self) -> str:
        """Detailed representation."""
        return (
            f"VODAvailability(tmdb_id={self.tmdb_id}, provider_id={self.provider_id}, "
            f"provider_name='{self.provider_name}', region='{self.region}', "
            f"availability_type='{self.availability_type}')"
        )
