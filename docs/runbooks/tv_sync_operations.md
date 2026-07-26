---
type: Runbook
title: TV Sync Operations
description: Safely inspect TV generations and stage the report-first reversible Sonarr and Plex Watchlist workflow.
tags:
  - tv
  - trakt
  - sonarr
  - plex
  - operations
timestamp: 2026-07-26T00:00:00Z
version: 1.1.0
---

# Safety Boundary

The backend remains the only Trakt and TMDB client. The TV worker consumes one
immutable schema-v2 export and independently collects Sonarr, Plex TV-library,
Plex Watchlist, and SQLite state before it plans. Do not call Trakt from the
worker or infer a destination action from a missing export: `404` means no
published generation, not an empty desired state.

Keep the committed and host defaults false:

```text
TV_SYNC_ENABLED=false
TV_SYNC_APPLY=false
TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false
TRAKT_HISTORY_SYNC_APPLY=false
TV_SYNC_ALLOW_SEASON_FILE_DELETION=false
TV_SYNC_ALLOW_TERMINAL_SERIES_DELETION=false
TV_SYNC_ALLOW_NO_RECYCLE_BIN_DELETE=false
```

`TV_SYNC_ENABLED=true` enables TV collection and reporting only.
`TV_SYNC_APPLY=true` is only a host gate; an action also requires an explicit
`sync_tv.py --apply`. Adoption additionally requires
`TV_SYNC_ADOPT_EXISTING_DESTINATIONS=true`. Never set a history or cleanup
switch true for this release.

Only the following destination transitions are in scope: exact-TVDB Sonarr
lookup/add, series and selected-season monitoring, selected-season aired and
unwatched episode search, and exact Plex Watchlist add/remove. Plex library
media, Plex episode history, Trakt history, movie state, Android, and Sonarr
file, season, or whole-series removal are prohibited.

# Inspect The Backend Generation

Use the host-local sync key without printing it. Sync and integration routes
require `X-Watchlist-Sync-Key`; browse, status, and exports do not.

```bash
curl -fsS -H "X-Watchlist-Sync-Key: $SYNC_KEY" \
  http://127.0.0.1:5000/api/integrations/trakt/status
curl -fsS -X POST -H "X-Watchlist-Sync-Key: $SYNC_KEY" \
  http://127.0.0.1:5000/api/sync/tv
curl -fsS http://127.0.0.1:5000/api/sync/status
curl -fsS http://127.0.0.1:5000/api/export/tv/sync-state
```

Confirm the export has `schemaVersion: "2"` and a
`destinationSync` object. `destinationSync.capable=false`, a stale generation,
any duplicate identity, or a collection error is a stop condition. The retained
`mutationCapable=false` value is expected and is not a reason to change a
history or cleanup flag.

TVDB is mandatory for Sonarr. Missing, nonpositive, or conflicting TVDB IDs
are report blockers, not candidates for title/year matching. Plex needs an
exact TVDB GUID, or an exact TMDB/IMDb GUID verified by the backend as the same
TVDB show. An unknown or ambiguous Plex row must be reported and preserved.

The worker selects regular seasons only: Season 1 for an unstarted show;
otherwise Trakt's next-episode season, or the next aired numbered season after
the latest fully watched season. Its selected season must have its own current
Poland provider observation. `unknown` and `stale` block a new Sonarr action;
only `confirmed_unavailable` permits a Sonarr add/monitor/search. Plex
Watchlist is desired when the selected season is `available` or an exact Plex
TV-library episode exists, and an exact Plex Watchlist row may be removed only
when neither fact is true.

# Reversible destination rollout

Perform each stage only after the previous stage's redacted report has been
reviewed by a human. Run these commands on the deployment host from the clean,
exact-SHA checkout. The examples update protected host configuration without
printing a credential; retain every history and cleanup setting above as
`false` throughout.

Set the shared Compose variables once for the commands below:

```bash
cd /opt/watchlist-prod/repository
export WATCHLIST_CONFIG_DIR=/opt/watchlist-prod/config
export WATCHLIST_DATA_DIR=/opt/watchlist-prod/data
export WATCHLIST_RELEASE="$(cat /opt/watchlist-prod/state/last-successful.sha)"
```

## 1. Deploy Disabled

Leave `TV_SYNC_ENABLED=false` in `/opt/watchlist-prod/config/worker.env`,
deploy the exact-SHA release, and verify the movie workflow remains healthy.
Do not record this as an enabled TV stage.

