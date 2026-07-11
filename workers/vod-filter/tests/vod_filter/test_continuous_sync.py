from __future__ import annotations

import importlib
import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))


def test_continuous_run_uses_single_movie_sync_entrypoint(monkeypatch):
    continuous_sync = importlib.reload(importlib.import_module("continuous_sync"))
    sync_movies = importlib.import_module("sync_movies")
    run_all_syncs = importlib.import_module("run_all_syncs")
    calls = []
    monkeypatch.setattr(
        sync_movies,
        "main",
        lambda argv: calls.append(("movie_sync", argv)) or 0,
    )
    monkeypatch.setattr(
        run_all_syncs,
        "main",
        lambda argv: calls.append(("legacy", argv)) or 9,
    )

    exit_code = continuous_sync.run_sync()

    assert exit_code == 0
    assert calls == [("movie_sync", [])]
