---
type: Runbook
title: VOD Filter Operations
description: Operational commands and safety checks for the Python VOD Filter worker.
tags:
  - worker
  - dry-run
  - radarr
  - plex
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Preferred Source Mode

Run from `workers/vod-filter`:

```powershell
$env:WATCHLIST_SOURCE="watchlist_app"
$env:WATCHLIST_APP_URL="http://localhost:5000"
$env:WATCHLIST_APP_SYNC_FIRST="false"
python run_all_syncs.py --dry-run --quiet
```

# Useful Commands

```powershell
python src/main.py --dry-run
python src/main.py --dry-run --log-level WARNING
python run_all_syncs.py --dry-run
python run_all_syncs.py --dry-run --quiet
python cleanup_removed_movies.py --dry-run --report-file data/reports/cleanup-review.md
python cache_inspect.py --section stats
python cache_inspect.py --section runs --json
python compare_watchlist_sources.py --skip-watchlist-app-sync
python validate_providers.py
python continuous_sync.py --continuous --interval 3600
```

# Dry-Run Review

Review generated reports under:

```text
workers/vod-filter/data/reports
```

Skipped cache misses are not deleted. They indicate cleanup could not safely
identify a title/details record from cache.

# Production Guardrails

- Inspect `.env` before production runs.
- Confirm `WATCHLIST_SOURCE=watchlist_app` and the intended
  `WATCHLIST_APP_URL`.
- Keep `RADARR_DELETE_FILES_WHEN_VOD_AVAILABLE=false` unless explicitly
  choosing file deletion.
- Treat interrupted Codex command timeouts as verification limits until the run
  history and reports show an application failure.

# Links

- Worker system: [VOD Filter Worker](../systems/vod_filter_worker.md)
- Migration context: [Migration Context](../references/migration_context.md)

