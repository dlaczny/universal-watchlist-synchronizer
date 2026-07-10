---
type: Report
title: OKF Cleanup Report
description: Cleanup report for converting repository knowledge to an OKF-first docs bundle.
tags:
  - report
  - okf
  - cleanup
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Summary

The repository was reorganized around an OKF-first knowledge layer in `docs/`.
The previous non-OKF documentation was removed after useful content was moved
into OKF concepts.

# Converted Into OKF

| Source | OKF concepts |
|---|---|
| `AGENTS.md` | Agent onboarding, project, boundaries, decisions, validation. |
| `README.md` | Project, local development, architecture, systems. |
| `CICD.md` | Deployment tooling, homelab CD runbook, homelab CD decision. |
| `knowledge-dump.md` | Migration context and monorepo migration decision. |
| `okf.md` | OKF rules and knowledge system concepts. |
| `workers/vod-filter/README.md` | VOD Filter worker and operations runbook. |
| `deploy/portainer/README.md` | Deployment tooling and homelab CD boundary. |
| Previous `docs/*.md` | Project, architecture, API, integrations, Android, roadmap concepts. |
| Previous `docs/superpowers/` | Current requirements summarized into system, decision, backlog, and runbook concepts. |

# Deleted From Active Knowledge

| Deleted path | Replacement |
|---|---|
| `CICD.md` | [Deployment Tooling](../systems/deployment_tooling.md), [Homelab CD](../runbooks/homelab_cd.md) |
| `knowledge-dump.md` | [Migration Context](../references/migration_context.md) |
| `okf.md` | [OKF Rules](../references/okf_rules.md), [Knowledge System](../architecture/knowledge_system.md) |
| `workers/vod-filter/README.md` | [VOD Filter Worker](../systems/vod_filter_worker.md), [VOD Filter Operations](../runbooks/vod_filter_operations.md) |
| `deploy/portainer/README.md` | [Deployment Tooling](../systems/deployment_tooling.md), [Homelab CD Boundary](../decisions/homelab_cd_boundary.md) |
| `old-docs/` | Deleted after the useful archive content was represented in OKF. |

# Preserved

- `.github/workflows/backend-ci.yml` and `.github/workflows/android-ci.yml`.
- Source code and behavior were not changed for the OKF conversion.

# Remaining Cleanup Candidates

- Add machine-readable JSON schemas only if API contract validation becomes a
  real workflow need.
- Split `android/app/src/main/java/com/watchlist/tv/MainActivity.java` into
  smaller components as tracked in [Roadmap](../backlog/roadmap.md).
