"""File locking to prevent concurrent execution.

Uses cross-platform file locking to ensure only one instance runs at a time.
"""

import os
import sys
import time
from pathlib import Path
from typing import Optional

import structlog

logger = structlog.get_logger(__name__)


class LockAcquisitionError(Exception):
    """Raised when file lock cannot be acquired."""

    pass


def _windows_process_exists(pid: int) -> bool:
    """Return whether a process ID exists on Windows."""
    try:
        import ctypes
        from ctypes import wintypes

        process_query_limited_information = 0x1000
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.OpenProcess.argtypes = [
            wintypes.DWORD,
            wintypes.BOOL,
            wintypes.DWORD,
        ]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL

        handle = kernel32.OpenProcess(
            process_query_limited_information,
            False,
            pid,
        )
        if handle:
            kernel32.CloseHandle(handle)
            return True

        # Access denied still means the PID exists.
        return ctypes.get_last_error() == 5
    except Exception as e:
        logger.warning(
            "Could not check Windows process status",
            pid=pid,
            error=str(e),
        )
        return True


def _process_exists(pid: int) -> bool:
    """Return whether a process ID is currently running."""
    if pid <= 0:
        return False

    if sys.platform == "win32":
        return _windows_process_exists(pid)

    try:
        # Send signal 0 to check if process exists (doesn't actually send signal)
        os.kill(pid, 0)
        return True
    except OSError:
        return False


class FileLock:
    """Context manager for file-based locking."""

    def __init__(self, lock_file: Path, timeout: int = 10):
        """Initialize file lock.

        Args:
            lock_file: Path to lock file
            timeout: Maximum seconds to wait for lock acquisition
        """
        self.lock_file = lock_file
        self.timeout = timeout
        self.lock_acquired = False

    def __enter__(self) -> "FileLock":
        """Acquire lock on entry.

        Returns:
            Self

        Raises:
            RuntimeError: If lock cannot be acquired within timeout
        """
        start_time = time.time()
        while True:
            try:
                # Try to create lock file exclusively
                fd = os.open(self.lock_file, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.write(fd, str(os.getpid()).encode())
                os.close(fd)
                self.lock_acquired = True
                logger.info("Lock acquired", lock_file=str(self.lock_file))
                return self
            except FileExistsError:
                # Lock file exists - check if process is still running
                if self._is_stale_lock():
                    logger.warning(
                        "Removing stale lock file",
                        lock_file=str(self.lock_file),
                    )
                    self._remove_lock()
                    continue

                # Check timeout
                if time.time() - start_time > self.timeout:
                    raise LockAcquisitionError(
                        f"Could not acquire lock within {self.timeout} seconds. "
                        f"Another instance may be running. Lock file: {self.lock_file}"
                    )

                logger.debug(
                    "Waiting for lock",
                    lock_file=str(self.lock_file),
                    elapsed=int(time.time() - start_time),
                )
                time.sleep(1)

    def __exit__(self, exc_type, exc_val, exc_tb) -> None:
        """Release lock on exit."""
        if self.lock_acquired:
            self._remove_lock()
            logger.info("Lock released", lock_file=str(self.lock_file))

    def _is_stale_lock(self) -> bool:
        """Check if lock file is stale (process no longer running).

        Returns:
            True if lock is stale, False otherwise
        """
        try:
            with open(self.lock_file, "r") as f:
                pid = int(f.read().strip())

            return not _process_exists(pid)
        except (ValueError, FileNotFoundError, IOError):
            return True  # Invalid or missing lock file

    def _remove_lock(self) -> None:
        """Remove lock file."""
        try:
            os.remove(self.lock_file)
        except FileNotFoundError:
            pass


def get_lock(lock_file: Optional[Path] = None, timeout: int = 10) -> FileLock:
    """Get a file lock instance.

    Args:
        lock_file: Path to lock file (default: ./data/vod-filter.lock)
        timeout: Maximum seconds to wait for lock acquisition

    Returns:
        FileLock instance
    """
    if lock_file is None:
        lock_file = Path("./data/vod-filter.lock")

    # Ensure directory exists
    lock_file.parent.mkdir(parents=True, exist_ok=True)

    return FileLock(lock_file, timeout)


def exclusive_execution(lock_file: Optional[str] = None, timeout: int = 30) -> FileLock:
    """Context manager for exclusive execution.

    Args:
        lock_file: Path to lock file (optional)
        timeout: Maximum seconds to wait for lock

    Returns:
        FileLock context manager

    Raises:
        LockAcquisitionError: If lock cannot be acquired
    """
    lock_path = Path(lock_file) if lock_file else Path("./data/vod-filter.lock")
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    return FileLock(lock_path, timeout)
