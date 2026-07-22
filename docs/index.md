---
okf_version: "0.1"
---

# Watchlist App OKF Bundle

This directory is the active knowledge base for the repository. It uses Open
Knowledge Format conventions: every non-reserved Markdown file in this bundle is
an OKF concept document with YAML frontmatter and a non-empty `type` field.

## Sections

- [Project](projects/index.md)
- [Architecture](architecture/index.md)
- [Systems](systems/index.md)
- [APIs](apis/index.md)
- [Integrations](integrations/index.md)
- [Data Models](data_models/index.md)
- [Runbooks](runbooks/index.md)
- [Decisions](decisions/index.md)
- [Backlog](backlog/index.md)
- [References](references/index.md)
- [Reports](reports/index.md)

## Agent Entry Points

- Start with [Agent Onboarding](runbooks/agent_onboarding.md).
- Use [System Boundaries](architecture/system_boundaries.md) before changing cross-component behavior.
- Use [Production Movie Sync](architecture/movie_sync_production.md) before changing movie sync behavior.
- Use [Backend API](apis/backend_api.md) before changing HTTP contracts.
- Use [Validation](runbooks/validation.md) before claiming work is complete.

For Phase 1 TV work, use [TV Sync Read Model](architecture/tv_sync_read_model.md),
[Trakt](integrations/trakt.md), and [TV Sync Operations](runbooks/tv_sync_operations.md).

