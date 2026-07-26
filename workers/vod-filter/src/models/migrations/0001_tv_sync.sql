-- Isolated state for the reversible TV destination workflow.  Movie tables stay
-- in models/schema.sql and are deliberately not referenced here.
CREATE TABLE IF NOT EXISTS tv_sync_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    generation_id TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'running',
    started_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    finished_at TIMESTAMP,
    CHECK (length(generation_id) > 0),
    CHECK (status IN ('running', 'completed', 'failed'))
);

CREATE TABLE IF NOT EXISTS tv_destination_ownership (
    destination TEXT NOT NULL,
    tvdb_id INTEGER NOT NULL,
    origin TEXT NOT NULL,
    recorded_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (destination, tvdb_id),
    CHECK (destination IN ('sonarr', 'plex_watchlist')),
    CHECK (tvdb_id > 0),
    CHECK (origin IN ('worker', 'manual'))
);

CREATE TABLE IF NOT EXISTS tv_destination_actions (
    action_id TEXT PRIMARY KEY,
    action TEXT NOT NULL,
    status TEXT NOT NULL,
    recorded_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK (length(action_id) > 0),
    CHECK (length(action) > 0),
    CHECK (status IN ('planned', 'completed', 'failed', 'skipped'))
);

CREATE TABLE IF NOT EXISTS tv_destination_leases (
    lease_key TEXT PRIMARY KEY,
    owner TEXT NOT NULL,
    acquired_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK (lease_key = 'tv_destination_sync'),
    CHECK (length(owner) > 0)
);

CREATE INDEX IF NOT EXISTS idx_tv_destination_actions_recorded_at
    ON tv_destination_actions(recorded_at);
