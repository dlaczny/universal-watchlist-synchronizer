"""Retry logic with exponential backoff using tenacity.

Provides decorators and utilities for robust API calls with automatic retries.
"""

from functools import wraps
from typing import Callable, Type, Tuple
import time
import logging
import re

from tenacity import (
    retry,
    stop_after_attempt,
    wait_exponential,
    wait_fixed,
    retry_if_exception_type,
    before_sleep_log,
)
import structlog

logger = structlog.get_logger(__name__)


def retry_on_api_error(
    max_attempts: int = 3,
    min_wait: int = 2,
    max_wait: int = 16,
    exceptions: Tuple[Type[Exception], ...] = (Exception,),
) -> Callable:
    """Decorator for retrying functions with exponential backoff.

    Args:
        max_attempts: Maximum number of retry attempts
        min_wait: Minimum wait time in seconds
        max_wait: Maximum wait time in seconds
        exceptions: Tuple of exception types to retry on

    Returns:
        Decorated function with retry logic
    """

    def decorator(func: Callable) -> Callable:
        @retry(
            stop=stop_after_attempt(max_attempts),
            wait=wait_exponential(multiplier=1, min=min_wait, max=max_wait),
            retry=retry_if_exception_type(exceptions),
            before_sleep=before_sleep_log(logger, log_level=logging.WARNING),
            reraise=True,
        )
        @wraps(func)
        def wrapper(*args, **kwargs):
            return func(*args, **kwargs)

        return wrapper

    return decorator


def retry_on_rate_limit(
    max_attempts: int = 5,
    min_wait: int = 10,
    max_wait: int = 60,
) -> Callable:
    """Decorator for retrying on rate limit errors with longer backoff.

    Specifically designed for APIs with rate limiting (like TMDB).

    Args:
        max_attempts: Maximum number of retry attempts
        min_wait: Minimum wait time in seconds
        max_wait: Maximum wait time in seconds

    Returns:
        Decorated function with retry logic
    """
    import httpx

    def decorator(func: Callable) -> Callable:
        @retry(
            stop=stop_after_attempt(max_attempts),
            wait=wait_exponential(multiplier=2, min=min_wait, max=max_wait),
            retry=retry_if_exception_type((httpx.HTTPStatusError,)),
            before_sleep=before_sleep_log(logger, log_level=logging.WARNING),
            reraise=True,
        )
        @wraps(func)
        def wrapper(*args, **kwargs):
            return func(*args, **kwargs)

        return wrapper

    return decorator


def _extract_retry_after_seconds(error: Exception) -> int | None:
    """Extract Retry-After seconds from common exception forms."""
    response = getattr(error, "response", None)
    headers = getattr(response, "headers", None)
    if headers:
        retry_after = headers.get("Retry-After") or headers.get("retry-after")
        if retry_after and str(retry_after).isdigit():
            return int(retry_after)

    match = re.search(r"retry-after[:=]\s*(\d+)", str(error), flags=re.IGNORECASE)
    if match:
        return int(match.group(1))

    return None


def is_plex_rate_limit_error(error: Exception) -> bool:
    """Return whether an exception looks like a Plex rate-limit failure."""
    error_msg = str(error).lower()
    return (
        "429" in error_msg
        or "rate limit" in error_msg
        or "too many requests" in error_msg
        or "too_many_requests" in error_msg
    )


def retry_on_plex_rate_limit(
    wait_seconds: int | None = None,
    max_attempts: int = 5,
    min_wait: int = 15,
    max_wait: int = 180,
    multiplier: int = 2,
) -> Callable:
    """Custom retry decorator for Plex 429 rate limit errors.

    This decorator specifically handles Plex rate limit errors by waiting
    a fixed amount of time before retrying.

    Args:
        wait_seconds: Legacy fixed wait override. Prefer min/max exponential args.
        max_attempts: Maximum attempts including the first call.
        min_wait: First exponential wait in seconds.
        max_wait: Maximum wait in seconds.
        multiplier: Exponential multiplier.

    Returns:
        Decorated function with rate limit retry logic
    """

    def decorator(func: Callable) -> Callable:
        @wraps(func)
        def wrapper(*args, **kwargs):
            for attempt in range(max_attempts):
                try:
                    return func(*args, **kwargs)
                except Exception as e:
                    # Check if it's a 429 rate limit error
                    if is_plex_rate_limit_error(e):
                        if attempt < max_attempts - 1:
                            retry_after = _extract_retry_after_seconds(e)
                            if retry_after is not None:
                                wait = min(retry_after, max_wait)
                            elif wait_seconds is not None:
                                wait = wait_seconds
                            else:
                                wait = min(min_wait * (multiplier ** attempt), max_wait)
                            logger.warning(
                                "plex_rate_limit_encountered",
                                attempt=attempt + 1,
                                wait_seconds=wait,
                                error=str(e),
                            )
                            time.sleep(wait)
                            continue
                    # Re-raise if not a rate limit error or max retries reached
                    raise

            return None  # Should never reach here

        return wrapper

    return decorator
