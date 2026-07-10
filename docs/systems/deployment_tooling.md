---
type: System
title: Deployment Tooling
description: GitHub Actions, Docker Compose, local deploy scripts, and homelab deployment boundaries.
tags:
  - deployment
  - ci
  - homelab
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

Deployment tooling supports a local-only homelab setup. GitHub Actions validate
builds and tests. A trusted Proxmox VM handles backend deployment and Android TV
APK installation.

# Files

| Path | Role |
|---|---|
| `.github/workflows/backend-ci.yml` | Builds and tests backend changes. |
| `.github/workflows/android-ci.yml` | Builds Android debug APK and unit tests. |
| `.github/workflows/validate-okf.yml` | Validates OKF docs. |
| `backend/src/Watchlist.Api/Dockerfile` | Backend container image. |
| `deploy/backend/compose.yaml` | Backend Docker Compose deployment using VM-local env file. |
| `deploy/backend/watchlist-backend.env.example` | Non-secret example backend env file. |
| `deploy/local-cd/systemd/` | Systemd service and timer for local deployment. |
| `scripts/deploy-watchlist-local.sh` | Pulls trusted main, deploys backend, builds Android APK, installs to TV. |

# Rules

- Do not commit backend secrets, Plex tokens, TMDB tokens, MongoDB credentials,
  signing keys, or local APKs.
- Do not deploy pull request code automatically.
- Keep runtime secrets on the trusted local deployment host.
- Use MongoDB Atlas in deployment because the Proxmox VM CPU did not expose AVX
  needed by modern MongoDB containers.
- Portainer is deferred; use systemd on the VM first.

# Links

- Runbook: [Homelab CD](../runbooks/homelab_cd.md)
- Decision: [Homelab CD Boundary](../decisions/homelab_cd_boundary.md)

