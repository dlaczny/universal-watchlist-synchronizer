# MongoDB Read Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the backend's in-memory seeded read model with MongoDB persistence while keeping the Android TV API contract stable.

**Architecture:** Add MongoDB-backed repositories behind the existing application read boundary, a sync-status read boundary, and a startup bootstrap service that inserts deterministic records only into empty collections. Keep API tests deterministic with test service overrides and translate MongoDB dependency failures to HTTP `503`.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, MongoDB.Driver, Docker Compose, xUnit, FluentAssertions.

---

### Task 1: Local MongoDB Runtime

**Files:**
- Create: `compose.yaml`
- Modify: `backend/src/Watchlist.Api/appsettings.json`
- Modify: `docs/integrations.md`

- [ ] Add a `mongo` service using the official MongoDB image, publish port `27017`, and persist `/data/db` in a named volume.
- [ ] Add `MongoDb.ConnectionString`, `MongoDb.DatabaseName`, `MongoDb.WatchlistItemsCollectionName`, and `MongoDb.SyncRunsCollectionName` configuration values.
- [ ] Run `docker compose up -d mongo`.
- [ ] Verify with `docker compose ps` and `docker compose exec mongo mongosh --quiet --eval "db.runCommand({ ping: 1 }).ok"`.
- [ ] Commit with `chore: add local mongodb runtime`.

### Task 2: MongoDB Watchlist Repository

**Files:**
- Create: `backend/src/Watchlist.Infrastructure/MongoDbOptions.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoWatchlistItemDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoWatchlistReadRepository.cs`
- Create: `backend/tests/Watchlist.Application.Tests/MongoWatchlistItemDocumentTests.cs`
- Modify: `backend/src/Watchlist.Infrastructure/Watchlist.Infrastructure.csproj`

- [ ] Write failing tests that map a Mongo watchlist document to the complete `WatchlistItem` domain record and reject unspecified enum values.
- [ ] Run `dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter MongoWatchlistItemDocumentTests` and verify failure because the document type does not exist.
- [ ] Add `MongoDB.Driver`, options, document mapping, and `MongoWatchlistReadRepository.GetItemsAsync`.
- [ ] Re-run the focused tests and verify they pass.
- [ ] Commit with `feat: add mongodb watchlist repository`.

### Task 3: MongoDB Sync Status Repository

**Files:**
- Create: `backend/src/Watchlist.Application/ISyncStatusReadRepository.cs`
- Create: `backend/src/Watchlist.Application/SyncStatusDto.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoSyncRunDocument.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoSyncStatusReadRepository.cs`
- Create: `backend/tests/Watchlist.Application.Tests/MongoSyncRunDocumentTests.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`

- [ ] Write a failing test that maps a sync document to `SyncStatusDto`.
- [ ] Run `dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter MongoSyncRunDocumentTests` and verify failure because the sync document does not exist.
- [ ] Add the sync-status application boundary, DTO, MongoDB document, repository, and async `/api/sync/status` endpoint.
- [ ] Re-run the focused test and verify it passes.
- [ ] Commit with `feat: read sync status from mongodb`.

### Task 4: Bootstrap Empty Collections

**Files:**
- Create: `backend/src/Watchlist.Infrastructure/MongoBootstrapHostedService.cs`
- Create: `backend/src/Watchlist.Infrastructure/SeedData.cs`
- Create: `backend/tests/Watchlist.Application.Tests/SeedDataTests.cs`
- Modify: `backend/src/Watchlist.Infrastructure/SeededWatchlistReadRepository.cs`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`

- [ ] Write failing tests that assert the extracted seed data contains the existing three watchlist records and one seeded sync record.
- [ ] Run `dotnet test backend/tests/Watchlist.Application.Tests/Watchlist.Application.Tests.csproj --filter SeedDataTests` and verify failure because `SeedData` does not exist.
- [ ] Extract deterministic seed records, register MongoDB services, and add a hosted bootstrap service that inserts only when each collection count is zero.
- [ ] Keep `SeededWatchlistReadRepository` backed by the extracted seed records for deterministic API test overrides.
- [ ] Re-run the focused tests and verify they pass.
- [ ] Commit with `feat: bootstrap mongodb read model`.

### Task 5: API Dependency Failure Handling

**Files:**
- Create: `backend/src/Watchlist.Api/MongoUnavailableExceptionHandler.cs`
- Modify: `backend/src/Watchlist.Api/Program.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] Add an API test factory override whose watchlist repository throws `MongoConnectionException`; assert `/api/watchlist` returns `503` and JSON `{ "error": "MongoDB is unavailable." }`.
- [ ] Run `dotnet test backend/tests/Watchlist.Api.Tests/Watchlist.Api.Tests.csproj --filter MongoUnavailable` and verify failure because the API currently returns `500`.
- [ ] Add an ASP.NET Core exception handler that maps `MongoException` to HTTP `503`.
- [ ] Re-run the focused API tests and verify they pass.
- [ ] Commit with `feat: return 503 when mongodb is unavailable`.

### Task 6: Deterministic API Tests

**Files:**
- Create: `backend/tests/Watchlist.Api.Tests/SeededApiFactory.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/WatchlistApiTests.cs`

- [ ] Add a factory that replaces Mongo repositories and removes the bootstrap hosted service for API tests.
- [ ] Update existing API tests to use the deterministic factory.
- [ ] Run `dotnet test backend/Watchlist.sln` and verify all tests pass without requiring Docker.
- [ ] Commit with `test: isolate api tests from mongodb`.

### Task 7: Documentation And End-To-End Verification

**Files:**
- Modify: `docs/architecture.md`
- Modify: `docs/integrations.md`
- Modify: `docs/api.md`
- Create: `docs/todo.md` if it is not already present

- [ ] Document Docker Compose startup, bootstrap behavior, MongoDB-backed endpoints, `503` behavior, and the Android TV remote UX backlog item.
- [ ] Run `docker compose up -d mongo`.
- [ ] Run `dotnet test backend/Watchlist.sln`.
- [ ] Start the backend with `dotnet run --project backend/src/Watchlist.Api/Watchlist.Api.csproj --urls http://localhost:5000`.
- [ ] Verify `/api/watchlist?mediaType=movie&filter=all` returns the seeded movie records and `/api/sync/status` returns `seeded`.
- [ ] Run `git diff --check`.
- [ ] Commit with `docs: document mongodb read model`.

