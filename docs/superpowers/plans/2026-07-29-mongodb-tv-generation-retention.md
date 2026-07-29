---
type: Plan
title: MongoDB TV Generation Retention Implementation Plan
description: Test-first implementation tasks for bounded immutable TV-generation storage and six-hour scheduled synchronization.
tags:
  - mongodb
  - tv
  - retention
  - testing
timestamp: 2026-07-29T00:00:00Z
version: 1.0.0
---

# MongoDB TV Generation Retention Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bound immutable TV-generation storage to seven days and 48 total generations while preserving the published pointer and reducing scheduled full syncs to every six hours.

**Architecture:** A pure application planner selects retained, expired, deferred-orphan, and uncertain-orphan generation IDs. A MongoDB repository reads retention metadata and applies a pointer-guarded child-first deletion plan; an infrastructure service runs that plan mandatorily before staging and best-effort after publication under the existing TV operation coordinator.

**Tech Stack:** .NET 10, C#, MongoDB.Driver 3.9, Microsoft.Extensions.Options, Microsoft.Extensions.Logging, xUnit, FluentAssertions, MongoDB 8 integration tests.

---

## File Structure

Create these focused files:

- `backend/src/Watchlist.Application/TvGenerationRetentionPolicy.cs` — validated age, count, and orphan-grace policy.
- `backend/src/Watchlist.Application/TvGenerationRetentionSnapshot.cs` — persistence-neutral stored-generation summary.
- `backend/src/Watchlist.Application/TvGenerationRetentionPlan.cs` — immutable planner output.
- `backend/src/Watchlist.Application/TvGenerationRetentionPlanner.cs` — deterministic keep/delete policy.
- `backend/src/Watchlist.Application/ITvGenerationRetentionRepository.cs` — retention persistence boundary and delete result.
- `backend/src/Watchlist.Application/ITvGenerationRetentionService.cs` — required and best-effort orchestration boundary.
- `backend/src/Watchlist.Application/TvGenerationRetentionException.cs` — stable redacted mandatory-cleanup failure.
- `backend/src/Watchlist.Infrastructure/TvGenerationRetentionOptions.cs` — configuration binding defaults.
- `backend/src/Watchlist.Infrastructure/MongoTvGenerationRetentionRepository.cs` — pointer-guarded metadata reads and deletes.
- `backend/src/Watchlist.Infrastructure/TvGenerationRetentionService.cs` — plan/apply orchestration and redacted logging.
- `backend/tests/Watchlist.Application.Tests/TvGenerationRetentionPlannerTests.cs` — pure boundary tests.
- `backend/tests/Watchlist.Application.Tests/MongoTvGenerationRetentionRepositoryTests.cs` — real-Mongo retention tests.
- `backend/tests/Watchlist.Application.Tests/TvGenerationRetentionServiceTests.cs` — failure and logging tests.

Modify these existing files:

- `backend/src/Watchlist.Infrastructure/TraktOptions.cs`
- `backend/src/Watchlist.Infrastructure/DependencyInjection.cs`
- `backend/src/Watchlist.Infrastructure/TvSyncHostedService.cs`
- `backend/src/Watchlist.Application/TvSyncService.cs`
- `backend/src/Watchlist.Api/MongoUnavailableExceptionHandler.cs`
- `backend/src/Watchlist.Api/appsettings.json`
- `backend/tests/Watchlist.Application.Tests/TvOptionsTests.cs`
- `backend/tests/Watchlist.Application.Tests/TvSyncServiceTests.cs`
- `backend/tests/Watchlist.Api.Tests/TvSyncApiTests.cs`
- `docs/architecture/tv_sync_read_model.md`
- `docs/systems/backend_service.md`
- `docs/apis/backend_api.md`
- `docs/runbooks/tv_sync_operations.md`
- `docs/runbooks/validation.md`

Do not modify indexes, `letterboxd_source_snapshots`, `sync_runs`, worker code,
or destination behavior.

### Task 1: Configure Six-Hour Sync And Retention Defaults

**Files:**
- Create: `backend/src/Watchlist.Infrastructure/TvGenerationRetentionOptions.cs`
- Modify: `backend/src/Watchlist.Infrastructure/TraktOptions.cs:15-19`
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs:47-62`
- Modify: `backend/src/Watchlist.Api/appsettings.json:25-33`
- Test: `backend/tests/Watchlist.Application.Tests/TvOptionsTests.cs`

- [ ] **Step 1: Write failing default and validation tests**

Add these tests to `TvOptionsTests`:

```csharp
[Fact]
public void TraktOptions_Constructor_UsesSixHourFullSyncDefault()
{
    TraktOptions options = new();

    options.FullSyncInterval.Should().Be(TimeSpan.FromHours(6));
}

[Fact]
public void TvGenerationRetentionOptions_Constructor_UsesApprovedDefaults()
{
    TvGenerationRetentionOptions options = new();

    TvGenerationRetentionOptions.SectionName.Should().Be("TvGenerationRetention");
    options.MaxAge.Should().Be(TimeSpan.FromDays(7));
    options.MaxGenerations.Should().Be(48);
    options.OrphanGracePeriod.Should().Be(TimeSpan.FromDays(1));
}

[Theory]
[InlineData("TvGenerationRetention:MaxAge", "00:00:00")]
[InlineData("TvGenerationRetention:MaxGenerations", "0")]
[InlineData("TvGenerationRetention:OrphanGracePeriod", "00:59:59")]
public void AddWatchlistInfrastructure_InvalidRetentionConfiguration_IsRejected(
    string key,
    string value)
{
    IConfiguration configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
        .Build();
    ServiceCollection services = new();
    services.AddWatchlistInfrastructure(configuration);
    using ServiceProvider provider = services.BuildServiceProvider();

    Action resolve = () =>
        _ = provider.GetRequiredService<IOptions<TvGenerationRetentionOptions>>().Value;

    resolve.Should().Throw<OptionsValidationException>();
}
```

Change the existing assertion in
`TraktOptions_Constructor_UsesExpectedDefaults` from one hour to six hours so
the test suite has one consistent expected default.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test backend\Watchlist.sln --configuration Release --filter "FullyQualifiedName~TvOptionsTests" --no-restore
```

Expected: failure because `TvGenerationRetentionOptions` does not exist and
`TraktOptions.FullSyncInterval` is still one hour.

- [ ] **Step 3: Add the options class and approved defaults**

Create `TvGenerationRetentionOptions.cs`:

```csharp
namespace Watchlist.Infrastructure;

public sealed class TvGenerationRetentionOptions
{
    public const string SectionName = "TvGenerationRetention";

    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(7);

    public int MaxGenerations { get; init; } = 48;

    public TimeSpan OrphanGracePeriod { get; init; } = TimeSpan.FromDays(1);
}
```

Change `TraktOptions`:

```csharp
public TimeSpan FullSyncInterval { get; init; } = TimeSpan.FromHours(6);
```

Bind and validate retention options in `DependencyInjection.AddWatchlistInfrastructure`:

```csharp
services.AddOptions<TvGenerationRetentionOptions>()
    .Bind(configuration.GetSection(TvGenerationRetentionOptions.SectionName))
    .Validate(
        options => options.MaxAge > TimeSpan.Zero,
        "TvGenerationRetention:MaxAge must be positive.")
    .Validate(
        options => options.MaxGenerations >= 1,
        "TvGenerationRetention:MaxGenerations must be at least one.")
    .Validate(
        options => options.OrphanGracePeriod >= TimeSpan.FromHours(1),
        "TvGenerationRetention:OrphanGracePeriod must be at least one hour.")
    .ValidateOnStart();
```

Update `appsettings.json`:

```json
"Trakt": {
  "BaseUrl": "https://api.trakt.tv",
  "ClientId": "",
  "ClientSecret": "",
  "RedirectUri": "urn:ietf:wg:oauth:2.0:oob",
  "ActivityPollInterval": "00:05:00",
  "FullSyncInterval": "06:00:00",
  "TokenRefreshSkew": "00:05:00"
},
"TvGenerationRetention": {
  "MaxAge": "7.00:00:00",
  "MaxGenerations": 48,
  "OrphanGracePeriod": "1.00:00:00"
}
```

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the Step 2 command again.

Expected: all `TvOptionsTests` pass.

- [ ] **Step 5: Commit configuration behavior**

```powershell
git add backend/src/Watchlist.Infrastructure/TvGenerationRetentionOptions.cs backend/src/Watchlist.Infrastructure/TraktOptions.cs backend/src/Watchlist.Infrastructure/DependencyInjection.cs backend/src/Watchlist.Api/appsettings.json backend/tests/Watchlist.Application.Tests/TvOptionsTests.cs
git commit -m "feat: configure TV generation retention"
```

