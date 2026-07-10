from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.utils import file_lock


def test_windows_stale_lock_with_dead_pid_is_removed(
    monkeypatch,
    tmp_path: Path,
) -> None:
    lock_path = tmp_path / "vod-filter.db.lock"
    lock_path.write_text("999999", encoding="utf-8")

    monkeypatch.setattr(file_lock.sys, "platform", "win32")
    monkeypatch.setattr(file_lock, "_process_exists", lambda pid: False)

    with file_lock.FileLock(lock_path, timeout=0):
        assert lock_path.read_text(encoding="utf-8") == str(file_lock.os.getpid())

    assert not lock_path.exists()

