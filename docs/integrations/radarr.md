---
type: Integration
title: Radarr
description: Live movie automation destination managed through worker ownership and no-file-deletion policy.
tags:
  - radarr
  - worker
  - movies
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Purpose

Radarr downloads and monitors backend-eligible movies that are unavailable on
configured owned services.

# Production Boundary

The worker reads all live Radarr movies and compares them with the complete
backend movie snapshot. It adds an eligible missing movie and records ownership,
or adopts an already-present desired movie. It preserves every unrelated
unmanaged movie.

A worker-owned movie no longer eligible for Radarr is removed only when Radarr
reports no downloaded file. The executor always passes `delete_files=false`.
Rows with downloaded files are reported as
`downloaded_file_requires_manual_review` and remain unchanged.

# Configuration

```text
RADARR_URL
RADARR_API_KEY
RADARR_QUALITY_PROFILE_ID
RADARR_ROOT_FOLDER
```

Legacy deletion variables still exist in compatibility code but do not change
the production executor's hardcoded file-preservation behavior.

# Links

- [VOD Filter Worker](../systems/vod_filter_worker.md)
- [VOD Filter Operations](../runbooks/vod_filter_operations.md)
