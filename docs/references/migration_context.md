---
type: Reference
title: Migration Context
description: Summary of the VOD Filter migration into watchlist-app and related verification.
tags:
  - migration
  - worker
  - provenance
timestamp: 2026-07-08T00:00:00Z
version: 0.1.0
---

# Context

The Python VOD Filter worker was copied from:

```text
C:\NextCloud\plex-radarr-letterboxed-tmdb\vod-filter
```

to:

```text
C:\Users\laczn\Documents\watchlist-app\workers\vod-filter
```

The old repo is now reference/backup. Implementation should continue in
`watchlist-app`.

# Migrated Content

- Worker source code.
- Worker tests under `workers/vod-filter/tests/vod_filter`.
- Runtime `.env` and `data/vod-filter.db` for local cutover, both ignored by git.

Generated artifacts such as virtualenvs, build output, pytest cache, and
`__pycache__` directories were removed during migration.

# Verification Recorded Before OKF Conversion

Worker:

```powershell
python -m pytest tests\vod_filter -q
```

Recorded result: `38 passed`.

Backend:

```powershell
dotnet build backend\src\Watchlist.Api\Watchlist.Api.csproj
dotnet test backend\tests\Watchlist.Api.Tests\Watchlist.Api.Tests.csproj --no-restore --no-build
dotnet test backend\tests\Watchlist.Application.Tests\Watchlist.Application.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName!~Mongo"
```

Recorded results: build passed, API tests passed, Application tests excluding
Mongo passed.

# Known Dirty Worktree Context

Before the OKF conversion, the worktree already contained unrelated Android,
backend retry/combined-sync, worker, and documentation changes. Preserve user
work and avoid reverting unrelated files.

# Links

- Decision: [Monorepo Migration](../decisions/monorepo_migration.md)
- Worker: [VOD Filter Worker](../systems/vod_filter_worker.md)

