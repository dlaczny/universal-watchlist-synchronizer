"""Streaming provider configuration model."""

from dataclasses import dataclass
from typing import Optional

from src.models import BaseModel, ValidationError


@dataclass
class StreamingProvider(BaseModel):
    """Represents a configured streaming service that the user owns.

    Attributes:
        provider_id: TMDB provider ID (primary key)
        provider_name: Human-readable name (e.g., "HBO Max")
        enabled: Whether to check this provider for availability
        region: Two-letter ISO region code (e.g., "PL" for Poland)
    """

    provider_id: int
    provider_name: str
    enabled: bool = True
    region: str = "PL"

    def __post_init__(self):
        """Validate provider data after initialization."""
        self.validate()

    def validate(self) -> None:
        """Validate streaming provider data.

        Raises:
            ValidationError: If validation fails
        """
        # Validate provider_id
        if not isinstance(self.provider_id, int) or self.provider_id <= 0:
            raise ValidationError(
                f"Provider ID must be a positive integer, got: {self.provider_id}"
            )

        # Validate provider_name
        if not self.provider_name or len(self.provider_name.strip()) == 0:
            raise ValidationError("Provider name cannot be empty")

        # Validate region code (must be 2 uppercase letters)
        if not isinstance(self.region, str) or len(self.region) != 2:
            raise ValidationError(
                f"Region must be 2-character ISO code, got: {self.region}"
            )
        if not self.region.isupper():
            raise ValidationError(f"Region code must be uppercase, got: {self.region}")
        if not self.region.isalpha():
            raise ValidationError(
                f"Region code must contain only letters, got: {self.region}"
            )

        # Validate enabled flag
        if not isinstance(self.enabled, bool):
            raise ValidationError(
                f"Enabled must be a boolean value, got: {type(self.enabled)}"
            )

    def to_dict(self) -> dict:
        """Convert provider to dictionary representation.

        Returns:
            Dictionary with provider data suitable for serialization
        """
        return {
            "provider_id": self.provider_id,
            "provider_name": self.provider_name,
            "enabled": self.enabled,
            "region": self.region,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "StreamingProvider":
        """Create StreamingProvider instance from dictionary.

        Args:
            data: Dictionary with provider data

        Returns:
            StreamingProvider instance
        """
        return cls(
            provider_id=data["provider_id"],
            provider_name=data["provider_name"],
            enabled=data.get("enabled", True),
            region=data.get("region", "PL"),
        )

    def __str__(self) -> str:
        """String representation of provider."""
        status = "enabled" if self.enabled else "disabled"
        return f"{self.provider_name} ({self.region}) [{status}]"

    def __repr__(self) -> str:
        """Detailed representation of provider."""
        return (
            f"StreamingProvider(provider_id={self.provider_id}, "
            f"provider_name='{self.provider_name}', enabled={self.enabled}, "
            f"region='{self.region}')"
        )


# Default provider configuration for Poland region
DEFAULT_PROVIDERS_PL = [
    StreamingProvider(
        provider_id=384, provider_name="HBO Max", enabled=True, region="PL"
    ),
    StreamingProvider(
        provider_id=119, provider_name="Amazon Prime Video", enabled=True, region="PL"
    ),
    # Note: SkyShowtime provider ID needs to be determined from TMDB API
    # This is a placeholder until we can query the actual ID
    # StreamingProvider(provider_id=TBD, provider_name="SkyShowtime", enabled=True, region="PL"),
]
