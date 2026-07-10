from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from compare_watchlist_sources import (
    Candidate,
    compare_candidates,
    render_report,
    write_report,
)


def test_compare_candidates_groups_overlap_and_source_only_items():
    python_candidates = [
        Candidate(source="python", title="Same", year=2020, tmdb_id=101, imdb_id="tt101"),
        Candidate(source="python", title="Python Only", year=2021, tmdb_id=202),
        Candidate(source="python", title="Different Id", year=2022, tmdb_id=303),
    ]
    watchlist_candidates = [
        Candidate(source="watchlist_app", title="Same", year=2020, tmdb_id=101, imdb_id="tt101"),
        Candidate(source="watchlist_app", title="App Only", year=2023, tmdb_id=404),
        Candidate(source="watchlist_app", title="Different Id", year=2022, tmdb_id=999),
    ]

    result = compare_candidates(python_candidates, watchlist_candidates)

    assert [item.title for item in result.in_both] == ["Same"]
    assert [item.title for item in result.only_python] == ["Python Only"]
    assert [item.title for item in result.only_watchlist_app] == ["App Only"]
    assert [(left.tmdb_id, right.tmdb_id) for left, right in result.same_title_different_ids] == [
        (303, 999)
    ]


def test_render_report_includes_counts_and_tmdb_links():
    result = compare_candidates(
        [Candidate(source="python", title="Same", year=2020, tmdb_id=101)],
        [Candidate(source="watchlist_app", title="Same", year=2020, tmdb_id=101)],
    )

    content = render_report(result)

    assert "# Watchlist source comparison report" in content
    assert "Python direct candidates: 1" in content
    assert "watchlist-app candidates: 1" in content
    assert "[TMDB 101](https://www.themoviedb.org/movie/101)" in content


def test_write_report_creates_markdown_file(tmp_path: Path):
    result = compare_candidates([], [])

    output_path = write_report(result, tmp_path / "compare.md")

    assert output_path.exists()
    assert output_path.read_text(encoding="utf-8").startswith(
        "# Watchlist source comparison report"
    )

