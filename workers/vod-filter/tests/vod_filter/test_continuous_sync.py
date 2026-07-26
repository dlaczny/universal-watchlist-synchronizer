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


class ManualClock:
    def __init__(self) -> None:
        self.now = 0.0

    def monotonic(self) -> float:
        return self.now

    def sleep(self, seconds: float) -> None:
        self.now += seconds


def test_continuous_mode_runs_movie_and_tv_on_independent_deadlines() -> None:
    continuous_sync = importlib.reload(importlib.import_module("continuous_sync"))
    clock = ManualClock()
    calls: list[tuple[str, int]] = []

    continuous_sync.run_scheduled_syncs(
        movie_interval=3600,
        tv_interval=900,
        tv_enabled=True,
        run_movie=lambda: calls.append(("movie", int(clock.now))) or 0,
        run_tv=lambda: calls.append(("tv", int(clock.now))) or 0,
        monotonic=clock.monotonic,
        sleep=clock.sleep,
        stop_after=3601,
    )

    assert calls == [
        ("movie", 0),
        ("tv", 0),
        ("tv", 900),
        ("tv", 1800),
        ("tv", 2700),
        ("movie", 3600),
        ("tv", 3600),
    ]


def test_disabled_tv_never_runs_or_imports_tv_entrypoint(monkeypatch) -> None:
    continuous_sync = importlib.reload(importlib.import_module("continuous_sync"))
    clock = ManualClock()
    calls: list[str] = []

    continuous_sync.run_scheduled_syncs(
        movie_interval=10,
        tv_interval=1,
        tv_enabled=False,
        run_movie=lambda: calls.append("movie") or 0,
        run_tv=lambda: (_ for _ in ()).throw(AssertionError("TV must not run")),
        monotonic=clock.monotonic,
        sleep=clock.sleep,
        stop_after=11,
    )

    assert calls == ["movie", "movie"]


def test_scheduler_keeps_tv_deadline_when_movie_run_raises() -> None:
    continuous_sync = importlib.reload(importlib.import_module("continuous_sync"))
    clock = ManualClock()
    calls: list[tuple[str, int]] = []

    def movie() -> int:
        calls.append(("movie", int(clock.now)))
        if len([name for name, _ in calls if name == "movie"]) == 1:
            raise RuntimeError("movie unavailable")
        return 0

    continuous_sync.run_scheduled_syncs(
        movie_interval=10,
        tv_interval=3,
        tv_enabled=True,
        run_movie=movie,
        run_tv=lambda: calls.append(("tv", int(clock.now))) or 0,
        monotonic=clock.monotonic,
        sleep=clock.sleep,
        stop_after=11,
    )

    assert calls == [("movie", 0), ("tv", 0), ("tv", 3), ("tv", 6), ("tv", 9), ("movie", 10)]