```bash
sudo systemctl start watchlist-deploy.service
sudo systemctl status watchlist-deploy.service --no-pager
docker inspect --format '{{.State.Health.Status}}' watchlist-prod-worker
```

## 2. Report-Only Collection

In the protected `worker.env`, set `TV_SYNC_ENABLED=true`, retain
`TV_SYNC_APPLY=false` and `TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false`, then
recreate the worker and run one explicit report-only pass:

```bash
docker compose -f deploy/production/compose.yaml up -d --no-build --force-recreate movie-sync-worker
docker compose -f deploy/production/compose.yaml exec -T movie-sync-worker \
  python sync_tv.py --once --report-dir /app/data/reports
ls -lt /opt/watchlist-prod/data/worker/reports
```

Review the newest JSON and Markdown reports before any configuration change.
They must show one generation ID, redacted counts and stable reasons, exact
identity outcomes, selected seasons, provider states, action counts, and no
collection/policy blocker other than `tv_apply_disabled`. A blocked, stale, or
uncertain plan is not eligible for the next stage.

## 3. Supervised Adoption

This stage records origin only; it must not change Sonarr monitoring or create
a destination row. After approving the report-only plan, set both
`TV_SYNC_APPLY=true` and `TV_SYNC_ADOPT_EXISTING_DESTINATIONS=true` in the
protected `worker.env`, recreate the worker, then make one supervised request:

```bash
docker compose -f deploy/production/compose.yaml up -d --no-build --force-recreate movie-sync-worker
docker compose -f deploy/production/compose.yaml exec -T movie-sync-worker \
  python sync_tv.py --once --apply --report-dir /app/data/reports
```

Review the resulting report and SQLite audit before proceeding. Each adopted
exact Sonarr row must be recorded with `manual` origin. Stop if an identity,
provider, collection, threshold, or policy blocker appears.

## 4. Supervised Reversible Apply And Convergence

After adoption review, set `TV_SYNC_ADOPT_EXISTING_DESTINATIONS=false` while
keeping `TV_SYNC_ENABLED=true` and `TV_SYNC_APPLY=true`. Recreate the worker,
run one supervised apply, and review its destination outcomes:

```bash
docker compose -f deploy/production/compose.yaml up -d --no-build --force-recreate movie-sync-worker
docker compose -f deploy/production/compose.yaml exec -T movie-sync-worker \
  python sync_tv.py --once --apply --report-dir /app/data/reports
```

Only after this review passes, run the same command a second time. The second
report should converge to `keep` or `skip` decisions. If it does not, leave
apply enabled only long enough to preserve evidence, return to report-only by
setting `TV_SYNC_APPLY=false`, and investigate from fresh collection rather
than replaying an old plan.

# Device Authorization And Key-Ring Recovery

Start device authorization only when the protected status says disconnected,
`refresh_required`, revoked, or unreadable. Complete the user interaction at
the returned verification URL before expiry. Never put a device/user code,
access/refresh token, client secret, protected ciphertext, sync key, Plex
token, or connection string in a ticket, report, shell history, or ledger.

```bash
curl -fsS -X POST -H "X-Watchlist-Sync-Key: $SYNC_KEY" \
  http://127.0.0.1:5000/api/integrations/trakt/device/start
```

For an unreadable connection, preserve
`/opt/watchlist-prod/data/backend/data-protection-keys` for recovery and
audit. Verify the configured `DataProtection__KeyRingPath`, application name,
ownership, and mounted volume. If the original keys cannot be restored,
disconnect the unusable singleton through the protected endpoint and complete a
new device authorization; do not delete the old key-ring as a first response.

Back up the key-ring through the host's protected backup process, retain old
keys through the maximum token lifetime plus rollback window, and restart one
API instance after validating it can read connection status. Replacing the
whole key-ring directory or application name is a destructive credential
migration and requires supervised reconnection.

# Evidence

For each actual stage, record only commands, redacted response metadata,
generation ID, timestamps, counts, stable reason codes, report status, and
destination outcome totals in the [TV Integration Rollout](../reports/tv_integration_rollout.md).
Do not claim deployment, adoption, apply, or convergence from local or CI
output. A successful destination rollout does not authorize any history,
cleanup, library, or file operation.

# Links

- [Export Endpoints](../apis/export_endpoints.md)
- [TV Sync Read Model](../architecture/tv_sync_read_model.md)
- [Homelab CD](homelab_cd.md)
