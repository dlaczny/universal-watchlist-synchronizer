---
type: Runbook
title: Homelab CD
description: Provision, operate, inspect, and recover the exact-SHA movie deployment on the trusted host.
tags:
  - deployment
  - homelab
  - rollback
timestamp: 2026-07-12T00:00:00Z
version: 0.2.1
---

# Boundary

The host at `192.168.50.163` deploys only backend and movie-worker containers.
Android TV deployment is on hold. GitHub contains no runtime credentials and
the host accepts a release only after `Movie CI` succeeds for the exact `main`
push SHA.

# Host Layout

| Path | Content |
|---|---|
| `/opt/watchlist-prod/repository` | Clean detached production checkout. |
| `/opt/watchlist-prod/config/backend.env` | Backend credentials and integration settings. |
| `/opt/watchlist-prod/config/worker.env` | Worker credentials, policy, and interval. |
| `/opt/watchlist-prod/config/deploy.env` | Public repository/deployment settings only. |
| `/opt/watchlist-prod/data/worker` | SQLite, reports, and heartbeat. |
| `/opt/watchlist-prod/state/last-successful.sha` | Current rollback reference. |
| `/opt/watchlist-prod/state/previous-successful.sha` | Prior successful release SHA after a later cutover. |
| `/opt/watchlist-prod/deployer` | Stable validated deploy scripts. |

All three env files use mode `0600`; directories use mode `0700` and are owned
by `watchlist`. `/opt/watchlist-app` is preserved unchanged as the legacy
checkout and first-cutover backend rollback.

# Provisioning

1. Create the host directories as root and assign them to `watchlist`.
2. Copy `deploy/production/*.env.example` and
   `deploy/local-cd/watchlist-deploy.env.example` to the host-local names.
3. Replace placeholders locally without printing values. Generate one random
   sync key and use the same value for backend `Sync__ApiKey` and worker
   `WATCHLIST_APP_SYNC_KEY`.
4. Set `WATCHLIST_SOURCE=watchlist_app`,
   `WATCHLIST_APP_SYNC_FIRST=true`, `WATCHLIST_APP_SYNC_TIMEOUT_SECONDS=900`,
   and `MOVIE_SYNC_APPLY=false` for the first release.
5. Install the validated `deploy-movie-sync.sh` and `check-movie-ci.py` under
   `/opt/watchlist-prod/deployer`.
6. Install the service and timer from `deploy/local-cd/systemd/` under
   `/etc/systemd/system`, then reload systemd.

The service runs as `watchlist`, stores Docker state under
`/opt/watchlist-prod/.docker`, and may write only `/opt/watchlist-prod` through
its systemd filesystem sandbox. The deployer exports that service account's
numeric UID/GID to Compose so the worker can write the private bind-mounted data
directory without changing it to world-writable permissions.

The default deploy health gate waits up to 240 five-second attempts. This
20-minute window is deliberately longer than the 15-minute full-sync timeout;
ordinary backend reads still fail after 30 seconds by default.

# First Reconciliation Deployment

```bash
sudo systemctl start watchlist-deploy.service
sudo systemctl status watchlist-deploy.service --no-pager
sudo journalctl -u watchlist-deploy.service -n 100 --no-pager
docker ps --filter name=watchlist-prod
curl -fsS http://127.0.0.1:5000/healthz
```

Inspect the newest files under
`/opt/watchlist-prod/data/worker/reports/`. Apply must remain disabled until the
checklist in [VOD Filter Operations](vod_filter_operations.md) passes.

# Enable Apply

Edit only `MOVIE_SYNC_APPLY` in the host worker env, then recreate the worker:

```bash
cd /opt/watchlist-prod/repository
export WATCHLIST_CONFIG_DIR=/opt/watchlist-prod/config
export WATCHLIST_DATA_DIR=/opt/watchlist-prod/data
export WATCHLIST_RELEASE="$(cat /opt/watchlist-prod/state/last-successful.sha)"
docker compose -f deploy/production/compose.yaml up -d --no-build --force-recreate movie-sync-worker
```

Review the first apply report, Radarr state, Plex watchlist, and worker
heartbeat. Automatic behavior never deletes downloaded files or Plex library
media.

# Timer And Status

```bash
sudo systemctl enable --now watchlist-deploy.timer
systemctl list-timers watchlist-deploy.timer
sudo journalctl -u watchlist-deploy.service --since today --no-pager
docker inspect --format '{{.State.Health.Status}}' watchlist-prod-api watchlist-prod-worker
```

The timer polls every five minutes with up to 60 seconds randomized delay.
Pending, failed, or missing CI runs are skipped. API lookup failure marks the
service failed without changing the running release.

# Rollback

Build failure occurs before cutover and leaves the current service running.
Failure after cutover automatically starts the previous SHA-tagged production
images. On the first cutover, it instead restarts the legacy backend Compose
project. The state file changes only after both new containers are healthy.

Do not reset or clean `/opt/watchlist-app`. Remove the legacy deployment only
after the production path has completed an agreed rollback-observation period.

# Links

- [Deployment Tooling](../systems/deployment_tooling.md)
- [VOD Filter Operations](vod_filter_operations.md)
- [Validation](validation.md)
