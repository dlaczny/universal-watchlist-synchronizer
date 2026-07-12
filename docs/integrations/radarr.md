---
type: Integration
title: Radarr
description: Live movie automation destination with ordinary ownership protection and exact watched cleanup authorization.
tags:
  - radarr
  - worker
  - movies
timestamp: 2026-07-11T00:00:00Z
version: 0.3.0
---

# Purpose

Radarr downloads and monitors backend-eligible movies that are unavailable on
configured owned services.

# Production Boundary

The worker reads all live Radarr movies and compares them with the complete
backend movie snapshot. It adds an eligible missing movie and records ownership,
or adopts an already-present desired movie. It preserves every unrelated
unmanaged movie.

An ordinary worker-owned movie no longer eligible for Radarr is removed only
when Radarr reports no downloaded file, with `delete_files=false`. Ordinary
downloaded rows remain `downloaded_file_requires_manual_review`.

A current published Letterboxd watched event is the only exception. It may
remove any exact-TMDB Radarr match, including a downloaded or pre-ownership row,
with `delete_files=true`. The decision must carry
`authorization=letterboxd_watched`, a non-empty lifecycle event ID, and pass the
separate `MOVIE_SYNC_ALLOW_WATCHED_FILE_DELETION` gate. The executor validates
the same invariant before calling Radarr.

Every successful complete Radarr collection updates the SQLite observation
ledger. The first collection is baseline-only. A never-Letterboxd row that
later disappears manually is not deleted by the worker; its observation may
authorize only exact Plex-watchlist cleanup.

# Configuration

```text
RADARR_URL
RADARR_API_KEY
RADARR_QUALITY_PROFILE_ID
RADARR_ROOT_FOLDER
```

Legacy deletion variables still exist in compatibility code but do not change
the production plan-and-apply authorization rules.

# Links

- [VOD Filter Worker](../systems/vod_filter_worker.md)
- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
