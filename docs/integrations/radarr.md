---
type: Integration
title: Radarr
description: Movie automation target managed by the VOD Filter worker, not by Android-facing backend flows.
tags:
  - radarr
  - worker
  - cleanup
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Purpose

Radarr is a local automation target for unavailable watchlist movies. It is
managed by the Python VOD Filter worker.

# Boundary

The Android app and backend read APIs do not perform Radarr mutations. The
worker consumes backend export data and performs Radarr side effects locally.

# Configuration

The worker reads Radarr settings from environment variables such as:

```text
RADARR_URL
RADARR_API_KEY
RADARR_QUALITY_PROFILE_ID
RADARR_ROOT_FOLDER
RADARR_DELETE_FILES_ON_REMOVAL
RADARR_REMOVE_WHEN_VOD_AVAILABLE
RADARR_DELETE_FILES_WHEN_VOD_AVAILABLE
```

# Safety

- Dry-run reports should be reviewed before production cleanup.
- File deletion is intentionally separate from Radarr removal.
- Cache misses are skipped when cleanup cannot safely identify an item.

# Links

- Worker: [VOD Filter Worker](../systems/vod_filter_worker.md)
- Export API: [Export Endpoints](../apis/export_endpoints.md)

