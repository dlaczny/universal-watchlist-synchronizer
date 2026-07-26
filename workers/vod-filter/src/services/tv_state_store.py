"""SQLite persistence isolated from the established movie schema."""

from __future__ import annotations

import sqlite3
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator


class TvStateStore:
    """Persist TV workflow run, ownership, action audit, and singleton lease state."""

    def __init__(self, database_path: str | Path = "data/vod-filter.db") -> None:
        self.database_path = Path(database_path)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        migration = Path(__file__).parent.parent / "models" / "migrations" / "0001_tv_sync.sql"
        with self.connection() as connection:
            connection.executescript(migration.read_text(encoding="utf-8"))

    @contextmanager
    def connection(self) -> Iterator[sqlite3.Connection]:
        connection = sqlite3.connect(self.database_path)
        connection.row_factory = sqlite3.Row
        try:
            yield connection
            connection.commit()
        finally:
            connection.close()

    def start_run(self, generation_id: str) -> int:
        with self.connection() as connection:
            cursor = connection.execute(
                "INSERT INTO tv_sync_runs (generation_id) VALUES (?)", (generation_id,)
            )
            return int(cursor.lastrowid)

    def finish_run(self, run_id: int, status: str) -> None:
        if status not in {"completed", "failed"}:
            raise ValueError("TV run status must be completed or failed")
        with self.connection() as connection:
            connection.execute(
                "UPDATE tv_sync_runs SET status = ?, finished_at = CURRENT_TIMESTAMP WHERE id = ?",
                (status, run_id),
            )

    def get_runs(self) -> tuple[tuple[int, str, str], ...]:
        with self.connection() as connection:
            rows = connection.execute(
                "SELECT id, generation_id, status FROM tv_sync_runs ORDER BY id"
            ).fetchall()
        return tuple((int(row["id"]), str(row["generation_id"]), str(row["status"])) for row in rows)

    def record_ownership(self, destination: str, tvdb_id: int, origin: str) -> None:
        if destination not in {"sonarr", "plex_watchlist"}:
            raise ValueError("TV destination is invalid")
        if not isinstance(tvdb_id, int) or isinstance(tvdb_id, bool) or tvdb_id <= 0:
            raise ValueError("TVDB ID must be positive")
        if origin not in {"worker", "manual"}:
            raise ValueError("TV ownership origin must be worker or manual")
        with self.connection() as connection:
            connection.execute(
                """
                INSERT INTO tv_destination_ownership (destination, tvdb_id, origin)
                VALUES (?, ?, ?)
                ON CONFLICT(destination, tvdb_id) DO UPDATE SET origin = excluded.origin,
                    recorded_at = CURRENT_TIMESTAMP
                """,
                (destination, tvdb_id, origin),
            )

    def get_ownership(self) -> tuple[dict[str, object], ...]:
        with self.connection() as connection:
            rows = connection.execute(
                "SELECT destination, tvdb_id, origin FROM tv_destination_ownership "
                "ORDER BY destination, tvdb_id"
            ).fetchall()
        return tuple(dict(row) for row in rows)

    def record_action(self, action_id: str, action: str, status: str) -> None:
        if status not in {"planned", "completed", "failed", "skipped"}:
            raise ValueError("TV action status is invalid")
        with self.connection() as connection:
            connection.execute(
                """
                INSERT INTO tv_destination_actions (action_id, action, status)
                VALUES (?, ?, ?)
                ON CONFLICT(action_id) DO UPDATE SET action = excluded.action,
                    status = excluded.status, recorded_at = CURRENT_TIMESTAMP
                """,
                (action_id, action, status),
            )

    def get_actions(self) -> tuple[dict[str, str], ...]:
        with self.connection() as connection:
            rows = connection.execute(
                "SELECT action_id, action, status FROM tv_destination_actions ORDER BY action_id"
            ).fetchall()
        return tuple(dict(row) for row in rows)  # type: ignore[return-value]

    def acquire_lease(self, owner: str) -> bool:
        if not isinstance(owner, str) or not owner.strip():
            raise ValueError("TV lease owner is required")
        with self.connection() as connection:
            try:
                connection.execute(
                    "INSERT INTO tv_destination_leases (lease_key, owner) VALUES ('tv_destination_sync', ?)",
                    (owner,),
                )
                return True
            except sqlite3.IntegrityError:
                return False

    def release_lease(self, owner: str) -> None:
        with self.connection() as connection:
            connection.execute(
                "DELETE FROM tv_destination_leases WHERE lease_key = 'tv_destination_sync' AND owner = ?",
                (owner,),
            )
