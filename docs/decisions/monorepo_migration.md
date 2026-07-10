---
type: Decision
title: Monorepo Migration
description: Implementation continues in watchlist-app and the old VOD Filter repository is reference-only.
tags:
  - decision
  - migration
  - worker
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Decision

Continue implementation in:

```text
C:\Users\laczn\Documents\watchlist-app
```

Treat the old repository as reference/backup:

```text
C:\NextCloud\plex-radarr-letterboxed-tmdb
```

# Rationale

The .NET backend, Android app, worker, deployment tooling, and OKF knowledge
should live together so boundaries and contracts stay synchronized.

# Consequences

- `workers/vod-filter/` contains the migrated Python worker.
- The old repo should not receive new features except for final cutover
  reference needs.
- Runtime worker `.env` and SQLite data are local-only and ignored by git.

# Links

- Migration context: [Migration Context](../references/migration_context.md)