### Task 2: Build The Pure Deterministic Retention Planner

**Files:**
- Create: `backend/src/Watchlist.Application/TvGenerationRetentionPolicy.cs`
- Create: `backend/src/Watchlist.Application/TvGenerationRetentionSnapshot.cs`
- Create: `backend/src/Watchlist.Application/TvGenerationRetentionPlan.cs`
- Create: `backend/src/Watchlist.Application/TvGenerationRetentionPlanner.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvGenerationRetentionPlannerTests.cs`

- [ ] **Step 1: Write failing planner boundary tests**

Create `TvGenerationRetentionPlannerTests.cs` with this fixture and cases:

```csharp
using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class TvGenerationRetentionPlannerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);

    private static readonly TvGenerationRetentionPolicy Policy =
        new(TimeSpan.FromDays(7), 48, TimeSpan.FromDays(1));

    private readonly TvGenerationRetentionPlanner planner = new();

    [Fact]
    public void Create_CurrentOlderThanMaxAge_StillRetainsCurrent()
    {
        string current = GenerationId(Now.AddDays(-30), 'a');
        TvGenerationRetentionSnapshot snapshot = new(
            current,
            [new TvStoredGenerationSummary(current, Now.AddDays(-30))],
            []);

        TvGenerationRetentionPlan plan = planner.Create(snapshot, Policy, Now);

        plan.RetainedGenerationIds.Should().Equal(current);
        plan.ExpiredManifestGenerationIds.Should().BeEmpty();
    }

    [Fact]
    public void Create_MoreThanCapInsideAgeWindow_KeepsCurrentAndNewest47()
    {
        TvStoredGenerationSummary[] manifests = Enumerable.Range(0, 60)
            .Select(index => new TvStoredGenerationSummary(
                GenerationId(Now.AddMinutes(-index), (char)('a' + index % 6)),
                Now.AddMinutes(-index)))
            .ToArray();
        string current = manifests[0].GenerationId;

        TvGenerationRetentionPlan plan = planner.Create(
            new TvGenerationRetentionSnapshot(current, manifests, []),
            Policy,
            Now);

        plan.RetainedGenerationIds.Should().HaveCount(48);
        plan.RetainedGenerationIds.Should().Contain(current);
        plan.ExpiredManifestGenerationIds.Should().HaveCount(12);
        plan.RetainedGenerationIds.Should().NotIntersectWith(
            plan.ExpiredManifestGenerationIds);
    }

    [Fact]
    public void Create_SevenDayBoundary_IsInclusive()
    {
        string current = GenerationId(Now, 'a');
        string boundary = GenerationId(Now.AddDays(-7), 'b');
        string older = GenerationId(Now.AddDays(-7).AddTicks(-1), 'c');
        TvGenerationRetentionSnapshot snapshot = new(
            current,
            [
                new TvStoredGenerationSummary(current, Now),
                new TvStoredGenerationSummary(boundary, Now.AddDays(-7)),
                new TvStoredGenerationSummary(older, Now.AddDays(-7).AddTicks(-1))
            ],
            []);

        TvGenerationRetentionPlan plan = planner.Create(snapshot, Policy, Now);

        plan.RetainedGenerationIds.Should().Contain(boundary);
        plan.ExpiredManifestGenerationIds.Should().Equal(older);
    }

    [Fact]
    public void Create_EqualPublishedTimes_UsesOrdinalDescendingGenerationId()
    {
        string current = GenerationId(Now, 'a');
        string lower = GenerationId(Now.AddHours(-1), 'b');
        string higher = GenerationId(Now.AddHours(-1), 'c');
        TvGenerationRetentionPolicy capTwo =
            new(TimeSpan.FromDays(7), 2, TimeSpan.FromDays(1));
        TvGenerationRetentionSnapshot snapshot = new(
            current,
            [
                new TvStoredGenerationSummary(current, Now),
                new TvStoredGenerationSummary(lower, Now.AddHours(-1)),
                new TvStoredGenerationSummary(higher, Now.AddHours(-1))
            ],
            []);

        TvGenerationRetentionPlan plan = planner.Create(snapshot, capTwo, Now);

        plan.RetainedGenerationIds.Should().Contain(higher);
        plan.ExpiredManifestGenerationIds.Should().Equal(lower);
    }

    [Fact]
    public void Create_Orphans_ExpiresOnlySafeIdsAtGraceBoundary()
    {
        string current = GenerationId(Now, 'a');
        string expired = GenerationId(Now.AddDays(-1), 'b');
        string young = GenerationId(Now.AddDays(-1).AddMilliseconds(1), 'c');
        TvGenerationRetentionSnapshot snapshot = new(
            current,
            [new TvStoredGenerationSummary(current, Now)],
            [expired, young, "generation-test-fixture"]);

        TvGenerationRetentionPlan plan = planner.Create(snapshot, Policy, Now);

        plan.ExpiredOrphanGenerationIds.Should().Equal(expired);
        plan.DeferredOrphanGenerationIds.Should().Equal(young);
        plan.UncertainOrphanGenerationIds.Should().Equal("generation-test-fixture");
    }

    [Fact]
    public void Create_NoPublishedPointer_PreservesEveryRow()
    {
        string manifest = GenerationId(Now.AddDays(-30), 'a');
        string orphan = GenerationId(Now.AddDays(-30), 'b');
        TvGenerationRetentionSnapshot snapshot = new(
            null,
            [new TvStoredGenerationSummary(manifest, Now.AddDays(-30))],
            [orphan]);

        TvGenerationRetentionPlan plan = planner.Create(snapshot, Policy, Now);

        plan.RetainedGenerationIds.Should().Equal(manifest);
        plan.ExpiredManifestGenerationIds.Should().BeEmpty();
        plan.ExpiredOrphanGenerationIds.Should().BeEmpty();
        plan.UncertainOrphanGenerationIds.Should().Equal(orphan);
    }

    [Fact]
    public void Create_CurrentManifestMissing_RejectsPlan()
    {
        string current = GenerationId(Now, 'a');

        Action action = () => planner.Create(
            new TvGenerationRetentionSnapshot(current, [], []),
            Policy,
            Now);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("tv_generation_retention_current_manifest_missing");
    }

    private static string GenerationId(DateTimeOffset time, char suffix)
    {
        return $"tv-{time:yyyyMMddHHmmssfff}-{new string(suffix, 32)}";
    }
}
```

- [ ] **Step 2: Run planner tests and verify RED**

Run:

```powershell
dotnet test backend\Watchlist.sln --configuration Release --filter "FullyQualifiedName~TvGenerationRetentionPlannerTests" --no-restore
```

Expected: compile failure because the planner and model types do not exist.

- [ ] **Step 3: Add the policy and persistence-neutral models**

Create `TvGenerationRetentionPolicy.cs`:

```csharp
namespace Watchlist.Application;

public sealed record TvGenerationRetentionPolicy
{
    public TvGenerationRetentionPolicy(
        TimeSpan maxAge,
        int maxGenerations,
        TimeSpan orphanGracePeriod)
    {
        if (maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge));
        }

        if (maxGenerations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGenerations));
        }

        if (orphanGracePeriod < TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(orphanGracePeriod));
        }

        MaxAge = maxAge;
        MaxGenerations = maxGenerations;
        OrphanGracePeriod = orphanGracePeriod;
    }

    public TimeSpan MaxAge { get; }

    public int MaxGenerations { get; }

    public TimeSpan OrphanGracePeriod { get; }
}
```

Create `TvGenerationRetentionSnapshot.cs`:

```csharp
namespace Watchlist.Application;

public sealed record TvStoredGenerationSummary(
    string GenerationId,
    DateTimeOffset PublishedAt);

public sealed record TvGenerationRetentionSnapshot(
    string? CurrentGenerationId,
    IReadOnlyList<TvStoredGenerationSummary> Manifests,
    IReadOnlyList<string> OrphanGenerationIds);
```

Create `TvGenerationRetentionPlan.cs`:

```csharp
namespace Watchlist.Application;

public sealed record TvGenerationRetentionPlan(
    string? ExpectedCurrentGenerationId,
    IReadOnlyList<string> RetainedGenerationIds,
    IReadOnlyList<string> ExpiredManifestGenerationIds,
    IReadOnlyList<string> ExpiredOrphanGenerationIds,
    IReadOnlyList<string> DeferredOrphanGenerationIds,
    IReadOnlyList<string> UncertainOrphanGenerationIds);
```

- [ ] **Step 4: Implement the minimal planner**

