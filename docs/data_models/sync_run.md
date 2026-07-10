---
type: Data Model
title: Sync Run
description: Sync status and history data used to explain freshness, failures, and partial results.
tags:
  - data-model
  - sync
  - mongodb
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

Sync run data records integration freshness, status, errors, timestamps, and
counts. The backend uses `sync_runs`; the worker also maintains local SQLite run
history under `workers/vod-filter/data/`.

# Backend Usage

`GET /api/sync/status` reads latest backend sync status from MongoDB.
`POST /api/sync/availability/refresh` uses the latest successful Plex movie
sync timestamp to decide whether cached availability is fresh.

# Worker Usage

`run_all_syncs.py` wraps cleanup, main sync, and library sync in local run
history entries. Interrupted or failed runs should be marked explicitly so
future reviews can distinguish application failures from command timeouts.

# Links

- Sync pipeline: [Sync Pipeline](../architecture/sync_pipeline.md)
- VOD Filter worker: [VOD Filter Worker](../systems/vod_filter_worker.md)

