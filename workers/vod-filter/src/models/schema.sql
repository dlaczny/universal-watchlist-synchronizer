-- VOD Filter Database Schema
-- SQLite database for caching movie metadata, VOD availability, and sync state

-- Movies table
CREATE TABLE IF NOT EXISTS movies (
    tmdb_id INTEGER PRIMARY KEY,
    imdb_id TEXT,
    letterboxd_id TEXT,
    title TEXT NOT NULL,
    year INTEGER NOT NULL,
    poster_url TEXT,
    last_updated TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    CHECK (tmdb_id > 0),
    CHECK (year >= 1800 AND year <= 2100),
    CHECK (length(title) > 0),
    CHECK (imdb_id IS NULL OR imdb_id GLOB 'tt[0-9][0-9][0-9][0-9][0-9][0-9][0-9]*')
);

-- VOD availability table
CREATE TABLE IF NOT EXISTS vod_availability (
    tmdb_id INTEGER NOT NULL,
    provider_id INTEGER NOT NULL,
    provider_name TEXT NOT NULL,
    region TEXT NOT NULL DEFAULT 'PL',
    availability_type TEXT NOT NULL,
    checked_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    PRIMARY KEY (tmdb_id, provider_id),
    FOREIGN KEY (tmdb_id) REFERENCES movies(tmdb_id) ON DELETE CASCADE,

    CHECK (provider_id > 0),
    CHECK (length(region) = 2),
    CHECK (availability_type IN ('flatrate', 'rent', 'buy', 'ads'))
);

-- Sync state table
CREATE TABLE IF NOT EXISTS sync_state (
    tmdb_id INTEGER PRIMARY KEY,
    on_letterboxd BOOLEAN NOT NULL DEFAULT 1,
    on_plex BOOLEAN NOT NULL DEFAULT 0,
    on_radarr BOOLEAN NOT NULL DEFAULT 0,
    vod_available BOOLEAN NOT NULL DEFAULT 0,
    last_synced TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    sync_error TEXT,

    FOREIGN KEY (tmdb_id) REFERENCES movies(tmdb_id) ON DELETE CASCADE,

    CHECK (sync_error IS NULL OR length(sync_error) <= 500)
);

-- Streaming provider configuration
CREATE TABLE IF NOT EXISTS streaming_providers (
    provider_id INTEGER PRIMARY KEY,
    provider_name TEXT NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT 1,
    region TEXT NOT NULL DEFAULT 'PL',

    CHECK (provider_id > 0),
    CHECK (length(region) = 2)
);

-- Plex watchlist cache (reduces API calls)
CREATE TABLE IF NOT EXISTS plex_watchlist_cache (
    tmdb_id INTEGER PRIMARY KEY,
    title TEXT NOT NULL,
    year INTEGER NOT NULL,
    cached_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    CHECK (tmdb_id > 0),
    CHECK (year >= 1800 AND year <= 2100)
);

-- Plex library cache (reduces API calls)
CREATE TABLE IF NOT EXISTS plex_library_cache (
    tmdb_id INTEGER PRIMARY KEY,
    title TEXT NOT NULL,
    year INTEGER NOT NULL,
    cached_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    CHECK (tmdb_id > 0),
    CHECK (year >= 1800 AND year <= 2100)
);

-- Cache metadata (track last full refresh)
CREATE TABLE IF NOT EXISTS cache_metadata (
    cache_name TEXT PRIMARY KEY,
    last_refresh TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    ttl_minutes INTEGER NOT NULL DEFAULT 30,

    CHECK (ttl_minutes > 0)
);

-- Persistent run history for operational audit/debugging
CREATE TABLE IF NOT EXISTS run_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    workflow TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'running',
    dry_run BOOLEAN NOT NULL DEFAULT 0,
    trigger TEXT NOT NULL DEFAULT 'manual',
    started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    finished_at TIMESTAMP,
    exit_code INTEGER,
    summary TEXT,
    error TEXT,

    CHECK (length(workflow) > 0),
    CHECK (status IN ('running', 'success', 'failed', 'error', 'interrupted'))
);

-- Destination rows explicitly managed by the plan-and-apply movie worker.
CREATE TABLE IF NOT EXISTS managed_destinations (
    destination TEXT NOT NULL,
    tmdb_id INTEGER NOT NULL,
    first_managed_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_managed_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_action TEXT NOT NULL,

    PRIMARY KEY (destination, tmdb_id),
    CHECK (destination IN ('radarr', 'plex_watchlist')),
    CHECK (tmdb_id > 0),
    CHECK (length(last_action) > 0)
);

-- Durable Radarr inventory observations used to distinguish manual removals.
CREATE TABLE IF NOT EXISTS radarr_observation_state (
    singleton_id INTEGER PRIMARY KEY CHECK (singleton_id = 1),
    initialized BOOLEAN NOT NULL DEFAULT 0,
    updated_at TIMESTAMP
);

CREATE TABLE IF NOT EXISTS radarr_observations (
    tmdb_id INTEGER PRIMARY KEY,
    title TEXT NOT NULL,
    year INTEGER,
    present BOOLEAN NOT NULL,
    disappearance_cause TEXT,
    source_event_id TEXT,
    first_seen_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_seen_at TIMESTAMP,
    last_transition_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CHECK (tmdb_id > 0),
    CHECK (length(title) > 0),
    CHECK (present IN (0, 1)),
    CHECK (
        disappearance_cause IS NULL
        OR disappearance_cause IN ('manual', 'active_source', 'watched')
    )
);

CREATE TABLE IF NOT EXISTS movie_cleanup_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    authorization TEXT NOT NULL,
    authorization_event_id TEXT,
    destination TEXT NOT NULL,
    tmdb_id INTEGER NOT NULL,
    delete_files BOOLEAN NOT NULL DEFAULT 0,
    status TEXT NOT NULL,
    error TEXT,
    attempted_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CHECK (tmdb_id > 0),
    CHECK (destination IN ('radarr', 'plex_watchlist')),
    CHECK (delete_files IN (0, 1))
);

-- Indexes for performance
CREATE INDEX IF NOT EXISTS idx_vod_availability_checked_at ON vod_availability(checked_at);
CREATE INDEX IF NOT EXISTS idx_sync_state_last_synced ON sync_state(last_synced);
CREATE INDEX IF NOT EXISTS idx_movies_last_updated ON movies(last_updated);
CREATE INDEX IF NOT EXISTS idx_sync_state_on_letterboxd ON sync_state(on_letterboxd);
CREATE INDEX IF NOT EXISTS idx_sync_state_vod_available ON sync_state(vod_available);
CREATE INDEX IF NOT EXISTS idx_plex_watchlist_cache_cached_at ON plex_watchlist_cache(cached_at);
CREATE INDEX IF NOT EXISTS idx_plex_library_cache_cached_at ON plex_library_cache(cached_at);
CREATE INDEX IF NOT EXISTS idx_run_history_started_at ON run_history(started_at);
CREATE INDEX IF NOT EXISTS idx_run_history_workflow ON run_history(workflow);
CREATE INDEX IF NOT EXISTS idx_managed_destinations_tmdb_id
    ON managed_destinations(tmdb_id);
CREATE INDEX IF NOT EXISTS idx_radarr_observations_present
    ON radarr_observations(present);
CREATE INDEX IF NOT EXISTS idx_movie_cleanup_history_tmdb_id
    ON movie_cleanup_history(tmdb_id, attempted_at);
