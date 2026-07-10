---
type: Architecture
title: Knowledge System
description: Rules for maintaining this repository as an OKF-first knowledge system.
tags:
  - okf
  - documentation
  - agents
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Overview

`docs/` is the OKF bundle root for this repository. Active project knowledge
belongs here as linked concept documents.

Top-level files such as `README.md` and `AGENTS.md` are intentionally thin
entry points. They should point into OKF instead of duplicating architecture,
API, integration, product, or runbook details.

# Rules

- Every non-reserved Markdown file under `docs/` must have YAML frontmatter.
- Every concept document must have a non-empty `type`.
- `docs/index.md` is reserved for navigation and must declare `okf_version`.
- Subdirectory `index.md` files should not have frontmatter.
- `docs/log.md` is the OKF changelog and should use ISO date headings.
- Preserve unique knowledge by moving it into an OKF concept.
- Remove non-OKF documentation after its useful content is represented in OKF.

# Avoid

- Repeating detailed rules in `AGENTS.md`, `README.md`, nested READMEs, or tool
  config files.
- Keeping active documentation outside the OKF bundle.
- Reintroducing historical task plans as active docs unless they have been
  summarized as current OKF concepts.

# Links

- Cleanup report: [OKF Cleanup Report](../reports/okf_cleanup_report.md)
- OKF rules reference: [OKF Rules](../references/okf_rules.md)
