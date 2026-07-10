"""Structured logging configuration using structlog.

Provides JSON-formatted logging for machine readability and easy parsing.
"""

import logging
import sys
from typing import Optional, TextIO

import structlog


def ensure_utf8_stream(stream: TextIO) -> TextIO:
    """Best-effort configure text streams for UTF-8 logging output.

    Windows terminals may default to legacy encodings such as cp1250. Structured
    logs can contain film titles with characters outside that code page, and the
    standard logging handler should not crash because of that.
    """
    reconfigure = getattr(stream, "reconfigure", None)
    if callable(reconfigure):
        try:
            reconfigure(encoding="utf-8", errors="replace")
        except (OSError, ValueError):
            return stream
    return stream


def configure_logging(level: str = "INFO", log_file: Optional[str] = None) -> None:
    """Configure structured logging with JSON output.

    Args:
        level: Log level (DEBUG, INFO, WARNING, ERROR)
        log_file: Optional log file path (None = stdout only)
    """
    # Convert level string to logging level
    log_level = getattr(logging, level.upper(), logging.INFO)

    # Configure structlog processors
    processors = [
        structlog.stdlib.add_log_level,
        structlog.stdlib.add_logger_name,
        structlog.processors.TimeStamper(fmt="iso"),
        structlog.processors.StackInfoRenderer(),
        structlog.processors.format_exc_info,
        structlog.processors.UnicodeDecoder(),
        structlog.processors.JSONRenderer(),
    ]

    # Configure structlog
    structlog.configure(
        processors=processors,
        context_class=dict,
        logger_factory=structlog.stdlib.LoggerFactory(),
        wrapper_class=structlog.stdlib.BoundLogger,
        cache_logger_on_first_use=True,
    )

    # Configure standard logging handlers
    handlers = [logging.StreamHandler(ensure_utf8_stream(sys.stdout))]

    if log_file:
        handlers.append(logging.FileHandler(log_file, encoding="utf-8"))

    # Configure root logger
    logging.basicConfig(
        format="%(message)s",
        level=log_level,
        handlers=handlers,
    )
    logging.getLogger("httpx").setLevel(logging.WARNING)
    logging.getLogger("httpcore").setLevel(logging.WARNING)


def get_logger(name: str) -> structlog.BoundLogger:
    """Get a structured logger instance.

    Args:
        name: Logger name (typically __name__)

    Returns:
        Configured structlog logger
    """
    return structlog.get_logger(name)


def setup_logging(log_level: str = "INFO", log_format: str = "json", log_file: Optional[str] = None) -> None:
    """Setup logging with specified format.

    Args:
        log_level: Log level (DEBUG, INFO, WARNING, ERROR)
        log_format: Output format ('json' or 'human')
        log_file: Optional log file path
    """
    level = getattr(logging, log_level.upper(), logging.INFO)

    if log_format == "human":
        # Human-readable console format
        processors = [
            structlog.stdlib.add_log_level,
            structlog.processors.TimeStamper(fmt="%Y-%m-%d %H:%M:%S"),
            structlog.dev.ConsoleRenderer(),
        ]
    else:
        # JSON format (default)
        processors = [
            structlog.stdlib.add_log_level,
            structlog.stdlib.add_logger_name,
            structlog.processors.TimeStamper(fmt="iso"),
            structlog.processors.StackInfoRenderer(),
            structlog.processors.format_exc_info,
            structlog.processors.UnicodeDecoder(),
            structlog.processors.JSONRenderer(),
        ]

    structlog.configure(
        processors=processors,
        context_class=dict,
        logger_factory=structlog.stdlib.LoggerFactory(),
        wrapper_class=structlog.stdlib.BoundLogger,
        cache_logger_on_first_use=True,
    )

    handlers = [logging.StreamHandler(ensure_utf8_stream(sys.stdout))]
    if log_file:
        handlers.append(logging.FileHandler(log_file, encoding="utf-8"))

    logging.basicConfig(
        format="%(message)s",
        level=level,
        handlers=handlers,
        force=True,
    )
    logging.getLogger("httpx").setLevel(logging.WARNING)
    logging.getLogger("httpcore").setLevel(logging.WARNING)
