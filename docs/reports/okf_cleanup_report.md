---
type: Report
title: OKF Cleanup Report
description: What repository knowledge was moved, removed, preserved, and left as a deliberate cleanup candidate.
tags:
  - report
  - okf
  - cleanup
timestamp: 2026-07-12T00:00:00Z
version: 0.4.0
---

# Result

`docs/` is the active OKF knowledge layer. `AGENTS.md` and `README.md` remain
thin entry pointers. No `old-docs/` directory remains.

The 2026-07-11 pass removed stale descriptions of the overlapping
`run_all_syncs.py` production path and Android APK deployment. Current concepts
now document the implemented backend snapshot, worker plan/apply engine,
secret-free CI, and exact-SHA homelab release.

The 2026-07-12 lifecycle pass replaced blanket file-preservation statements
with one narrow implemented exception: an exact published Letterboxd watched
event may remove its Radarr row with files behind a separate default-off gate.
OKF also records retained lifecycle documents, publish-last source manifests,
manual Radarr observations, Plex-watchlist-only cleanup, audit rows, and the
no-backfill boundary.

# Moved Into OKF

| Previous source | Current concepts |
|---|---|
| `knowledge-dump.md` | Migration context and monorepo decision. |
| `okf.md` | OKF rules and knowledge-system architecture. |
| `CICD.md`, Portainer notes | Deployment system, homelab runbook, and deployment decision. |
| Worker README and legacy docs | Worker system, operations runbook, integrations, and production architecture. |
| Previous free-form `docs/` and `docs/superpowers/` | Project, architecture, systems, APIs, data models, decisions, backlog, and runbooks. |
| Production movie implementation | Movie architecture, API contracts, worker/deployment systems, operations, validation, and roadmap. |
| Watched lifecycle design and implementation | Active/watched manifests, export contract, observation ledger, authorization rules, feature gate, audit/report fields, and rollout runbook. |

# Deleted Or Replaced

| Path or knowledge | Disposition |
|---|---|
| `old-docs/` | Deleted after unique content was represented in OKF. |
| Root `knowledge-dump.md`, `okf.md`, `CICD.md` | Deleted after conversion. |
| Worker and deployment README duplicates | Deleted after conversion. |
| `.github/workflows/backend-ci.yml` | Replaced by `Movie CI`. |
| `.github/workflows/validate-okf.yml` | Replaced by the OKF job in `Movie CI`. |
| Legacy worker flow as production documentation | Replaced by `sync_movies.py` plan-and-apply concepts. |
| Android APK deployment as active CD documentation | Replaced by backend/movie-worker-only homelab delivery; Android is on hold. |

# Preserved Deliberately

- Android system and decision concepts because they contain unique read-only
  client constraints, clearly marked on hold.
- Migration context because it explains provenance without claiming current
  behavior.
- Legacy `/opt/watchlist-app` deployment files and old deploy script until the
  new release completes its rollback-observation period.
- Compatibility backend endpoints and direct-source worker code, documented as
  outside the production path.
- The watched lifecycle design and implementation plan as execution history
  until supervised rollout and its observation period are complete.

# Remaining Cleanup Candidates

- Remove `deploy/backend/` and `scripts/deploy-watchlist-local.sh` after the
  production rollback-observation period.
- Retire compatibility worker scripts after operating history confirms no
  manual dependency on them.
- Archive or remove completed production and watched-lifecycle implementation
  plans after rollout history is captured in durable runbooks and logs.
- Resolve the three stable movie-identity skips listed in the roadmap and make
  Plex rows without TMDB GUIDs visible in reconciliation reports.
- Add disk/failed-run alerting before removing the legacy rollback deployment.
- Add schemas only when automated API-contract tooling requires them.

# Links

- [Roadmap](../backlog/roadmap.md)
- [Knowledge System](../architecture/knowledge_system.md)
