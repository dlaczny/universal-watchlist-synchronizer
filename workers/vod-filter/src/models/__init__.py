"""Data models for VOD Filter.

This module provides base classes and shared validation logic for all data models.
"""

from dataclasses import dataclass
from datetime import datetime
from typing import Optional
import re


class ValidationError(Exception):
    """Raised when model validation fails."""

    pass


@dataclass
class BaseModel:
    """Base class for all data models with common validation."""

    def validate(self) -> None:
        """Validate model fields.

        Raises:
            ValidationError: If validation fails
        """
        pass


def validate_imdb_id(imdb_id: Optional[str]) -> None:
    """Validate IMDB ID format.

    Args:
        imdb_id: IMDB identifier (e.g., "tt1234567")

    Raises:
        ValidationError: If IMDB ID format is invalid
    """
    if imdb_id is not None:
        if not re.match(r"^tt\d{7,8}$", imdb_id):
            raise ValidationError(f"Invalid IMDB ID format: {imdb_id}")


def validate_year(year: int) -> None:
    """Validate movie release year.

    Args:
        year: Release year

    Raises:
        ValidationError: If year is out of valid range
    """
    current_year = datetime.now().year
    if year < 1800 or year > current_year + 5:
        raise ValidationError(
            f"Invalid year: {year}. Must be between 1800 and {current_year + 5}"
        )


def validate_region_code(region: str) -> None:
    """Validate ISO region code.

    Args:
        region: Two-letter region code (e.g., "PL")

    Raises:
        ValidationError: If region code is invalid
    """
    if len(region) != 2 or not region.isalpha():
        raise ValidationError(f"Invalid region code: {region}. Must be 2-letter ISO code")


def validate_tmdb_id(tmdb_id: int) -> None:
    """Validate TMDB ID.

    Args:
        tmdb_id: TMDB identifier

    Raises:
        ValidationError: If TMDB ID is invalid
    """
    if not isinstance(tmdb_id, int) or tmdb_id <= 0:
        raise ValidationError(f"Invalid TMDB ID: {tmdb_id}. Must be positive integer")


def validate_provider_id(provider_id: int) -> None:
    """Validate streaming provider ID.

    Args:
        provider_id: TMDB provider identifier

    Raises:
        ValidationError: If provider ID is invalid
    """
    if not isinstance(provider_id, int) or provider_id <= 0:
        raise ValidationError(f"Invalid provider ID: {provider_id}. Must be positive integer")


def validate_availability_type(availability_type: str) -> None:
    """Validate availability type.

    Args:
        availability_type: Type of availability (flatrate, rent, buy, ads)

    Raises:
        ValidationError: If availability type is invalid
    """
    valid_types = ["flatrate", "rent", "buy", "ads"]
    if availability_type not in valid_types:
        raise ValidationError(
            f"Invalid availability type: {availability_type}. "
            f"Must be one of: {', '.join(valid_types)}"
        )