Create `TvGenerationRetentionPlanner.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace Watchlist.Application;

public sealed partial class TvGenerationRetentionPlanner
{
    public TvGenerationRetentionPlan Create(
        TvGenerationRetentionSnapshot snapshot,
        TvGenerationRetentionPolicy policy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(policy);
        if (now == default || now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Retention time must be UTC.", nameof(now));
        }

        TvStoredGenerationSummary[] ordered = snapshot.Manifests
            .OrderByDescending(manifest => manifest.PublishedAt)
            .ThenByDescending(manifest => manifest.GenerationId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select(manifest => manifest.GenerationId)
            .Distinct(StringComparer.Ordinal)
            .Count() != ordered.Length)
        {
            throw new InvalidOperationException("tv_generation_retention_manifest_duplicate");
        }

        if (snapshot.CurrentGenerationId is null)
        {
            return new TvGenerationRetentionPlan(
                null,
                ordered.Select(manifest => manifest.GenerationId).ToArray(),
                [],
                [],
                [],
                snapshot.OrphanGenerationIds.Order(StringComparer.Ordinal).ToArray());
        }

        if (!ordered.Any(manifest => string.Equals(
            manifest.GenerationId,
            snapshot.CurrentGenerationId,
            StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "tv_generation_retention_current_manifest_missing");
        }

        HashSet<string> retained = new(StringComparer.Ordinal)
        {
            snapshot.CurrentGenerationId
        };
        List<string> expiredManifests = [];
        DateTimeOffset cutoff = now - policy.MaxAge;
        foreach (TvStoredGenerationSummary manifest in ordered)
        {
            if (string.Equals(
                manifest.GenerationId,
                snapshot.CurrentGenerationId,
                StringComparison.Ordinal))
            {
                continue;
            }

            if (manifest.PublishedAt >= cutoff
                && retained.Count < policy.MaxGenerations)
            {
                retained.Add(manifest.GenerationId);
            }
            else
            {
                expiredManifests.Add(manifest.GenerationId);
            }
        }

        List<string> expiredOrphans = [];
        List<string> deferredOrphans = [];
        List<string> uncertainOrphans = [];
        DateTimeOffset orphanCutoff = now - policy.OrphanGracePeriod;
        foreach (string generationId in snapshot.OrphanGenerationIds
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            if (!TryReadCreatedAt(generationId, out DateTimeOffset createdAt))
            {
                uncertainOrphans.Add(generationId);
            }
            else if (createdAt <= orphanCutoff)
            {
                expiredOrphans.Add(generationId);
            }
            else
            {
                deferredOrphans.Add(generationId);
            }
        }

        return new TvGenerationRetentionPlan(
            snapshot.CurrentGenerationId,
            retained.Order(StringComparer.Ordinal).ToArray(),
            expiredManifests.Order(StringComparer.Ordinal).ToArray(),
            expiredOrphans,
            deferredOrphans,
            uncertainOrphans);
    }

    private static bool TryReadCreatedAt(
        string generationId,
        out DateTimeOffset createdAt)
    {
        Match match = ProductionGenerationId().Match(generationId);
        return match.Success
            && DateTimeOffset.TryParseExact(
                match.Groups["timestamp"].Value,
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out createdAt);
    }

    [GeneratedRegex(
        "^tv-(?<timestamp>[0-9]{17})-[0-9a-f]{32}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProductionGenerationId();
}
```

- [ ] **Step 5: Run planner tests and verify GREEN**

Run the Step 2 command again.

Expected: all planner tests pass.

- [ ] **Step 6: Commit the pure planner**

```powershell
git add backend/src/Watchlist.Application/TvGenerationRetentionPolicy.cs backend/src/Watchlist.Application/TvGenerationRetentionSnapshot.cs backend/src/Watchlist.Application/TvGenerationRetentionPlan.cs backend/src/Watchlist.Application/TvGenerationRetentionPlanner.cs backend/tests/Watchlist.Application.Tests/TvGenerationRetentionPlannerTests.cs
git commit -m "feat: plan bounded TV generation retention"
```

### Task 3: Add Pointer-Guarded MongoDB Retention

**Files:**
- Create: `backend/src/Watchlist.Application/ITvGenerationRetentionRepository.cs`
- Create: `backend/src/Watchlist.Infrastructure/MongoTvGenerationRetentionRepository.cs`
- Test: `backend/tests/Watchlist.Application.Tests/MongoTvGenerationRetentionRepositoryTests.cs`

- [ ] **Step 1: Write failing Mongo integration tests**

Create `MongoTvGenerationRetentionRepositoryTests.cs`. Use the same
per-test-database `IAsyncLifetime` pattern as
`MongoTvGenerationRepositoryTests`. Add these core cases:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoTvGenerationRetentionRepositoryTests : IAsyncLifetime
{
[Fact]
public async Task ReadSnapshotAsync_ReturnsPointerManifestsAndPhysicalOrphans()
{
    DateTimeOffset now = new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
    string current = GenerationId(now, 'a');
    string abandoned = GenerationId(now.AddDays(-2), 'b');
    await InsertManifestAsync(current, now);
    await InsertPointerAsync(current, now);
    await InsertShowAsync(current, 1);
    await InsertShowAsync(abandoned, 2);
    await InsertEventAsync(abandoned, 2);

    TvGenerationRetentionSnapshot snapshot = await repository.ReadSnapshotAsync(
        CancellationToken.None);

    snapshot.CurrentGenerationId.Should().Be(current);
    snapshot.Manifests.Should().ContainSingle()
        .Which.GenerationId.Should().Be(current);
    snapshot.OrphanGenerationIds.Should().Equal(abandoned);
}

[Fact]
public async Task ApplyAsync_DeletesChildrenBeforeManifestAndPreservesPointerAndLegacy()
{
    DateTimeOffset now = new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
    string current = GenerationId(now, 'a');
    string expired = GenerationId(now.AddDays(-8), 'b');
    string orphan = GenerationId(now.AddDays(-2), 'c');
    await InsertGenerationAsync(current, now, 1);
    await InsertGenerationAsync(expired, now.AddDays(-8), 2);
    await InsertShowAsync(orphan, 3);
    await InsertEventAsync(orphan, 3);
    await InsertPointerAsync(current, now);
    await InsertLegacyShowAsync();
    TvGenerationRetentionPlan plan = new(
        current,
        [current],
        [expired],
        [orphan],
        [],
        []);

    TvGenerationRetentionDeleteResult result = await repository.ApplyAsync(
        plan,
        CancellationToken.None);

    result.ShowDocumentsDeleted.Should().Be(2);
    result.LifecycleEventsDeleted.Should().Be(2);
    result.ManifestsDeleted.Should().Be(1);
    (await ReadPointerAsync())["generationId"].AsString.Should().Be(current);
    (await database.GetCollection<BsonDocument>(ShowsCollection)
        .CountDocumentsAsync(new BsonDocument("documentKind", "legacy")))
        .Should().Be(1);
}

[Fact]
public async Task ApplyAsync_WhenPointerChanged_DeletesNothing()
{
    DateTimeOffset now = new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
    string first = GenerationId(now.AddHours(-1), 'a');
    string second = GenerationId(now, 'b');
    await InsertGenerationAsync(first, now.AddHours(-1), 1);
    await InsertGenerationAsync(second, now, 2);
    await InsertPointerAsync(second, now);
    TvGenerationRetentionPlan stale = new(
        first,
        [first],
        [second],
        [],
        [],
        []);

    Func<Task> action = () => repository.ApplyAsync(
        stale,
        CancellationToken.None);

    await action.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("tv_generation_retention_pointer_changed");
    (await database.GetCollection<BsonDocument>(ShowsCollection)
        .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty))
        .Should().Be(2);
}

[Fact]
public async Task ApplyAsync_WhenPointerShapeIsInvalid_DeletesNothing()
{
    DateTimeOffset now = new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
    string current = GenerationId(now, 'a');
    string expired = GenerationId(now.AddDays(-8), 'b');
    await InsertGenerationAsync(current, now, 1);
    await InsertGenerationAsync(expired, now.AddDays(-8), 2);
    await database
        .GetCollection<MongoTvPublishedPointerDocument>(ManifestsCollection)
        .InsertOneAsync(new MongoTvPublishedPointerDocument
        {
            Id = MongoTvPublishedPointerDocument.PublishedPointerId,
            DocumentKind =
                MongoTvPublishedPointerDocument.PointerDocumentKind,
            GenerationId = current,
            ManifestId = "invalid",
            ShowCount = 1,
            LifecycleEventCount = 1,
            MembershipHash = new string('a', 64),
            ProgressHash = new string('b', 64),
            PublishedAt = now
        });
    TvGenerationRetentionPlan plan = new(
        current,
        [current],
        [expired],
        [],
        [],
        []);

    Func<Task> action = () => repository.ApplyAsync(
        plan,
        CancellationToken.None);

    await action.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("tv_generation_retention_pointer_invalid");
    (await database.GetCollection<BsonDocument>(ShowsCollection)
        .CountDocumentsAsync(new BsonDocument("generationId", expired)))
        .Should().Be(1);
}

