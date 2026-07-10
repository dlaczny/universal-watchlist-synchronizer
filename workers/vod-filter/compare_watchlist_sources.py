"""Compare direct Python watchlist candidates with watchlist-app export candidates.

This is a read-only migration tool. It does not call Radarr or Plex.
"""

from __future__ import annotations

import argparse
import os
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Iterable

sys.path.insert(0, str(Path(__file__).parent))

from dotenv import load_dotenv

from src.clients.watchlist_app_client import WatchlistAppClient
from src.config import load_config
from src.services.cache_service import CacheService
from src.services.letterboxd_service import LetterboxdService
from src.services.tmdb_service import TMDBService
from src.utils.logging import setup_logging


@dataclass(frozen=True)
class Candidate:
    source: str
    title: str
    year: int | None = None
    tmdb_id: int | None = None
    imdb_id: str | None = None
    letterboxd_id: str | None = None

    @property
    def identity_key(self) -> str:
        if self.tmdb_id is not None:
            return f"tmdb:{self.tmdb_id}"
        if self.imdb_id:
            return f"imdb:{self.imdb_id.lower()}"
        return f"title_year:{normalize_title(self.title)}:{self.year or ''}"

    @property
    def title_year_key(self) -> str:
        return f"{normalize_title(self.title)}:{self.year or ''}"


@dataclass(frozen=True)
class ComparisonResult:
    python_candidates: list[Candidate]
    watchlist_app_candidates: list[Candidate]
    in_both: list[Candidate]
    only_python: list[Candidate]
    only_watchlist_app: list[Candidate]
    same_title_different_ids: list[tuple[Candidate, Candidate]]
    missing_ids: list[Candidate]


def normalize_title(title: str) -> str:
    return " ".join("".join(ch.lower() if ch.isalnum() else " " for ch in title).split())


def compare_candidates(
    python_candidates: list[Candidate],
    watchlist_app_candidates: list[Candidate],
) -> ComparisonResult:
    python_by_key = {candidate.identity_key: candidate for candidate in python_candidates}
    watchlist_by_key = {
        candidate.identity_key: candidate for candidate in watchlist_app_candidates
    }

    shared_keys = set(python_by_key) & set(watchlist_by_key)
    in_both = sorted((python_by_key[key] for key in shared_keys), key=sort_key)
    only_python = sorted(
        (candidate for key, candidate in python_by_key.items() if key not in shared_keys),
        key=sort_key,
    )
    only_watchlist_app = sorted(
        (candidate for key, candidate in watchlist_by_key.items() if key not in shared_keys),
        key=sort_key,
    )

    watchlist_by_title_year = {
        candidate.title_year_key: candidate for candidate in only_watchlist_app
    }
    same_title_different_ids = []
    only_python_without_title_overlap = []
    title_overlap_keys = set()

    for candidate in only_python:
        other = watchlist_by_title_year.get(candidate.title_year_key)
        if other is not None:
            same_title_different_ids.append((candidate, other))
            title_overlap_keys.add(other.identity_key)
        else:
            only_python_without_title_overlap.append(candidate)

    only_watchlist_without_title_overlap = [
        candidate
        for candidate in only_watchlist_app
        if candidate.identity_key not in title_overlap_keys
    ]

    missing_ids = sorted(
        [
            candidate
            for candidate in [*python_candidates, *watchlist_app_candidates]
            if candidate.tmdb_id is None and not candidate.imdb_id
        ],
        key=sort_key,
    )

    return ComparisonResult(
        python_candidates=python_candidates,
        watchlist_app_candidates=watchlist_app_candidates,
        in_both=in_both,
        only_python=only_python_without_title_overlap,
        only_watchlist_app=only_watchlist_without_title_overlap,
        same_title_different_ids=sorted(
            same_title_different_ids,
            key=lambda pair: sort_key(pair[0]),
        ),
        missing_ids=missing_ids,
    )


def sort_key(candidate: Candidate) -> tuple[str, int]:
    return (candidate.title.lower(), candidate.year or 0)


def render_report(result: ComparisonResult) -> str:
    lines = [
        "# Watchlist source comparison report",
        "",
        f"Generated: {datetime.now().isoformat(timespec='seconds')}",
        "",
        "## Summary",
        "",
        f"- Python direct candidates: {len(result.python_candidates)}",
        f"- watchlist-app candidates: {len(result.watchlist_app_candidates)}",
        f"- In both sources: {len(result.in_both)}",
        f"- Only in Python direct mode: {len(result.only_python)}",
        f"- Only in watchlist-app mode: {len(result.only_watchlist_app)}",
        f"- Same title/year but different IDs: {len(result.same_title_different_ids)}",
        f"- Missing stable IDs: {len(result.missing_ids)}",
        "",
    ]

    append_candidate_section(lines, "In both sources", result.in_both)
    append_candidate_section(lines, "Only in Python direct mode", result.only_python)
    append_candidate_section(lines, "Only in watchlist-app mode", result.only_watchlist_app)

    lines.extend([f"## Same title/year but different IDs ({len(result.same_title_different_ids)})", ""])
    if result.same_title_different_ids:
        for left, right in result.same_title_different_ids:
            lines.append(f"- Python: {format_candidate(left)}")
            lines.append(f"  watchlist-app: {format_candidate(right)}")
    else:
        lines.append("- None")
    lines.append("")

    append_candidate_section(lines, "Missing stable IDs", result.missing_ids)

    return "\n".join(lines)


