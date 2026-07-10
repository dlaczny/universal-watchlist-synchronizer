---
type: Decision
title: OKF-First Docs
description: Active documentation lives in docs as OKF concept documents, with thin top-level entry points.
tags:
  - decision
  - okf
  - documentation
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Decision

Use `docs/` as the OKF bundle root. Keep top-level `README.md` and `AGENTS.md`
thin. Remove pre-OKF docs after their useful content is represented in OKF.

# Rationale

The repository had useful knowledge duplicated across README, AGENTS, docs,
worker README, deployment notes, and historical plans. OKF gives agents and
humans one active knowledge layer with explicit concept links.

# Consequences

- New active documentation belongs in `docs/` and must validate as OKF.
- Non-OKF active docs should be removed or converted.
- Legacy Markdown should not remain as an active parallel knowledge system.