[Fact]
public async Task ApplyAsync_OverlappingPlan_RejectsBeforeDelete()
{
    DateTimeOffset now = new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
    string current = GenerationId(now, 'a');
    await InsertGenerationAsync(current, now, 1);
    await InsertPointerAsync(current, now);
    TvGenerationRetentionPlan invalid = new(
        current,
        [current],
        [current],
        [],
        [],
        []);

    Func<Task> action = () => repository.ApplyAsync(
        invalid,
        CancellationToken.None);

    await action.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("tv_generation_retention_plan_invalid");
    (await database.GetCollection<BsonDocument>(ShowsCollection)
        .CountDocumentsAsync(new BsonDocument("generationId", current)))
        .Should().Be(1);
}

[Fact]
public async Task ApplyAsync_ExactRetry_IsIdempotent()
{
    DateTimeOffset now = new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
    string current = GenerationId(now, 'a');
    string expired = GenerationId(now.AddDays(-8), 'b');
    await InsertGenerationAsync(current, now, 1);
    await InsertGenerationAsync(expired, now.AddDays(-8), 2);
    await InsertPointerAsync(current, now);
    TvGenerationRetentionPlan plan = new(
        current,
        [current],
        [expired],
        [],
        [],
        []);

    await repository.ApplyAsync(plan, CancellationToken.None);
    TvGenerationRetentionDeleteResult retry = await repository.ApplyAsync(
        plan,
        CancellationToken.None);

    retry.Should().Be(new TvGenerationRetentionDeleteResult(0, 0, 0));
}

[Fact]
public async Task ApplyAsync_AfterChildFirstPartialCleanup_Converges()
{
    DateTimeOffset now = new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
    string current = GenerationId(now, 'a');
    string expired = GenerationId(now.AddDays(-8), 'b');
    await InsertGenerationAsync(current, now, 1);
    await InsertGenerationAsync(expired, now.AddDays(-8), 2);
    await InsertPointerAsync(current, now);
    await database.GetCollection<BsonDocument>(ShowsCollection).DeleteManyAsync(
        new BsonDocument("generationId", expired));
    TvGenerationRetentionPlan plan = new(
        current,
        [current],
        [expired],
        [],
        [],
        []);

    TvGenerationRetentionDeleteResult result = await repository.ApplyAsync(
        plan,
        CancellationToken.None);

    result.ShowDocumentsDeleted.Should().Be(0);
    result.LifecycleEventsDeleted.Should().Be(1);
    result.ManifestsDeleted.Should().Be(1);
}

[Fact]
public async Task PlannerAndApplyAsync_ActivityBurst_LeavesAtMost48Manifests()
{
    DateTimeOffset now = new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
    List<string> ids = [];
    for (int index = 0; index < 50; index++)
    {
        DateTimeOffset publishedAt = now.AddHours(-index);
        string id = GenerationId(publishedAt, 'a');
        ids.Add(id);
        await InsertGenerationAsync(id, publishedAt, index + 1);
    }

    await InsertPointerAsync(ids[0], now);
    TvGenerationRetentionSnapshot snapshot = await repository.ReadSnapshotAsync(
        CancellationToken.None);
    TvGenerationRetentionPlan plan = new TvGenerationRetentionPlanner().Create(
        snapshot,
        new TvGenerationRetentionPolicy(
            TimeSpan.FromDays(7),
            48,
            TimeSpan.FromDays(1)),
        now);

    await repository.ApplyAsync(plan, CancellationToken.None);

    long retained = await database
        .GetCollection<MongoTvSyncManifestDocument>(ManifestsCollection)
        .CountDocumentsAsync(document =>
            document.DocumentKind
                == MongoTvSyncManifestDocument.ManifestDocumentKind);
    retained.Should().Be(48);
    (await ReadPointerAsync())["generationId"].AsString.Should().Be(ids[0]);
}
```

Use these fixture fields and helpers so every inserted value is synthetic and
the temporary database is dropped:

```csharp
private const string ShowsCollection = "tv_shows";
private const string ManifestsCollection = "tv_sync_manifests";
private const string EventsCollection = "tv_lifecycle_events";

private readonly string databaseName =
    $"watchlist_tv_retention_{Guid.NewGuid():N}";
private readonly MongoClient client = new("mongodb://localhost:27017");
private readonly IMongoDatabase database;
private readonly MongoTvGenerationRetentionRepository repository;

public MongoTvGenerationRetentionRepositoryTests()
{
    database = client.GetDatabase(databaseName);
    MongoDbOptions options = new()
    {
        ConnectionString = "mongodb://localhost:27017",
        DatabaseName = databaseName,
        TvShowsCollectionName = ShowsCollection,
        TvSyncManifestsCollectionName = ManifestsCollection,
        TvLifecycleEventsCollectionName = EventsCollection
    };
    repository = new MongoTvGenerationRetentionRepository(
        database,
        Options.Create(options));
}

public Task InitializeAsync() => Task.CompletedTask;

public async Task DisposeAsync()
{
    await client.DropDatabaseAsync(databaseName);
}

private Task InsertManifestAsync(string generationId, DateTimeOffset publishedAt)
{
    return database.GetCollection<MongoTvSyncManifestDocument>(ManifestsCollection)
        .InsertOneAsync(new MongoTvSyncManifestDocument
        {
            Id = $"generation:{generationId}",
            DocumentKind = MongoTvSyncManifestDocument.ManifestDocumentKind,
            GenerationId = generationId,
            PublishedAt = publishedAt
        });
}

private Task InsertPointerAsync(string generationId, DateTimeOffset publishedAt)
{
    return database.GetCollection<MongoTvPublishedPointerDocument>(
            ManifestsCollection)
        .ReplaceOneAsync(
            pointer => pointer.Id == MongoTvPublishedPointerDocument.PublishedPointerId,
            new MongoTvPublishedPointerDocument
            {
                GenerationId = generationId,
                ManifestId = $"generation:{generationId}",
                ShowCount = 1,
                LifecycleEventCount = 1,
                MembershipHash = new string('a', 64),
                ProgressHash = new string('b', 64),
                PublishedAt = publishedAt
            },
            new ReplaceOptions { IsUpsert = true });
}

private Task InsertShowAsync(string generationId, long traktId)
{
    return database.GetCollection<MongoTvShowDocument>(ShowsCollection)
        .InsertOneAsync(new MongoTvShowDocument
        {
            Id = $"generation:{generationId}:{traktId}",
            DocumentKind = MongoTvShowDocument.GenerationDocumentKind,
            GenerationId = generationId,
            TraktId = traktId
        });
}

private Task InsertEventAsync(string generationId, long traktId)
{
    return database.GetCollection<MongoTvLifecycleEventDocument>(EventsCollection)
        .InsertOneAsync(new MongoTvLifecycleEventDocument
        {
            Id = $"generation:{generationId}:event-{traktId}",
            EventId = $"event-{traktId}",
            GenerationId = generationId,
            TraktId = traktId
        });
}

private async Task InsertGenerationAsync(
    string generationId,
    DateTimeOffset publishedAt,
    long traktId)
{
    await InsertManifestAsync(generationId, publishedAt);
    await InsertShowAsync(generationId, traktId);
    await InsertEventAsync(generationId, traktId);
}

private Task InsertLegacyShowAsync()
{
    return database.GetCollection<MongoTvShowDocument>(ShowsCollection)
        .InsertOneAsync(new MongoTvShowDocument
        {
            Id = "legacy:synthetic",
            DocumentKind = MongoTvShowDocument.LegacyDocumentKind
        });
}

private async Task<BsonDocument> ReadPointerAsync()
{
    return await database.GetCollection<BsonDocument>(ManifestsCollection)
        .Find(new BsonDocument(
            "_id",
            MongoTvPublishedPointerDocument.PublishedPointerId))
        .SingleAsync();
}

