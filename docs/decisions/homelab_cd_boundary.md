---
type: Decision
title: Homelab CD Boundary
description: Public CI validates code while a trusted local VM performs personal deployment.
tags:
  - decision
  - deployment
  - homelab
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Decision

Use GitHub Actions for validation and a trusted Proxmox VM for local deployment.
Do not deploy pull request code or store backend secrets in GitHub for this
local-only setup.

# Rationale

The Android app bakes the backend URL into the APK, and backend runtime secrets
belong on the local deployment host. A public APK or public CI secret flow is
unnecessary for a personal local project.

# Consequences

- VM-local `.env` files contain real backend secrets.
- The VM builds the personal Android APK locally.
- Portainer is deferred until systemd deployment works.

