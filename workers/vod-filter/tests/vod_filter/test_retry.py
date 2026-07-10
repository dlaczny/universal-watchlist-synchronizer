from __future__ import annotations

import sys
from pathlib import Path

import pytest


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.utils.retry import is_plex_rate_limit_error, retry_on_plex_rate_limit


def test_plex_rate_limit_retry_uses_exponential_waits(monkeypatch: pytest.MonkeyPatch):
    sleeps = []
    calls = {"count": 0}

    monkeypatch.setattr("src.utils.retry.time.sleep", lambda seconds: sleeps.append(seconds))

    @retry_on_plex_rate_limit(max_attempts=4, min_wait=10, max_wait=120, multiplier=2)
    def flaky():
        calls["count"] += 1
        if calls["count"] < 4:
            raise RuntimeError("429 too_many_requests rate limit exceeded")
        return "ok"

    assert flaky() == "ok"
    assert sleeps == [10, 20, 40]


def test_plex_rate_limit_retry_honors_retry_after(monkeypatch: pytest.MonkeyPatch):
    sleeps = []
    calls = {"count": 0}

    monkeypatch.setattr("src.utils.retry.time.sleep", lambda seconds: sleeps.append(seconds))

    @retry_on_plex_rate_limit(max_attempts=2, min_wait=10, max_wait=120)
    def flaky():
        calls["count"] += 1
        if calls["count"] == 1:
            raise RuntimeError("429 too_many_requests Retry-After: 75")
        return "ok"

    assert flaky() == "ok"
    assert sleeps == [75]


def test_plex_rate_limit_detector_matches_common_plex_429_text():
    assert is_plex_rate_limit_error(RuntimeError("(429) too_many_requests")) is True
    assert is_plex_rate_limit_error(RuntimeError("Rate limit exceeded!")) is True
    assert is_plex_rate_limit_error(RuntimeError("not found")) is False