private static string GenerationId(DateTimeOffset time, char suffix)
{
    return $"tv-{time:yyyyMMddHHmmssfff}-{new string(suffix, 32)}";
}
}
```

- [ ] **Step 2: Run Mongo retention tests and verify RED**

Start local MongoDB 8 on `localhost:27017`, then run:

```powershell
dotnet test backend\Watchlist.sln --configuration Release --filter "FullyQualifiedName~MongoTvGenerationRetentionRepositoryTests" --no-restore
```

Expected: compile failure because the repository contract and implementation do
not exist.

- [ ] **Step 3: Add the repository contract**

Create `ITvGenerationRetentionRepository.cs`:

```csharp
namespace Watchlist.Application;

public sealed record TvGenerationRetentionDeleteResult(
    long ShowDocumentsDeleted,
    long LifecycleEventsDeleted,
    long ManifestsDeleted);

public interface ITvGenerationRetentionRepository
{
    Task<TvGenerationRetentionSnapshot> ReadSnapshotAsync(
        CancellationToken cancellationToken);

    Task<TvGenerationRetentionDeleteResult> ApplyAsync(
        TvGenerationRetentionPlan plan,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement metadata reads and pointer-guarded deletes**

Create `MongoTvGenerationRetentionRepository.cs` with these complete
operations:

```csharp
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoTvGenerationRetentionRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : ITvGenerationRetentionRepository
{
    private readonly IMongoCollection<MongoTvShowDocument> shows =
        database.GetCollection<MongoTvShowDocument>(
            options.Value.TvShowsCollectionName);
    private readonly IMongoCollection<MongoTvLifecycleEventDocument> events =
        database.GetCollection<MongoTvLifecycleEventDocument>(
            options.Value.TvLifecycleEventsCollectionName);
    private readonly IMongoCollection<MongoTvSyncManifestDocument> manifests =
        database.GetCollection<MongoTvSyncManifestDocument>(
            options.Value.TvSyncManifestsCollectionName);
    private readonly IMongoCollection<MongoTvPublishedPointerDocument> pointers =
        database.GetCollection<MongoTvPublishedPointerDocument>(
            options.Value.TvSyncManifestsCollectionName);

    public async Task<TvGenerationRetentionSnapshot> ReadSnapshotAsync(
        CancellationToken cancellationToken)
    {
        MongoTvPublishedPointerDocument? pointer = await pointers
            .Find(document =>
                document.Id == MongoTvPublishedPointerDocument.PublishedPointerId)
            .FirstOrDefaultAsync(cancellationToken);
        List<MongoTvSyncManifestDocument> manifestDocuments = await manifests
            .Find(document =>
                document.DocumentKind
                    == MongoTvSyncManifestDocument.ManifestDocumentKind)
            .ToListAsync(cancellationToken);
        TvStoredGenerationSummary[] summaries = manifestDocuments
            .Select(document => new TvStoredGenerationSummary(
                document.GenerationId,
                document.PublishedAt))
            .ToArray();
        HashSet<string> manifestIds = summaries
            .Select(summary => summary.GenerationId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> physicalIds = new(StringComparer.Ordinal);

        FilterDefinition<MongoTvShowDocument> generationRows =
            Builders<MongoTvShowDocument>.Filter.Eq(
                document => document.DocumentKind,
                MongoTvShowDocument.GenerationDocumentKind)
            & Builders<MongoTvShowDocument>.Filter.Ne(
                document => document.GenerationId,
                null);
        using (IAsyncCursor<string?> showCursor = await shows.DistinctAsync(
            document => document.GenerationId,
            generationRows,
            cancellationToken: cancellationToken))
        {
            physicalIds.UnionWith((await showCursor.ToListAsync(cancellationToken))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!));
        }

        using (IAsyncCursor<string> eventCursor = await events.DistinctAsync(
            document => document.GenerationId,
            FilterDefinition<MongoTvLifecycleEventDocument>.Empty,
            cancellationToken: cancellationToken))
        {
            physicalIds.UnionWith(await eventCursor.ToListAsync(cancellationToken));
        }

        physicalIds.ExceptWith(manifestIds);
        return new TvGenerationRetentionSnapshot(
            pointer?.GenerationId,
            summaries,
            physicalIds.Order(StringComparer.Ordinal).ToArray());
    }

    public async Task<TvGenerationRetentionDeleteResult> ApplyAsync(
        TvGenerationRetentionPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidatePlan(plan);
        MongoTvPublishedPointerDocument? pointer = await pointers
            .Find(document =>
                document.Id == MongoTvPublishedPointerDocument.PublishedPointerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (pointer is not null && !pointer.HasValidShape())
        {
            throw new InvalidOperationException(
                "tv_generation_retention_pointer_invalid");
        }

        string? current = pointer?.GenerationId;
        if (!string.Equals(
            current,
            plan.ExpectedCurrentGenerationId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "tv_generation_retention_pointer_changed");
        }

        string[] manifestIds = plan.ExpiredManifestGenerationIds
            .Where(id => !string.Equals(id, current, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] childIds = manifestIds
            .Concat(plan.ExpiredOrphanGenerationIds)
            .Where(id => !string.Equals(id, current, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        long showsDeleted = 0;
        long eventsDeleted = 0;
        long manifestsDeleted = 0;

        if (childIds.Length > 0)
        {
            DeleteResult showResult = await shows.DeleteManyAsync(
                Builders<MongoTvShowDocument>.Filter.Eq(
                    document => document.DocumentKind,
                    MongoTvShowDocument.GenerationDocumentKind)
                & Builders<MongoTvShowDocument>.Filter.In(
                    document => document.GenerationId,
                    childIds)
                & Builders<MongoTvShowDocument>.Filter.Ne(
                    document => document.GenerationId,
                    current),
                cancellationToken);
            EnsureAcknowledged(showResult);
            DeleteResult eventResult = await events.DeleteManyAsync(
                Builders<MongoTvLifecycleEventDocument>.Filter.In(
                    document => document.GenerationId,
                    childIds)
                & Builders<MongoTvLifecycleEventDocument>.Filter.Ne(
                    document => document.GenerationId,
                    current),
                cancellationToken);
            EnsureAcknowledged(eventResult);
            showsDeleted = showResult.DeletedCount;
            eventsDeleted = eventResult.DeletedCount;
        }

        if (manifestIds.Length > 0)
        {
            DeleteResult manifestResult = await manifests.DeleteManyAsync(
                Builders<MongoTvSyncManifestDocument>.Filter.Eq(
                    document => document.DocumentKind,
                    MongoTvSyncManifestDocument.ManifestDocumentKind)
                & Builders<MongoTvSyncManifestDocument>.Filter.In(
                    document => document.GenerationId,
                    manifestIds)
                & Builders<MongoTvSyncManifestDocument>.Filter.Ne(
                    document => document.GenerationId,
                    current),
                cancellationToken);
            EnsureAcknowledged(manifestResult);
            manifestsDeleted = manifestResult.DeletedCount;
        }

        return new TvGenerationRetentionDeleteResult(
            showsDeleted,
            eventsDeleted,
            manifestsDeleted);
    }

    private static void ValidatePlan(TvGenerationRetentionPlan plan)
    {
        HashSet<string> retained =
            plan.RetainedGenerationIds.ToHashSet(StringComparer.Ordinal);
        HashSet<string> expired = plan.ExpiredManifestGenerationIds
            .Concat(plan.ExpiredOrphanGenerationIds)
            .ToHashSet(StringComparer.Ordinal);
        bool expectedCurrentIsRetained =
            plan.ExpectedCurrentGenerationId is null
            || retained.Contains(plan.ExpectedCurrentGenerationId);
        bool destructivePlanHasPointer =
            plan.ExpectedCurrentGenerationId is not null
            || expired.Count == 0;
        bool deleteSetsAreDistinct =
            plan.ExpiredManifestGenerationIds
                .Intersect(
                    plan.ExpiredOrphanGenerationIds,
                    StringComparer.Ordinal)
                .Any() is false;
        if (!expectedCurrentIsRetained
            || !destructivePlanHasPointer
            || retained.Overlaps(expired)
            || !deleteSetsAreDistinct)
        {
            throw new InvalidOperationException(
                "tv_generation_retention_plan_invalid");
        }
    }

    private static void EnsureAcknowledged(DeleteResult result)
    {
        if (!result.IsAcknowledged)
        {
            throw new InvalidOperationException(
                "tv_generation_retention_write_unacknowledged");
        }
    }
}
```

- [ ] **Step 5: Run Mongo retention tests and verify GREEN**

Run the Step 2 command again.

Expected: all Mongo retention tests pass, including the exact idempotent retry.

- [ ] **Step 6: Commit Mongo retention persistence**

```powershell
git add backend/src/Watchlist.Application/ITvGenerationRetentionRepository.cs backend/src/Watchlist.Infrastructure/MongoTvGenerationRetentionRepository.cs backend/tests/Watchlist.Application.Tests/MongoTvGenerationRetentionRepositoryTests.cs
git commit -m "feat: prune expired TV generations in MongoDB"
```

### Task 4: Orchestrate Required And Best-Effort Retention

**Files:**
- Create: `backend/src/Watchlist.Application/ITvGenerationRetentionService.cs`
- Create: `backend/src/Watchlist.Application/TvGenerationRetentionException.cs`
- Create: `backend/src/Watchlist.Infrastructure/TvGenerationRetentionService.cs`
- Test: `backend/tests/Watchlist.Application.Tests/TvGenerationRetentionServiceTests.cs`

- [ ] **Step 1: Write failing orchestration tests**

Create `TvGenerationRetentionServiceTests.cs` with fakes for the repository,
time provider, and logger:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TvGenerationRetentionServiceTests
{
[Fact]
public async Task PruneRequiredAsync_AppliesPlannerResultAndLogsRedactedCounts()
{
    Harness harness = Harness.Create();
    harness.Repository.Snapshot = harness.CurrentSnapshot();

    await harness.Service.PruneRequiredAsync(CancellationToken.None);

    harness.Repository.ApplyCalls.Should().Be(1);
    harness.Logs.Should().Contain(entry =>
        entry.Contains("pre_sync", StringComparison.Ordinal)
        && entry.Contains("ManifestsDeleted", StringComparison.Ordinal));
}

[Fact]
public async Task PruneRequiredAsync_RepositoryFailure_ThrowsStableTypedFailure()
{
    Harness harness = Harness.Create();
    harness.Repository.ReadException =
        new InvalidOperationException("secret-document-payload");

    Func<Task> action = () =>
        harness.Service.PruneRequiredAsync(CancellationToken.None);

    TvGenerationRetentionException failure = (await action.Should()
        .ThrowAsync<TvGenerationRetentionException>())
        .Which;
    failure.Code.Should().Be("tv_generation_retention_failed");
    failure.Message.Should().NotContain("secret-document-payload");
    harness.Logs.Should().NotContain(entry =>
        entry.Contains("secret-document-payload", StringComparison.Ordinal));
}

[Fact]
public async Task PruneBestEffortAsync_RepositoryFailure_LogsDeferredAndReturns()
{
    Harness harness = Harness.Create();
    harness.Repository.ReadException =
        new InvalidOperationException("secret-document-payload");

    await harness.Service.PruneBestEffortAsync(CancellationToken.None);

    harness.Logs.Should().Contain(entry =>
        entry.Contains("tv_generation_retention_deferred", StringComparison.Ordinal));
    harness.Logs.Should().NotContain(entry =>
        entry.Contains("secret-document-payload", StringComparison.Ordinal));
}
```

Use these complete fakes:

```csharp
private sealed class Harness
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);

    private Harness()
    {
        Repository = new FakeRepository();
        ListLogger<TvGenerationRetentionService> logger = new();
        Logs = logger.Messages;
        Service = new TvGenerationRetentionService(
            Repository,
            new TvGenerationRetentionPlanner(),
            new TvGenerationRetentionPolicy(
                TimeSpan.FromDays(7),
                48,
                TimeSpan.FromDays(1)),
            new FixedTimeProvider(Now),
            logger);
    }

    public FakeRepository Repository { get; }

    public List<string> Logs { get; }

    public TvGenerationRetentionService Service { get; }

    public static Harness Create() => new();

    public TvGenerationRetentionSnapshot CurrentSnapshot()
    {
        string current =
            $"tv-{Now:yyyyMMddHHmmssfff}-{new string('a', 32)}";
        return new TvGenerationRetentionSnapshot(
            current,
            [new TvStoredGenerationSummary(current, Now)],
            []);
    }
}

private sealed class FakeRepository : ITvGenerationRetentionRepository
{
    public TvGenerationRetentionSnapshot Snapshot { get; set; } =
        new(null, [], []);

