---
type: Decision
title: Homelab CD Boundary
description: Public CI validates exact commits while a trusted local host builds and deploys with host-only credentials.
tags:
  - decision
  - deployment
  - homelab
timestamp: 2026-07-11T00:00:00Z
version: 0.2.0
---

# Decision

Use secret-free GitHub Actions to validate code and a trusted homelab host to
build and run backend and movie-worker containers. Deploy only a `main` push
whose exact SHA has a completed successful `Movie CI` run.

# Rationale

The repository is public and the integrations are LAN/personal services.
GitHub needs no MongoDB, TMDB, Plex, Radarr, backend sync, SSH, or deployment
credential. A local exact-SHA gate gives auditable validation without exporting
those secrets.

# Consequences

- Runtime env files remain mode `0600` under `/opt/watchlist-prod/config`.
- The deploy service runs as `watchlist` and maintains a dedicated clean
  checkout separate from `/opt/watchlist-app`.
- Cutover requires backend and worker health and automatically restores the
  prior release on failure.
- Android TV deployment is on hold and is not part of this release path.
- Portainer remains unnecessary while the systemd flow is sufficient.
