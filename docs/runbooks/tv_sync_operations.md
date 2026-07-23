---
type: Runbook
title: TV Sync Operations
description: Safe inspection, read synchronization, key-ring rotation, and recovery procedures for the Phase 1 TV read model.
tags:
  - tv
  - trakt
  - operations
timestamp: 2026-07-19T00:00:00Z
version: 1.0.0
---

# Safety Boundary

Phase 1 has no TV mutation operation. Do not use this runbook to write Trakt
history, query Plex episode history, change Sonarr, or alter the Plex
watchlist. Keep all six TV switches false:

```text
TRAKT_HISTORY_SYNC_APPLY=false
TV_SYNC_APPLY=false
TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false
TV_SYNC_ALLOW_SEASON_FILE_DELETION=false
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false
```

# Inspect And Synchronize

Use the host-local sync key without printing it. All sync `POST` routes and all
integration routes require `X-Watchlist-Sync-Key`, including protected
`GET /api/integrations/trakt/status`. Browse/detail reads, read-only exports,
and public `GET /api/sync/status` do not require that key.

```bash
curl -fsS -H "X-Watchlist-Sync-Key: $SYNC_KEY" \
  http://127.0.0.1:5000/api/integrations/trakt/status
curl -fsS -X POST -H "X-Watchlist-Sync-Key: $SYNC_KEY" \
  http://127.0.0.1:5000/api/sync/tv
curl -fsS http://127.0.0.1:5000/api/sync/status
curl -fsS http://127.0.0.1:5000/api/export/tv/sync-state
```

`POST /api/sync/tv` creates a scheduled full read generation only when the
connection is usable. Confirm `mutationCapable` remains false and both health
reasons remain present. A missing export is `404`, which means no TV generation
has been published; it is not permission to bootstrap destinations.

If Trakt rate limits the request, the endpoint returns `503` with
`code=trakt_rate_limited`. Honor its `Retry-After` header when present; otherwise
wait for the upstream limit to clear and retry. The previous published
generation, if any, remains unchanged.

Provider `unknown` means no usable PL observation was obtained. `stale` means
the last usable observation was retained after a provider failure. Neither
state means unavailable and neither should trigger a destination decision.

# Device Authorization And Recovery

Start a device authorization only when the status is disconnected,
`refresh_required`, revoked, or unreadable. Complete the user interaction at
the returned verification URL before its expiry. Never place the returned user
code in a ticket, report, or shell history.

```bash
curl -fsS -X POST -H "X-Watchlist-Sync-Key: $SYNC_KEY" \
  http://127.0.0.1:5000/api/integrations/trakt/device/start
```

For an unreadable connection, preserve `/opt/watchlist-prod/data/backend/data-protection-keys`
for recovery and audit. Verify the configured `DataProtection__KeyRingPath`,
application name, ownership, and mounted volume. If the original keys cannot be
restored, disconnect the unusable singleton through the protected endpoint and
complete a new device authorization; do not delete the old key-ring as a first
response.

# Key-Ring Rotation

Back up the key-ring using the host's protected backup process, retain old keys
through the maximum token lifetime plus rollback window, and restart one API
instance after validating it can read connection status. ASP.NET Data Protection
adds new keys automatically; replacing the whole directory or application name
is a destructive credential migration and requires a supervised reconnection.

# Evidence

Record commands, redacted response metadata, generation ID, status timestamps,
and health/mutation fields in the [TV Integration Rollout](../reports/tv_integration_rollout.md).
Do not claim a production deployment from local or CI output.

# Links

- [Trakt Integration](../integrations/trakt.md)
- [Homelab CD](homelab_cd.md)
- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