    public Exception? ReadException { get; set; }

    public int ApplyCalls { get; private set; }

    public Task<TvGenerationRetentionSnapshot> ReadSnapshotAsync(
        CancellationToken cancellationToken)
    {
        return ReadException is null
            ? Task.FromResult(Snapshot)
            : Task.FromException<TvGenerationRetentionSnapshot>(ReadException);
    }

    public Task<TvGenerationRetentionDeleteResult> ApplyAsync(
        TvGenerationRetentionPlan plan,
        CancellationToken cancellationToken)
    {
        ApplyCalls++;
        return Task.FromResult(new TvGenerationRetentionDeleteResult(0, 0, 0));
    }
}

private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

private sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, null));
    }
}
}
```

- [ ] **Step 2: Run service tests and verify RED**

Run:

```powershell
dotnet test backend\Watchlist.sln --configuration Release --filter "FullyQualifiedName~TvGenerationRetentionServiceTests" --no-restore
```

Expected: compile failure because the service contract, exception, and
implementation do not exist.

- [ ] **Step 3: Add the service contract and typed exception**

Create `ITvGenerationRetentionService.cs`:

```csharp
namespace Watchlist.Application;

public interface ITvGenerationRetentionService
{
    Task PruneRequiredAsync(CancellationToken cancellationToken);

    Task PruneBestEffortAsync(CancellationToken cancellationToken);
}
```

Create `TvGenerationRetentionException.cs`:

```csharp
namespace Watchlist.Application;

public sealed class TvGenerationRetentionException : Exception
{
    public const string StableCode = "tv_generation_retention_failed";

    public TvGenerationRetentionException(Exception innerException)
        : base("TV generation retention failed.", innerException)
    {
    }

    public string Code => StableCode;
}
```

- [ ] **Step 4: Implement orchestration and redacted logging**

Create `TvGenerationRetentionService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class TvGenerationRetentionService(
    ITvGenerationRetentionRepository repository,
    TvGenerationRetentionPlanner planner,
    TvGenerationRetentionPolicy policy,
    TimeProvider timeProvider,
    ILogger<TvGenerationRetentionService> logger)
    : ITvGenerationRetentionService
{
    public Task PruneRequiredAsync(CancellationToken cancellationToken)
    {
        return RunRequiredAsync("pre_sync", cancellationToken);
    }

    public async Task PruneBestEffortAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunCoreAsync("post_publish", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "TV retention {Mode} deferred with code {Code} and exception type {ExceptionType}.",
                "post_publish",
                "tv_generation_retention_deferred",
                exception.GetType().Name);
        }
    }

    private async Task RunRequiredAsync(
        string mode,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunCoreAsync(mode, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TvGenerationRetentionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "TV retention {Mode} failed with code {Code} and exception type {ExceptionType}.",
                mode,
                TvGenerationRetentionException.StableCode,
                exception.GetType().Name);
            throw new TvGenerationRetentionException(exception);
        }
    }

    private async Task RunCoreAsync(
        string mode,
        CancellationToken cancellationToken)
    {
        TvGenerationRetentionSnapshot snapshot = await repository
            .ReadSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        TvGenerationRetentionPlan plan = planner.Create(
            snapshot,
            policy,
            timeProvider.GetUtcNow().ToUniversalTime());
        TvGenerationRetentionDeleteResult deleted = await repository
            .ApplyAsync(plan, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "TV retention {Mode} completed. Retained={Retained}; ExpiredManifests={ExpiredManifests}; ExpiredOrphans={ExpiredOrphans}; DeferredOrphans={DeferredOrphans}; UncertainOrphans={UncertainOrphans}; ShowDocumentsDeleted={ShowDocumentsDeleted}; LifecycleEventsDeleted={LifecycleEventsDeleted}; ManifestsDeleted={ManifestsDeleted}.",
            mode,
            plan.RetainedGenerationIds.Count,
            plan.ExpiredManifestGenerationIds.Count,
            plan.ExpiredOrphanGenerationIds.Count,
            plan.DeferredOrphanGenerationIds.Count,
            plan.UncertainOrphanGenerationIds.Count,
            deleted.ShowDocumentsDeleted,
            deleted.LifecycleEventsDeleted,
            deleted.ManifestsDeleted);
    }
}
```

- [ ] **Step 5: Run service tests and verify GREEN**

Run the Step 2 command again.

Expected: all retention service tests pass and no test log contains the
synthetic secret payload.

- [ ] **Step 6: Commit retention orchestration**

```powershell
git add backend/src/Watchlist.Application/ITvGenerationRetentionService.cs backend/src/Watchlist.Application/TvGenerationRetentionException.cs backend/src/Watchlist.Infrastructure/TvGenerationRetentionService.cs backend/tests/Watchlist.Application.Tests/TvGenerationRetentionServiceTests.cs
git commit -m "feat: coordinate TV retention around publication"
```

### Task 5: Wire Retention Into Sync, DI, Hosted Logs, And API Failures

**Files:**
- Modify: `backend/src/Watchlist.Infrastructure/DependencyInjection.cs:124-193`
- Modify: `backend/src/Watchlist.Application/TvSyncService.cs:10-46`
- Modify: `backend/src/Watchlist.Application/TvSyncService.cs:154-178`
- Modify: `backend/src/Watchlist.Infrastructure/TvSyncHostedService.cs:83-105`
- Modify: `backend/src/Watchlist.Api/MongoUnavailableExceptionHandler.cs:14-74`
- Modify: `backend/tests/Watchlist.Application.Tests/TvOptionsTests.cs`
- Modify: `backend/tests/Watchlist.Application.Tests/TvSyncServiceTests.cs`
- Modify: `backend/tests/Watchlist.Api.Tests/TvSyncApiTests.cs`

- [ ] **Step 1: Write failing sync ordering and failure tests**

Extend the `TvSyncServiceTests.Harness` with a
`FakeGenerationRetentionService`. Pass it to `TvSyncService`, and share one
`List<string>` call order with the fake generation repository.

Add:

```csharp
[Fact]
public async Task SyncAsync_RunsRequiredRetentionBeforeSourceCollectionAndPostAfterPublish()
{
    Harness harness = Harness.Create();

    await harness.SyncAsync(TvGenerationKind.ScheduledFull);

    harness.CallOrder.Should().Equal(
        "retention-required",
        "stage",
        "publish",
        "retention-best-effort");
}