def append_candidate_section(lines: list[str], title: str, candidates: list[Candidate]) -> None:
    lines.extend([f"## {title} ({len(candidates)})", ""])
    if candidates:
        for candidate in candidates:
            lines.append(f"- {format_candidate(candidate)}")
    else:
        lines.append("- None")
    lines.append("")


def format_candidate(candidate: Candidate) -> str:
    year = f" ({candidate.year})" if candidate.year else ""
    identifiers = []
    if candidate.tmdb_id is not None:
        identifiers.append(
            f"[TMDB {candidate.tmdb_id}](https://www.themoviedb.org/movie/{candidate.tmdb_id})"
        )
    if candidate.imdb_id:
        identifiers.append(f"IMDb {candidate.imdb_id}")
    if candidate.letterboxd_id:
        identifiers.append(f"Letterboxd {candidate.letterboxd_id}")
    identifiers_text = f" — {', '.join(identifiers)}" if identifiers else ""
    return f"{candidate.title}{year}{identifiers_text}"


def write_report(result: ComparisonResult, path: Path) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(render_report(result), encoding="utf-8")
    return path


def candidates_from_movies(source: str, movies: Iterable) -> list[Candidate]:
    candidates = []
    for movie in movies:
        candidates.append(
            Candidate(
                source=source,
                title=getattr(movie, "title", None) or movie["title"],
                year=getattr(movie, "year", None) if not isinstance(movie, dict) else movie.get("year"),
                tmdb_id=getattr(movie, "tmdb_id", None) if not isinstance(movie, dict) else movie.get("tmdb_id"),
                imdb_id=getattr(movie, "imdb_id", None) if not isinstance(movie, dict) else movie.get("imdb_id"),
                letterboxd_id=(
                    getattr(movie, "letterboxd_id", None)
                    if not isinstance(movie, dict)
                    else movie.get("letterboxd_id")
                ),
            )
        )
    return candidates


def fetch_python_direct_candidates(config, cache_service: CacheService) -> list[Candidate]:
    letterboxd = LetterboxdService(config.letterboxd_username, cache_service)
    tmdb = TMDBService(
        api_key=config.tmdb_api_key,
        region=config.tmdb_region,
        cache_service=cache_service,
        cache_ttl_hours=config.cache_ttl_hours,
    )

    raw_watchlist = letterboxd.fetch_watchlist()
    resolved = [movie for movie in tmdb.batch_resolve_movies(raw_watchlist) if movie is not None]
    candidates = []

    for movie in resolved:
        is_available, _ = tmdb.check_vod_availability(
            tmdb_id=movie.tmdb_id,
            configured_providers=config.vod_providers,
            force_refresh=config.force_refresh,
        )
        if not is_available:
            candidates.append(movie)

    return candidates_from_movies("python", candidates)


def fetch_watchlist_app_candidates(config) -> list[Candidate]:
    client = WatchlistAppClient(config.watchlist_app_url)
    movies = client.fetch_radarr_movie_export(sync_first=config.watchlist_app_sync_first)
    return candidates_from_movies("watchlist_app", movies)


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Compare Python direct and watchlist-app sources")
    parser.add_argument("--config", default=None, help="Optional .env path")
    parser.add_argument("--report-file", default=None, help="Output Markdown report path")
    parser.add_argument(
        "--skip-watchlist-app-sync",
        action="store_true",
        help="Do not call watchlist-app POST /api/sync/all even if configured",
    )
    return parser.parse_args(argv)


def main(argv=None) -> int:
    load_dotenv()
    args = parse_args(argv)
    setup_logging(log_level=os.getenv("LOG_LEVEL", "INFO"), log_format="human")
    config = load_config(args.config)

    if not config.watchlist_app_url:
        print("WATCHLIST_APP_URL is required for source comparison.", file=sys.stderr)
        return 1

    if args.skip_watchlist_app_sync:
        config.watchlist_app_sync_first = False

    cache_service = CacheService(database_path=str(config.database_path))
    python_candidates = fetch_python_direct_candidates(config, cache_service)
    watchlist_candidates = fetch_watchlist_app_candidates(config)
    result = compare_candidates(python_candidates, watchlist_candidates)

    report_path = (
        Path(args.report_file)
        if args.report_file
        else Path("data/reports")
        / f"watchlist-source-compare-{datetime.now().strftime('%Y%m%d-%H%M%S')}.md"
    )
    written = write_report(result, report_path)

    print(f"Comparison report written: {written}")
    print(
        "Summary: "
        f"both={len(result.in_both)} "
        f"only_python={len(result.only_python)} "
        f"only_watchlist_app={len(result.only_watchlist_app)} "
        f"different_ids={len(result.same_title_different_ids)} "
        f"missing_ids={len(result.missing_ids)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
