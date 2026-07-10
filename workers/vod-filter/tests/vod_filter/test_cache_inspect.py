from __future__ import annotations

import json
import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from cache_inspect import build_payload, main
from src.services.cache_service import CacheService


def test_cache_inspect_builds_stats_payload(tmp_path: Path):
    cache = CacheService(database_path=str(tmp_path / "vod-filter.db"))
    cache.upsert_provider(119, "Amazon Prime Video", enabled=True, region="PL")
    run_id = cache.start_run("main", dry_run=True, trigger="test")
    cache.finish_run(run_id, status="success", exit_code=0)

    payload = build_payload(cache, section="stats", limit=5)

    assert payload["stats"]["movies"] == 0
    assert payload["providers"][0]["provider_id"] == 119
    assert payload["recent_runs"][0]["workflow"] == "main"


def test_cache_inspect_json_payload_is_serializable(tmp_path: Path):
    cache = CacheService(database_path=str(tmp_path / "vod-filter.db"))

    payload = build_payload(cache, section="stats", limit=5)

    assert json.loads(json.dumps(payload))["stats"]["movies"] == 0


def test_cache_inspect_json_output_is_clean(capsys, tmp_path: Path):
    database = tmp_path / "vod-filter.db"

    exit_code = main(["--database", str(database), "--section", "stats", "--json"])

    output = capsys.readouterr().out
    assert exit_code == 0
    assert json.loads(output)["stats"]["movies"] == 0