[Fact]
public async Task SyncAsync_RequiredRetentionFailure_StagesAndPublishesNothing()
{
    Harness harness = Harness.Create();
    harness.Retention.RequiredException =
        new TvGenerationRetentionException(new InvalidOperationException());

    Func<Task> action = () =>
        harness.SyncAsync(TvGenerationKind.ScheduledFull);

    await action.Should().ThrowAsync<TvGenerationRetentionException>();
    harness.Repository.StageCalls.Should().Be(0);
    harness.Repository.PublishCalls.Should().Be(0);
    harness.TokenProvider.GetCalls.Should().Be(0);
}
```

The fake is:

```csharp
private sealed class FakeGenerationRetentionService(
    List<string> callOrder) : ITvGenerationRetentionService
{
    public Exception? RequiredException { get; set; }

    public Task PruneRequiredAsync(CancellationToken cancellationToken)
    {
        callOrder.Add("retention-required");
        return RequiredException is null
            ? Task.CompletedTask
            : Task.FromException(RequiredException);
    }

    public Task PruneBestEffortAsync(CancellationToken cancellationToken)
    {
        callOrder.Add("retention-best-effort");
        return Task.CompletedTask;
    }
}
```

Update the harness construction exactly as follows:

```csharp
private Harness()
{
    Time = new MutableTimeProvider(Now);
    TokenProvider = new FakeTokenProvider();
    Trakt = new FakeTraktTvClient();
    Enrichment = new FakeEnrichmentService();
    CallOrder = [];
    Repository = new FakeGenerationRepository(CallOrder);
    Retention = new FakeGenerationRetentionService(CallOrder);
    Service = new TvSyncService(
        TokenProvider,
        Trakt,
        Enrichment,
        Repository,
        Retention,
        new TraktOperationCoordinator(),
        Time,
        TimeSpan.FromDays(1));
    SetUnfinishedProgress();
}

public List<string> CallOrder { get; }

public FakeGenerationRetentionService Retention { get; }
```

Change `FakeGenerationRepository` to accept the shared list:

```csharp
private sealed class FakeGenerationRepository(
    List<string> callOrder) : ITvGenerationRepository
{
    private readonly Dictionary<string, TvGenerationDraft> staged =
        new(StringComparer.Ordinal);

    public List<string> CallOrder { get; } = callOrder;
}
```

Change only the `FakeGenerationRepository` constructor and `CallOrder`
property shown above. Leave its existing `StageAsync`, `PublishAsync`, and
`GetPublishedAsync` method bodies byte-for-byte unchanged; their current
`CallOrder.Add("stage")` and `CallOrder.Add("publish")` statements now write
to the shared list.

- [ ] **Step 2: Add failing API mapping coverage**

Extend `SyncTv_MapsTypedFailuresWithoutLeakingDetails` in `TvSyncApiTests`:

```csharp
[InlineData("retention", HttpStatusCode.ServiceUnavailable, "tv_generation_retention_failed")]
```

Add this switch arm:

```csharp
"retention" => new Watchlist.Application.TvGenerationRetentionException(
    new InvalidOperationException("secret-source-body")),
```

- [ ] **Step 3: Run focused sync and API tests and verify RED**

Run:

```powershell
dotnet test backend\Watchlist.sln --configuration Release --filter "FullyQualifiedName~TvSyncServiceTests|FullyQualifiedName~TvSyncApiTests" --no-restore
```

Expected: failures because `TvSyncService` does not invoke retention and the
exception handler does not map the typed retention failure.

- [ ] **Step 4: Wire configuration policy and services in DI**

After options binding, register the policy:

```csharp
services.AddSingleton(serviceProvider =>
{
    TvGenerationRetentionOptions retention = serviceProvider
        .GetRequiredService<IOptions<TvGenerationRetentionOptions>>()
        .Value;
    return new TvGenerationRetentionPolicy(
        retention.MaxAge,
        retention.MaxGenerations,
        retention.OrphanGracePeriod);
});
```

Register the retention components beside `ITvGenerationRepository`:

```csharp
services.AddSingleton<TvGenerationRetentionPlanner>();
services.AddSingleton<
    ITvGenerationRetentionRepository,
    MongoTvGenerationRetentionRepository>();
services.AddSingleton<
    ITvGenerationRetentionService,
    TvGenerationRetentionService>();
```

Pass `ITvGenerationRetentionService` to `TvSyncService` immediately after the
generation repository. Extend the singleton-lifetime test in `TvOptionsTests`
with:

```csharp
services.Single(descriptor =>
        descriptor.ServiceType == typeof(ITvGenerationRetentionRepository))
    .Lifetime.Should().Be(ServiceLifetime.Singleton);
services.Single(descriptor =>
        descriptor.ServiceType == typeof(ITvGenerationRetentionService))
    .Lifetime.Should().Be(ServiceLifetime.Singleton);
services.Single(descriptor =>
        descriptor.ServiceType == typeof(TvGenerationRetentionPlanner))
    .Lifetime.Should().Be(ServiceLifetime.Singleton);
services.Single(descriptor =>
        descriptor.ServiceType == typeof(TvGenerationRetentionPolicy))
    .Lifetime.Should().Be(ServiceLifetime.Singleton);
```

- [ ] **Step 5: Invoke retention before source access and after publication**

Add `ITvGenerationRetentionService retentionService` to the
`TvSyncService` constructor. Reorder the start of `SyncAsync`:

```csharp
DateTimeOffset startedAt = UtcNow();
string generationId = CreateGenerationId(startedAt);
PublishedTvGeneration? previousGeneration = await generationRepository
    .GetPublishedAsync(cancellationToken)
    .ConfigureAwait(false);
await retentionService.PruneRequiredAsync(cancellationToken)
    .ConfigureAwait(false);
string accessToken = await accessTokenProvider
    .GetValidAccessTokenAsync(cancellationToken)
    .ConfigureAwait(false);
```

Immediately after durable publication add:

```csharp
await generationRepository.PublishAsync(manifest, cancellationToken)
    .ConfigureAwait(false);
await retentionService.PruneBestEffortAsync(cancellationToken)
    .ConfigureAwait(false);
