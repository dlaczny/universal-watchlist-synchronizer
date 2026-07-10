---
type: Runbook
title: Homelab CD
description: Local continuous delivery flow for backend deployment and Android TV APK installation.
tags:
  - deployment
  - homelab
  - android-tv
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

The recommended CD path uses a trusted Proxmox VM. GitHub Actions validate
builds; the VM pulls trusted `main`, deploys the backend, health-checks it,
builds the Android APK with LAN config, and installs it to the TV over ADB.

# Flow

```text
Push to main
  -> GitHub Actions validate backend and Android
  -> Proxmox VM pulls trusted main
  -> VM deploys backend with Docker Compose and local secrets
  -> VM health-checks backend
  -> VM builds Android debug APK with LAN backend URL
  -> VM installs APK to Android TV with adb
```

# Current Facts

| Item | Value |
|---|---|
| Deployer VM observed IP | `192.168.50.163` |
| Backend URL for Android TV | `http://192.168.50.163:5000` |
| Backend deploy compose path | `deploy/backend/compose.yaml` |
| Runtime database | MongoDB Atlas Free Tier |
| Android package | `com.watchlist.tv` |
| Android SDK path on VM | `/opt/android-sdk` |

# Stop Conditions

Stop and redesign if:

- Android needs direct Plex, TMDB, Letterboxd, or MongoDB credentials.
- Deployment would run unreviewed pull request code.
- A public APK would include sensitive config.
- The TV cannot keep stable ADB authorization.
- The backend cannot be reached from the TV over LAN.

# Links

- Deployment system: [Deployment Tooling](../systems/deployment_tooling.md)
- Decision: [Homelab CD Boundary](../decisions/homelab_cd_boundary.md)

