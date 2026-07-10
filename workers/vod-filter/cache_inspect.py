"""Inspect VOD Filter SQLite cache and operational state."""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).parent))

from dotenv import load_dotenv

from src.services.cache_service import CacheService
from src.utils.logging import setup_logging


def build_payload(cache: CacheService, section: str = "all", limit: int = 20) -> dict[str, Any]:
    """Build a serializable cache inspection payload."""
    payload: dict[str, Any] = {}

    if section in {"all", "stats"}:
        payload["stats"] = cache.get_database_stats()
        payload["providers"] = cache.get_all_providers()
        payload["cache_metadata"] = cache.get_cache_metadata()
        payload["recent_runs"] = cache.get_recent_runs(limit=limit)

    if section in {"all", "vod"}:
        payload["vod_availability"] = cache.get_recent_vod_availability(limit=limit)

    if section in {"all", "sync"}:
        payload["sync_state"] = cache.get_all_sync_states()[:limit]

    if section in {"all", "plex"}:
        payload["plex_watchlist_cache"] = cache.get_plex_watchlist_cache()[:limit]
        payload["plex_library_cache"] = cache.get_plex_library_cache()[:limit]

    if section in {"all", "runs"}:
        payload["recent_runs"] = cache.get_recent_runs(limit=limit)

    return payload


def render_text(payload: dict[str, Any]) -> str:
    """Render inspection payload as readable text."""
    lines: list[str] = []
    for section, value in payload.items():
        lines.append(f"## {section}")
        if isinstance(value, dict):
            for key, item in value.items():
                lines.append(f"{key}: {item}")
        elif isinstance(value, list):
            if not value:
                lines.append("(empty)")
            for row in value:
                lines.append(json.dumps(row, ensure_ascii=False, sort_keys=True))
        else:
            lines.append(str(value))
        lines.append("")
    return "\n".join(lines)


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Inspect VOD Filter cache/database state")
    parser.add_argument(
        "--database",
        default=os.getenv("DATABASE_PATH", "data/vod-filter.db"),
        help="SQLite database path",
    )
    parser.add_argument(
        "--section",
        choices=["all", "stats", "vod", "sync", "plex", "runs"],
        default="all",
        help="Cache section to inspect",
    )
    parser.add_argument("--limit", type=int, default=20, help="Maximum rows per section")
    parser.add_argument("--json", action="store_true", help="Output JSON")
    return parser.parse_args(argv)


def main(argv=None) -> int:
    load_dotenv()
    args = parse_args(argv)
    setup_logging(log_level="ERROR" if args.json else os.getenv("LOG_LEVEL", "INFO"))
    cache = CacheService(database_path=args.database)
    payload = build_payload(cache, section=args.section, limit=args.limit)

    if args.json:
        print(json.dumps(payload, indent=2, ensure_ascii=False, sort_keys=True))
    else:
        print(render_text(payload))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