```

- [ ] **Step 6: Map stable runtime and API failures**

Add this `TvSyncHostedService.LogFailure` switch arm before the Mongo arm:

```csharp
TvGenerationRetentionException => "tv_generation_retention_failed",
```

Add this branch near other typed TV failures in
`MongoUnavailableExceptionHandler`:

```csharp
if (exception is TvGenerationRetentionException)
{
    return await WriteTvFailureAsync(
        httpContext,
        StatusCodes.Status503ServiceUnavailable,
        "tv_generation_retention_failed",
        "TV generation retention is temporarily unavailable.",
        cancellationToken);
}
```

- [ ] **Step 7: Run focused tests and verify GREEN**

Run the Step 3 command again.

Expected: all sync and API tests pass, required failure makes zero token,
stage, and publish calls, and successful call order ends with
`retention-best-effort`.

- [ ] **Step 8: Run all retention and TV generation tests**

Run:

```powershell
dotnet test backend\Watchlist.sln --configuration Release --filter "FullyQualifiedName~TvGenerationRetention|FullyQualifiedName~MongoTvGenerationRepositoryTests|FullyQualifiedName~TvSyncScheduleTests|FullyQualifiedName~TvSyncServiceTests|FullyQualifiedName~TvSyncApiTests" --no-restore
```

Expected: all selected tests pass.

- [ ] **Step 9: Commit runtime wiring**

```powershell
git add backend/src/Watchlist.Infrastructure/DependencyInjection.cs backend/src/Watchlist.Application/TvSyncService.cs backend/src/Watchlist.Infrastructure/TvSyncHostedService.cs backend/src/Watchlist.Api/MongoUnavailableExceptionHandler.cs backend/tests/Watchlist.Application.Tests/TvOptionsTests.cs backend/tests/Watchlist.Application.Tests/TvSyncServiceTests.cs backend/tests/Watchlist.Api.Tests/TvSyncApiTests.cs
git commit -m "feat: enforce retention around TV publication"
```

### Task 6: Update Active OKF Documentation

**Files:**
- Modify: `docs/architecture/tv_sync_read_model.md`
- Modify: `docs/systems/backend_service.md`
- Modify: `docs/apis/backend_api.md`
- Modify: `docs/runbooks/tv_sync_operations.md`
- Modify: `docs/runbooks/validation.md`

- [ ] **Step 1: Update architecture and ownership text**

In `docs/architecture/tv_sync_read_model.md`, replace “hourly full-sync
interval” with “six-hour full-sync interval”, set `timestamp` to
`2026-07-29T00:00:00Z`, increment `version` to `1.2.0`, and add this paragraph
after the publication-flow code block:

```markdown
Before source collection, the coordinator runs mandatory TV-generation
retention. After durable publication it runs the same retention best-effort.
The published generation is always protected. Noncurrent scheduled and
activity-full generations share one seven-day, 48-generation bound. Expired
generation rows and lifecycle events are deleted before their manifest.
Legacy rows and malformed or uncertain orphan identities are never selected
for automatic deletion; safely dated abandoned staging rows receive a 24-hour
grace period.
```

In `docs/systems/backend_service.md`, set `timestamp` to
`2026-07-29T00:00:00Z`, increment `version` to `0.5.0`, add a
`TvGenerationRetention` row to the Configuration table, and append the
following paragraph under Persistence:

```markdown
TV-generation persistence is bounded independently of movie persistence.
While the TV operation coordinator is held, mandatory retention runs before
staging and best-effort retention runs after publication. It keeps the
published generation, then keeps noncurrent manifests no older than seven days
up to 48 total generations. It deletes generation-scoped `tv_shows` and
`tv_lifecycle_events` before their manifest and deletes safely identified
manifestless staging rows only after 24 hours. It does not prune
`letterboxd_source_snapshots`, `sync_runs`, legacy TV rows, malformed orphan
identities, or indexes.
```

Use this exact configuration-table row:

```markdown
| `TvGenerationRetention` | Seven-day maximum age, 48 total generations including current, and 24-hour orphan grace period. |
```

- [ ] **Step 2: Update backend API failure behavior**

In `docs/apis/backend_api.md`, set `timestamp` to
`2026-07-29T00:00:00Z`, increment `version` to `0.6.0`, and add this paragraph
after the existing TV typed-dependency-failure paragraph:

```markdown
A mandatory pre-sync retention failure returns redacted `503 Service
Unavailable` with `code=tv_generation_retention_failed`. It occurs before
source access and creates no staged generation or pointer change. Retention
after a durable publication is best-effort: a deferred cleanup is logged as
`tv_generation_retention_deferred`, does not change the successful response,
and is retried before the next staging attempt.
```

- [ ] **Step 3: Update TV operations with rollout checks**

In `docs/runbooks/tv_sync_operations.md`, set `timestamp` to
`2026-07-29T00:00:00Z`, increment `version` to `1.2.0`, and add this section
immediately before `# Evidence`:

````markdown
# TV Generation Retention Rollout

Before the first retention-enabled production deployment:

1. Record `db.runCommand({ atlasSize: 1 })`, manifest count, generation-row
   count, lifecycle-event count, and the published pointer.
2. Set `WATCHLIST_MONGO_URI` only in the current PowerShell process and create a
   restricted local `WATCHLIST_BACKUP_DIR`.
3. Create and verify logical dumps:

   ```powershell
   mongodump --uri="$env:WATCHLIST_MONGO_URI" --db=watchlist --collection=tv_shows --out="$env:WATCHLIST_BACKUP_DIR"
   mongodump --uri="$env:WATCHLIST_MONGO_URI" --db=watchlist --collection=tv_sync_manifests --out="$env:WATCHLIST_BACKUP_DIR"
   mongodump --uri="$env:WATCHLIST_MONGO_URI" --db=watchlist --collection=tv_lifecycle_events --out="$env:WATCHLIST_BACKUP_DIR"
   ```

4. Verify the three BSON files, then clear both environment variables.
5. Deploy the validated release and run one protected TV sync.
6. Verify the published pointer and TV browse, detail, status, and export
   endpoints.
7. Verify no more than 48 manifest documents remain.
8. After Atlas metrics refresh, require total usage below 180 MiB.
9. Observe 24 hours and expect about four scheduled generations plus genuine
   activity-full generations.

Do not use ad hoc production `deleteMany`, convert these collections to TTL,
drop indexes, or run `compact`. Retention starts only through the tested,
deployed backend path after the logical dump is verified.
````

- [ ] **Step 4: Update validation coverage**

In `docs/runbooks/validation.md`, set `timestamp` to
`2026-07-29T00:00:00Z`, increment `version` to `0.5.0`, and append this
paragraph to the Backend section:

```markdown
TV retention coverage must prove the inclusive seven-day boundary, the
48-generation cap including current, deterministic timestamp/ID ordering,
current-pointer protection, 24-hour orphan grace, malformed-orphan
preservation, invalid-plan rejection, pointer-race rejection, acknowledged
child-first deletes, exact-retry idempotency, and partial-cleanup convergence.
Sync coverage must prove the six-hour scheduled default, unchanged five-minute
activity polling, mandatory cleanup before source access or staging, zero
staging after a required-cleanup failure, and successful publication despite a
deferred post-publication cleanup.
```

- [ ] **Step 5: Validate OKF and documentation consistency**

Run:

```powershell
python tests\validate_okf.py
python -m pytest tests\test_tv_destination_plan_docs.py -q
git diff --check
```

Expected: OKF validation passes, documentation tests pass, and
`git diff --check` emits no errors.

- [ ] **Step 6: Commit active documentation**

```powershell
git add docs/architecture/tv_sync_read_model.md docs/systems/backend_service.md docs/apis/backend_api.md docs/runbooks/tv_sync_operations.md docs/runbooks/validation.md
git commit -m "docs: operate bounded TV generation retention"
```

### Task 7: Run Full Verification And Review The Storage Safety Diff

**Files:**
- Verify all files changed by Tasks 1-6.

- [ ] **Step 1: Restore and build the backend**

Run:

```powershell
dotnet restore backend\Watchlist.sln
dotnet build backend\Watchlist.sln --configuration Release --no-restore
```

Expected: restore and Release build succeed with zero errors.

- [ ] **Step 2: Run the complete backend suite against MongoDB 8**

With local MongoDB 8 running on `localhost:27017`, run:

```powershell
dotnet test backend\Watchlist.sln --configuration Release --no-build
```

Expected: the complete backend test suite passes with zero failed tests.

- [ ] **Step 3: Run repository-wide documentation and deployment gates**

Run:

```powershell
python tests\validate_okf.py
python -m pytest tests\test_tv_destination_plan_docs.py -q
python -m pytest tests\deployment -q
```

Expected: all three commands pass.

- [ ] **Step 4: Audit the final diff against the approved exclusions**

Run:

```powershell
git diff 749af53..HEAD --name-only
rg -n -S "CreateIndex|DropIndex|letterboxd_source_snapshots|sync_runs|DeleteMany" backend/src docs
git diff 749af53..HEAD --check
git status --short
```

Confirm:

- no index definition changed;
- no Letterboxd snapshot or sync-run delete path was added;
- only generation-scoped TV rows, events, and manifest documents are deleted;
- all delete filters exclude the current generation;
- the pointer document requires `documentKind=manifest` to be ineligible;
- no credential or production URI appears in the diff; and
- the worktree contains only intentional changes.

- [ ] **Step 5: Record verification evidence**

Add the exact command results to the implementation handoff message. Do not
claim production storage was reduced; only a deployed and observed retention
run can prove that outcome.
