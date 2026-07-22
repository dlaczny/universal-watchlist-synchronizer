---
type: System
title: Deployment Tooling
description: Secret-free GitHub validation and exact-SHA local deployment of backend, protected TV read model, and movie-worker containers.
tags:
  - deployment
  - ci
  - homelab
timestamp: 2026-07-12T00:00:00Z
version: 0.3.0
---

# GitHub Validation

`.github/workflows/movie-ci.yml` runs for pull requests, `main` pushes, and
manual dispatch. It has read-only repository permission and these jobs:

| Job | Validation |
|---|---|
| `okf` | OKF validator and deployment-tool tests. |
| `backend` | Release restore/build and all 187 backend tests with MongoDB 8. |
| `worker` | Python 3.11 dependencies, all worker tests, and production compile check. |
| `secret-scan` | Redacted Gitleaks history and checked-out-tree scans. |
| `containers` | Backend and worker production image builds after all prior jobs pass. |

Actions and the MongoDB/Gitleaks images are pinned to immutable revisions or
digests. No production credentials enter GitHub. Android work is deferred and
is not a release prerequisite. TV read-model validation includes key-ring
mount/configuration and checks that every TV mutation gate remains false.

# Production Files

| Path | Role |
|---|---|
| `deploy/production/compose.yaml` | Commit-tagged backend and worker services, health dependency, non-root/read-only controls, log rotation. |
| `deploy/production/*.env.example` | Non-secret configuration contracts. |
| `scripts/check-movie-ci.py` | Queries the public Actions API for a successful push run at one exact SHA. |
| `scripts/deploy-movie-sync.sh` | Locked checkout/build/cutover/state/rollback transaction. |
| `deploy/local-cd/systemd/` | Five-minute timer and hardened `watchlist` oneshot service. |

# Host Layout

| Path | Purpose |
|---|---|
| `/opt/watchlist-prod/repository` | Dedicated detached production checkout. |
| `/opt/watchlist-prod/config` | Mode-`0600` backend, worker, and deploy environment files. |
| `/opt/watchlist-prod/data/worker` | SQLite, reports, and heartbeat. |
| `/opt/watchlist-prod/data/backend/data-protection-keys` | Persistent protected Trakt key-ring mount. |
| `/opt/watchlist-prod/state/last-successful.sha` | Atomic rollback release state. |
| `/opt/watchlist-prod/state/previous-successful.sha` | Prior healthy release retained for audit/manual rollback. |
| `/opt/watchlist-prod/deployer` | Stable scripts replaced only after a validated successful release. |

The legacy dirty `/opt/watchlist-app` checkout is not reset or reused. Its old
backend Compose deployment is retained only as first-cutover rollback until the
new path has proved stable.

# Release Transaction

The systemd service takes `flock`, resolves `origin/main`, requires a completed
successful `Movie CI` push run for that exact SHA, checks it out detached,
validates Compose, prunes stale build cache, and builds SHA-tagged images. It
then stops the legacy backend when present, starts and verifies the new API
before starting the worker, and requires both backend HTTP health and a healthy
worker heartbeat. This avoids treating a transient API-container health state
during process startup as a release failure.

At cutover, the deployer stops the previous worker and removes its persisted
heartbeat. A release is accepted only after the new worker writes a nonempty
heartbeat and its container reports healthy. Rollback performs the same reset,
so neither a new release nor a restored release can inherit health from another
image.

Failure after cutover restores the previous production SHA or, on first
cutover, the legacy backend. Success atomically records the SHA, updates the
stable deployer, retains the current and prior image sets, and prunes older
release images.

# Secret Boundary

- Public Git and GitHub Actions contain examples and placeholders only.
- Backend, worker, MongoDB, Radarr, Plex, TMDB, and sync keys live only on the
  trusted host or ignored developer files.
- The API image keeps its published runtime files world-readable so Compose can
  run it as the host service UID; credentials remain in the separately mounted,
  mode-`0600` host environment file.
- The public Actions API requires no token for the polling rate used here.
- Scripts never enable shell tracing or print environment-file contents.
- The Trakt client secret and persisted key-ring stay host-local. Compose
  hard-overrides all six TV mutation flags to `false`, even if an env file is
  edited accidentally. A Phase 1 deployment is a read-model deployment, not a
  Sonarr/Plex/Trakt-history rollout.

# Links

- [Homelab CD](../runbooks/homelab_cd.md)
- [Production Movie Sync](../architecture/movie_sync_production.md)
- [TV Integration Rollout](../reports/tv_integration_rollout.md)
