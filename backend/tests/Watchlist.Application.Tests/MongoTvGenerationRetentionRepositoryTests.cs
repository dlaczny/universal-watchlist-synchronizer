using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoTvGenerationRetentionRepositoryTests : IAsyncLifetime
{
    private const string ShowsCollection = "tv_shows";
    private const string ManifestsCollection = "tv_sync_manifests";
    private const string EventsCollection = "tv_lifecycle_events";

    private static readonly DateTimeOffset BaseTime =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly string databaseName = $"watchlist_tv_retention_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly IMongoDatabase database;
    private readonly MongoDbOptions options;

    public MongoTvGenerationRetentionRepositoryTests()
    {
        database = client.GetDatabase(databaseName);
        options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName,
            TvShowsCollectionName = ShowsCollection,
            TvSyncManifestsCollectionName = ManifestsCollection,
            TvLifecycleEventsCollectionName = EventsCollection
        };
    }

    [Fact]
    public async Task ReadSnapshotAsync_ReturnsPointerManifestsAndPhysicalOrphans()
    {
        const string current = "generation-current";
        const string abandoned = "generation-abandoned";
        await InsertManifestsAsync(CreateManifest(current, BaseTime));
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertShowsAsync(
            CreateShow(current, 1),
            CreateShow(abandoned, 2));
        await InsertEventsAsync(CreateEvent(abandoned, 2));

        TvGenerationRetentionSnapshot result =
            await CreateRepository().ReadSnapshotAsync(CancellationToken.None);

        result.CurrentGenerationId.Should().Be(current);
        result.Manifests.Should().Equal(new TvStoredGenerationSummary(current, BaseTime));
        result.OrphanGenerationIds.Should().Equal(abandoned);
    }

    [Fact]
    public async Task ReadSnapshotAsync_IgnoresLegacyShowsAndPointerDocument()
    {
        const string current = "generation-current";
        await InsertManifestsAsync(CreateManifest(current, BaseTime));
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertShowsAsync(CreateLegacyShow("legacy-generation"));

        TvGenerationRetentionSnapshot result =
            await CreateRepository().ReadSnapshotAsync(CancellationToken.None);

        result.CurrentGenerationId.Should().Be(current);
        result.Manifests.Should().ContainSingle()
            .Which.GenerationId.Should().Be(current);
        result.OrphanGenerationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadSnapshotAsync_ArrayDocumentKindContainingGeneration_IsNotPhysical()
    {
        const string current = "generation-current";
        const string expired = "generation-expired";
        BsonDocument malformedShow = CreateShow(expired, 1).ToBsonDocument();
        malformedShow["_id"] = "raw-array-kind-show";
        malformedShow["documentKind"] = new BsonArray(
            [MongoTvShowDocument.GenerationDocumentKind]);
        await InsertManifestsAsync(CreateManifest(current, BaseTime));
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertRawShowsAsync(malformedShow);

        TvGenerationRetentionSnapshot result =
            await CreateRepository().ReadSnapshotAsync(CancellationToken.None);

        result.OrphanGenerationIds.Should().BeEmpty();
        (await CountRawDocumentByIdAsync(ShowsCollection, "raw-array-kind-show"))
            .Should().Be(1);
    }

    [Fact]
    public async Task ReadSnapshotAsync_InvalidPointerShape_FailsClosed()
    {
        await InsertPointerAsync(CreatePointer(
            "generation-current",
            BaseTime,
            "invalid",
            new string('b', 64)));

        Func<Task> action = () =>
            CreateRepository().ReadSnapshotAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tv_generation_retention_pointer_invalid");
    }

    [Fact]
    public async Task ReadSnapshotAsync_ProjectsOnlyManifestRetentionMetadata()
    {
        const string current = "generation-current";
        BsonDocument manifest = CreateManifest(current, BaseTime).ToBsonDocument();
        manifest["lifecycleEventIds"] = new BsonArray([1]);
        await InsertRawManifestsAsync(manifest);

        TvGenerationRetentionSnapshot result =
            await CreateRepository().ReadSnapshotAsync(CancellationToken.None);

        result.Manifests.Should().Equal(new TvStoredGenerationSummary(current, BaseTime));
    }

    [Fact]
    public async Task ReadSnapshotAsync_MalformedPhysicalIdentitiesBecomeOpaqueUncertainTokens()
    {
        const string current = "generation-current";
        string[] malformedKinds =
            ["missing", "null", "empty", "whitespace", "non-string", "array", "empty-array"];
        await InsertManifestsAsync(CreateManifest(current, BaseTime));
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertRawShowsAsync(
            malformedKinds.Select((kind, index) =>
                CreateRawShow($"raw-show-{index}", kind)).ToArray());
        await InsertRawEventsAsync(
            malformedKinds.Select((kind, index) =>
                CreateRawEvent($"raw-event-{index}", kind)).ToArray());
        MongoTvGenerationRetentionRepository repository = CreateRepository();

        TvGenerationRetentionSnapshot snapshot =
            await repository.ReadSnapshotAsync(CancellationToken.None);
        TvGenerationRetentionPlan plan = new TvGenerationRetentionPlanner().Create(
            snapshot,
            new TvGenerationRetentionPolicy(
                TimeSpan.FromDays(7),
                48,
                TimeSpan.FromHours(24)),
            BaseTime);
        TvGenerationRetentionDeleteResult result =
            await repository.ApplyAsync(plan, CancellationToken.None);

        snapshot.OrphanGenerationIds.Should().Equal(
            Enumerable.Range(1, malformedKinds.Length)
                .Select(index => $"invalid-physical-identity-{index:0000}"));
        snapshot.OrphanGenerationIds.Should().NotContain(string.Empty);
        snapshot.OrphanGenerationIds.Should().NotContain(" ");
        plan.UncertainOrphanGenerationIds.Should().Equal(snapshot.OrphanGenerationIds);
        result.Should().Be(new TvGenerationRetentionDeleteResult(0, 0, 0));
        (await CountAllShowsAsync()).Should().Be(malformedKinds.Length);
        (await CountAllEventsAsync()).Should().Be(malformedKinds.Length);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("non-string")]
    public async Task ReadSnapshotAsync_MalformedManifestIdentity_FailsClosedBeforeDelete(
        string malformedKind)
    {
        const string current = "generation-current";
        const string expired = "generation-expired";
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertShowsAsync(CreateShow(expired, 1));
        await InsertEventsAsync(CreateEvent(expired, 1));
        await InsertRawManifestsAsync(
            CreateRawManifest($"raw-manifest-{malformedKind}", malformedKind));

        Func<Task> action = () =>
            CreateRepository().ReadSnapshotAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tv_generation_retention_manifest_invalid");
        (await CountShowsAsync(expired)).Should().Be(1);
        (await CountEventsAsync(expired)).Should().Be(1);
        (await CountAllManifestsAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("array")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("non-string")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("wrong-string")]
    public async Task ReadSnapshotAsync_MalformedManifestDocumentKind_FailsClosedBeforeDelete(
        string malformedKind)
    {
        const string current = "generation-current";
        const string expired = "generation-expired";
        string manifestId = $"raw-manifest-kind-{malformedKind}";
        BsonDocument malformedManifest =
            CreateManifest(expired, BaseTime.AddDays(-8)).ToBsonDocument();
        malformedManifest["_id"] = manifestId;
        SetMalformedManifestDocumentKind(malformedManifest, malformedKind);
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertManifestsAsync(CreateManifest(current, BaseTime));
        await InsertShowsAsync(CreateShow(expired, 1));
        await InsertEventsAsync(CreateEvent(expired, 1));
        await InsertRawManifestsAsync(malformedManifest);

        Func<Task> action = () =>
            CreateRepository().ReadSnapshotAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tv_generation_retention_manifest_invalid");
        (await CountShowsAsync(expired)).Should().Be(1);
        (await CountEventsAsync(expired)).Should().Be(1);
        (await CountRawDocumentByIdAsync(ManifestsCollection, manifestId))
            .Should().Be(1);
        (await CountManifestsAsync(current)).Should().Be(1);
        (await ReadPointerAsync()).GenerationId.Should().Be(current);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("string")]
    [InlineData("default")]
    public async Task ReadSnapshotAsync_InvalidManifestPublishedAt_FailsClosedBeforeDelete(
        string malformedKind)
    {
        const string current = "generation-current";
        const string other = "generation-other";
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertManifestsAsync(CreateManifest(current, BaseTime));
        await InsertShowsAsync(CreateShow(current, 1), CreateShow(other, 2));
        await InsertEventsAsync(CreateEvent(current, 1), CreateEvent(other, 2));
        await InsertRawManifestsAsync(
            CreateRawManifestWithInvalidPublishedAt(
                "raw-manifest-invalid-published-at",
                other,
                malformedKind));

        Func<Task> action = () =>
            CreateRepository().ReadSnapshotAsync(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tv_generation_retention_manifest_invalid");
        (await CountShowsAsync(current)).Should().Be(1);
        (await CountShowsAsync(other)).Should().Be(1);
        (await CountEventsAsync(current)).Should().Be(1);
        (await CountEventsAsync(other)).Should().Be(1);
        (await CountManifestsAsync(current)).Should().Be(1);
        (await CountManifestsAsync(other)).Should().Be(1);
        (await ReadPointerAsync()).GenerationId.Should().Be(current);
    }

    [Fact]
    public async Task ApplyAsync_DeletesChildrenBeforeManifestAndPreservesPointerAndLegacy()
    {
        const string current = "generation-current";
        const string expired = "generation-expired";
        const string orphan = "generation-orphan";
        await InsertManifestsAsync(
            CreateManifest(current, BaseTime),
            CreateManifest(expired, BaseTime.AddDays(-8)));
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertShowsAsync(
            CreateShow(current, 1),
            CreateShow(expired, 2),
            CreateShow(orphan, 3),
            CreateLegacyShow("legacy-generation"));
        await InsertEventsAsync(
            CreateEvent(current, 1),
            CreateEvent(expired, 2),
            CreateEvent(orphan, 3));
        TvGenerationRetentionPlan plan = CreatePlan(
            current,
            [current],
            [expired],
            [orphan]);

        TvGenerationRetentionDeleteResult result =
            await CreateRepository().ApplyAsync(plan, CancellationToken.None);

        result.Should().Be(new TvGenerationRetentionDeleteResult(2, 2, 1));
        (await CountShowsAsync(current)).Should().Be(1);
        (await CountEventsAsync(current)).Should().Be(1);
        (await CountManifestsAsync(current)).Should().Be(1);
        (await CountPointersAsync()).Should().Be(1);
        (await CountLegacyShowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_ArrayIdentityMatchingExpiredId_IsNotDeleted()
    {
        const string current = "generation-current";
        const string expired = "generation-expired";
        BsonDocument malformedShow = CreateRawShow("raw-array-show", "array");
        BsonDocument malformedEvent = CreateRawEvent("raw-array-event", "array");
        BsonDocument malformedManifest =
            CreateRawManifest("raw-array-manifest", "array");
        malformedShow["generationId"] = new BsonArray([expired]);
        malformedEvent["generationId"] = new BsonArray([expired]);
        malformedManifest["generationId"] = new BsonArray([expired]);
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertManifestsAsync(CreateManifest(expired, BaseTime.AddDays(-8)));
        await InsertShowsAsync(CreateShow(expired, 1));
        await InsertEventsAsync(CreateEvent(expired, 1));
        await InsertRawShowsAsync(malformedShow);
        await InsertRawEventsAsync(malformedEvent);
        await InsertRawManifestsAsync(malformedManifest);
        TvGenerationRetentionPlan plan = CreatePlan(
            current,
            [current],
            [expired],
            []);

        TvGenerationRetentionDeleteResult result =
            await CreateRepository().ApplyAsync(plan, CancellationToken.None);

        result.Should().Be(new TvGenerationRetentionDeleteResult(1, 1, 1));
        (await CountRawDocumentByIdAsync(ShowsCollection, "raw-array-show"))
            .Should().Be(1);
        (await CountRawDocumentByIdAsync(EventsCollection, "raw-array-event"))
            .Should().Be(1);
        (await CountRawDocumentByIdAsync(ManifestsCollection, "raw-array-manifest"))
            .Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_ArrayDocumentKindContainingGeneration_IsNotDeleted()
    {
        const string current = "generation-current";
        const string expired = "generation-expired";
        BsonDocument malformedShow = CreateShow(expired, 1).ToBsonDocument();
        malformedShow["_id"] = "raw-array-kind-show";
        malformedShow["documentKind"] = new BsonArray(
            [MongoTvShowDocument.GenerationDocumentKind]);
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertRawShowsAsync(malformedShow);
        TvGenerationRetentionPlan plan = CreatePlan(
            current,
            [current],
            [],
            [expired]);

        TvGenerationRetentionDeleteResult result =
            await CreateRepository().ApplyAsync(plan, CancellationToken.None);

        result.Should().Be(new TvGenerationRetentionDeleteResult(0, 0, 0));
        (await CountRawDocumentByIdAsync(ShowsCollection, "raw-array-kind-show"))
            .Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_ArrayDocumentKindContainingManifest_IsNotDeleted()
    {
        const string current = "generation-current";
        const string expired = "generation-expired";
        BsonDocument malformedManifest =
            CreateManifest(expired, BaseTime.AddDays(-8)).ToBsonDocument();
        malformedManifest["_id"] = "raw-array-kind-manifest";
        malformedManifest["documentKind"] = new BsonArray(
            [MongoTvSyncManifestDocument.ManifestDocumentKind]);
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertManifestsAsync(CreateManifest(expired, BaseTime.AddDays(-8)));
        await InsertRawManifestsAsync(malformedManifest);
        TvGenerationRetentionPlan plan = CreatePlan(
            current,
            [current],
            [expired],
            []);

        TvGenerationRetentionDeleteResult result =
            await CreateRepository().ApplyAsync(plan, CancellationToken.None);

        result.Should().Be(new TvGenerationRetentionDeleteResult(0, 0, 1));
        (await CountRawDocumentByIdAsync(
                ManifestsCollection,
                "raw-array-kind-manifest"))
            .Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_WhenPointerChanged_DeletesNothing()
    {
        const string expected = "generation-expected";
        const string actual = "generation-actual";
        const string expired = "generation-expired";
        await InsertPointerAsync(CreatePointer(actual, BaseTime));
        await InsertManifestsAsync(CreateManifest(expired, BaseTime.AddDays(-8)));
        await InsertShowsAsync(CreateShow(expired, 1));
        await InsertEventsAsync(CreateEvent(expired, 1));
        TvGenerationRetentionPlan plan = CreatePlan(
            expected,
            [expected],
            [expired],
            []);

        Func<Task> action = () =>
            CreateRepository().ApplyAsync(plan, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tv_generation_retention_pointer_changed");
        (await CountShowsAsync(expired)).Should().Be(1);
        (await CountEventsAsync(expired)).Should().Be(1);
        (await CountManifestsAsync(expired)).Should().Be(1);
        (await CountPointersAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_WhenPointerShapeInvalid_DeletesNothing()
    {
        const string current = "generation-current";
        const string expired = "generation-expired";
        await InsertPointerAsync(CreatePointer(
            current,
            BaseTime,
            new string('a', 64),
            "invalid"));
        await InsertManifestsAsync(CreateManifest(expired, BaseTime.AddDays(-8)));
        await InsertShowsAsync(CreateShow(expired, 1));
        await InsertEventsAsync(CreateEvent(expired, 1));
        TvGenerationRetentionPlan plan = CreatePlan(
            current,
            [current],
            [expired],
            []);

        Func<Task> action = () =>
            CreateRepository().ApplyAsync(plan, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tv_generation_retention_pointer_invalid");
        (await CountShowsAsync(expired)).Should().Be(1);
        (await CountEventsAsync(expired)).Should().Be(1);
        (await CountManifestsAsync(expired)).Should().Be(1);
        (await CountPointersAsync()).Should().Be(1);
    }

    public static TheoryData<TvGenerationRetentionPlan> InvalidPlans()
    {
        return new TheoryData<TvGenerationRetentionPlan>
        {
            CreatePlan(null, [], ["expired-manifest"], []),
            CreatePlan("current", [], [], []),
            CreatePlan("current", ["current", "overlap"], ["overlap"], []),
            CreatePlan("current", ["current", "overlap"], [], ["overlap"]),
            CreatePlan("current", ["current"], ["overlap"], ["overlap"]),
            CreatePlan("current", ["current", "overlap"], ["expired-manifest"], [],
                ["overlap"], []),
            CreatePlan("current", ["current", "overlap"], ["expired-manifest"], [],
                [], ["overlap"]),
            CreatePlan("current", ["current"], ["overlap"], [],
                ["overlap"], []),
            CreatePlan("current", ["current"], ["overlap"], [],
                [], ["overlap"]),
            CreatePlan("current", ["current"], ["expired-manifest"], ["overlap"],
                ["overlap"], []),
            CreatePlan("current", ["current"], ["expired-manifest"], ["overlap"],
                [], ["overlap"]),
            CreatePlan("current", ["current"], ["expired-manifest"], [],
                ["overlap"], ["overlap"])
        };
    }

    public static TheoryData<TvGenerationRetentionPlan> MalformedIdPlans()
    {
        TheoryData<TvGenerationRetentionPlan> plans = [];
        foreach (string malformedId in new[] { string.Empty, " ", null! })
        {
            plans.Add(CreatePlan(
                "current",
                ["current", malformedId],
                ["expired-manifest"],
                []));
            plans.Add(CreatePlan(
                "current",
                ["current"],
                ["expired-manifest", malformedId],
                []));
            plans.Add(CreatePlan(
                "current",
                ["current"],
                ["expired-manifest"],
                [malformedId]));
            plans.Add(CreatePlan(
                "current",
                ["current"],
                ["expired-manifest"],
                [],
                [malformedId],
                []));
            plans.Add(CreatePlan(
                "current",
                ["current"],
                ["expired-manifest"],
                [],
                [],
                [malformedId]));
        }

        plans.Add(CreatePlan(
            string.Empty,
            [string.Empty],
            ["expired-manifest"],
            []));
        plans.Add(CreatePlan(
            " ",
            [" "],
            ["expired-manifest"],
            []));
        return plans;
    }

    [Theory]
    [MemberData(nameof(InvalidPlans))]
    public async Task ApplyAsync_OverlappingOrDestructivePointerlessPlan_RejectsBeforeDelete(
        TvGenerationRetentionPlan plan)
    {
        string[] manifestIds = plan.ExpiredManifestGenerationIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] childIds = manifestIds
            .Concat(plan.ExpiredOrphanGenerationIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await InsertManifestsAsync(
            manifestIds.Select(id => CreateManifest(id, BaseTime.AddDays(-8))).ToArray());
        await InsertShowsAsync(
            childIds.Select((id, index) => CreateShow(id, index + 1)).ToArray());
        await InsertEventsAsync(
            childIds.Select((id, index) => CreateEvent(id, index + 1)).ToArray());
        if (plan.ExpectedCurrentGenerationId is not null
            && !string.IsNullOrWhiteSpace(plan.ExpectedCurrentGenerationId))
        {
            await InsertPointerAsync(CreatePointer(
                plan.ExpectedCurrentGenerationId,
                BaseTime));
        }

        Func<Task> action = () =>
            CreateRepository().ApplyAsync(plan, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tv_generation_retention_plan_invalid");
        (await CountAllShowsAsync()).Should().Be(childIds.Length);
        (await CountAllEventsAsync()).Should().Be(childIds.Length);
        (await CountAllManifestsAsync()).Should().Be(manifestIds.Length);
    }

    [Theory]
    [MemberData(nameof(MalformedIdPlans))]
    public async Task ApplyAsync_MalformedPlanId_RejectsBeforeDelete(
        TvGenerationRetentionPlan plan)
    {
        const string current = "current";
        const string expired = "expired-manifest";
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertManifestsAsync(CreateManifest(expired, BaseTime.AddDays(-8)));
        await InsertShowsAsync(CreateShow(expired, 1));
        await InsertEventsAsync(CreateEvent(expired, 1));

        Func<Task> action = () =>
            CreateRepository().ApplyAsync(plan, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tv_generation_retention_plan_invalid");
        (await CountShowsAsync(expired)).Should().Be(1);
        (await CountEventsAsync(expired)).Should().Be(1);
        (await CountManifestsAsync(expired)).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_ExactRetry_IsIdempotent()
    {
        const string current = "generation-current";
        const string expired = "generation-expired";
        const string orphan = "generation-orphan";
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertManifestsAsync(CreateManifest(expired, BaseTime.AddDays(-8)));
        await InsertShowsAsync(CreateShow(expired, 1), CreateShow(orphan, 2));
        await InsertEventsAsync(CreateEvent(expired, 1), CreateEvent(orphan, 2));
        TvGenerationRetentionPlan plan = CreatePlan(
            current,
            [current],
            [expired],
            [orphan]);
        MongoTvGenerationRetentionRepository repository = CreateRepository();

        TvGenerationRetentionDeleteResult first =
            await repository.ApplyAsync(plan, CancellationToken.None);
        TvGenerationRetentionDeleteResult second =
            await repository.ApplyAsync(plan, CancellationToken.None);

        first.Should().Be(new TvGenerationRetentionDeleteResult(2, 2, 1));
        second.Should().Be(new TvGenerationRetentionDeleteResult(0, 0, 0));
    }

    [Fact]
    public async Task ApplyAsync_AfterChildFirstPartialCleanup_Converges()
    {
        const string current = "generation-current";
        const string expired = "generation-expired";
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertManifestsAsync(CreateManifest(expired, BaseTime.AddDays(-8)));
        await InsertShowsAsync(CreateShow(expired, 1));
        await InsertEventsAsync(CreateEvent(expired, 1));
        await database.GetCollection<MongoTvShowDocument>(ShowsCollection)
            .DeleteOneAsync(document => document.GenerationId == expired);
        TvGenerationRetentionPlan plan = CreatePlan(
            current,
            [current],
            [expired],
            []);

        TvGenerationRetentionDeleteResult result =
            await CreateRepository().ApplyAsync(plan, CancellationToken.None);

        result.Should().Be(new TvGenerationRetentionDeleteResult(0, 1, 1));
        (await CountShowsAsync(expired)).Should().Be(0);
        (await CountEventsAsync(expired)).Should().Be(0);
        (await CountManifestsAsync(expired)).Should().Be(0);
    }

    [Fact]
    public async Task PlannerAndApplyAsync_ActivityBurst_LeavesAtMost48Manifests()
    {
        const int generationCount = 50;
        string[] generationIds = Enumerable.Range(0, generationCount)
            .Select(index => CreateProductionGenerationId(BaseTime.AddMinutes(-index), index))
            .ToArray();
        string current = generationIds[0];
        await InsertManifestsAsync(
            generationIds.Select((id, index) =>
                CreateManifest(id, BaseTime.AddMinutes(-index))).ToArray());
        await InsertShowsAsync(
            generationIds.Select((id, index) => CreateShow(id, index + 1)).ToArray());
        await InsertEventsAsync(
            generationIds.Select((id, index) => CreateEvent(id, index + 1)).ToArray());
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        MongoTvGenerationRetentionRepository repository = CreateRepository();
        TvGenerationRetentionSnapshot snapshot =
            await repository.ReadSnapshotAsync(CancellationToken.None);
        TvGenerationRetentionPlan plan = new TvGenerationRetentionPlanner().Create(
            snapshot,
            new TvGenerationRetentionPolicy(
                TimeSpan.FromDays(7),
                48,
                TimeSpan.FromHours(24)),
            BaseTime);

        await repository.ApplyAsync(plan, CancellationToken.None);

        (await CountAllManifestsAsync()).Should().Be(48);
        (await ReadPointerAsync()).GenerationId.Should().Be(current);
        (await CountShowsAsync(current)).Should().Be(1);
        (await CountEventsAsync(current)).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_WhenDeletionListsIncludeCurrent_RejectsAndPreservesCurrent()
    {
        const string current = "generation-current";
        await InsertPointerAsync(CreatePointer(current, BaseTime));
        await InsertManifestsAsync(CreateManifest(current, BaseTime));
        await InsertShowsAsync(CreateShow(current, 1));
        await InsertEventsAsync(CreateEvent(current, 1));
        TvGenerationRetentionPlan malicious = CreatePlan(
            current,
            [current],
            [current],
            [current]);

        Func<Task> action = () =>
            CreateRepository().ApplyAsync(malicious, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("tv_generation_retention_plan_invalid");
        (await CountShowsAsync(current)).Should().Be(1);
        (await CountEventsAsync(current)).Should().Be(1);
        (await CountManifestsAsync(current)).Should().Be(1);
        (await ReadPointerAsync()).GenerationId.Should().Be(current);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }

    private MongoTvGenerationRetentionRepository CreateRepository()
    {
        return new MongoTvGenerationRetentionRepository(database, Options.Create(options));
    }

    private async Task InsertShowsAsync(params MongoTvShowDocument[] documents)
    {
        if (documents.Length > 0)
        {
            await database.GetCollection<MongoTvShowDocument>(ShowsCollection)
                .InsertManyAsync(documents);
        }
    }

    private async Task InsertEventsAsync(params MongoTvLifecycleEventDocument[] documents)
    {
        if (documents.Length > 0)
        {
            await database.GetCollection<MongoTvLifecycleEventDocument>(EventsCollection)
                .InsertManyAsync(documents);
        }
    }

    private async Task InsertManifestsAsync(params MongoTvSyncManifestDocument[] documents)
    {
        if (documents.Length > 0)
        {
            await database.GetCollection<MongoTvSyncManifestDocument>(ManifestsCollection)
                .InsertManyAsync(documents);
        }
    }

    private Task InsertPointerAsync(MongoTvPublishedPointerDocument document)
    {
        return database.GetCollection<MongoTvPublishedPointerDocument>(ManifestsCollection)
            .InsertOneAsync(document);
    }

    private async Task InsertRawShowsAsync(params BsonDocument[] documents)
    {
        await database.GetCollection<BsonDocument>(ShowsCollection)
            .InsertManyAsync(documents);
    }

    private async Task InsertRawEventsAsync(params BsonDocument[] documents)
    {
        await database.GetCollection<BsonDocument>(EventsCollection)
            .InsertManyAsync(documents);
    }

    private async Task InsertRawManifestsAsync(params BsonDocument[] documents)
    {
        await database.GetCollection<BsonDocument>(ManifestsCollection)
            .InsertManyAsync(documents);
    }

    private Task<long> CountShowsAsync(string generationId)
    {
        return database.GetCollection<MongoTvShowDocument>(ShowsCollection)
            .CountDocumentsAsync(document =>
                document.DocumentKind == MongoTvShowDocument.GenerationDocumentKind
                && document.GenerationId == generationId);
    }

    private Task<long> CountLegacyShowsAsync()
    {
        return database.GetCollection<MongoTvShowDocument>(ShowsCollection)
            .CountDocumentsAsync(document =>
                document.DocumentKind == MongoTvShowDocument.LegacyDocumentKind);
    }

    private Task<long> CountEventsAsync(string generationId)
    {
        return database.GetCollection<MongoTvLifecycleEventDocument>(EventsCollection)
            .CountDocumentsAsync(document => document.GenerationId == generationId);
    }

    private Task<long> CountManifestsAsync(string generationId)
    {
        return database.GetCollection<MongoTvSyncManifestDocument>(ManifestsCollection)
            .CountDocumentsAsync(document =>
                document.DocumentKind == MongoTvSyncManifestDocument.ManifestDocumentKind
                && document.GenerationId == generationId);
    }

    private Task<long> CountPointersAsync()
    {
        return database.GetCollection<MongoTvPublishedPointerDocument>(ManifestsCollection)
            .CountDocumentsAsync(document =>
                document.Id == MongoTvPublishedPointerDocument.PublishedPointerId);
    }

    private Task<long> CountAllShowsAsync()
    {
        return database.GetCollection<MongoTvShowDocument>(ShowsCollection)
            .CountDocumentsAsync(Builders<MongoTvShowDocument>.Filter.Empty);
    }

    private Task<long> CountAllEventsAsync()
    {
        return database.GetCollection<MongoTvLifecycleEventDocument>(EventsCollection)
            .CountDocumentsAsync(Builders<MongoTvLifecycleEventDocument>.Filter.Empty);
    }

    private Task<long> CountAllManifestsAsync()
    {
        return database.GetCollection<MongoTvSyncManifestDocument>(ManifestsCollection)
            .CountDocumentsAsync(document =>
                document.DocumentKind == MongoTvSyncManifestDocument.ManifestDocumentKind);
    }

    private Task<long> CountRawDocumentByIdAsync(string collectionName, string id)
    {
        return database.GetCollection<BsonDocument>(collectionName)
            .CountDocumentsAsync(new BsonDocument("_id", id));
    }

    private Task<MongoTvPublishedPointerDocument> ReadPointerAsync()
    {
        return database.GetCollection<MongoTvPublishedPointerDocument>(ManifestsCollection)
            .Find(document => document.Id == MongoTvPublishedPointerDocument.PublishedPointerId)
            .SingleAsync();
    }

    private static MongoTvShowDocument CreateShow(string generationId, int suffix)
    {
        return new MongoTvShowDocument
        {
            Id = $"generation:{generationId}:{suffix}",
            DocumentKind = MongoTvShowDocument.GenerationDocumentKind,
            GenerationId = generationId,
            TraktId = suffix
        };
    }

    private static MongoTvShowDocument CreateLegacyShow(string generationId)
    {
        return new MongoTvShowDocument
        {
            Id = $"legacy:{generationId}",
            DocumentKind = MongoTvShowDocument.LegacyDocumentKind,
            GenerationId = generationId,
            TraktId = 999
        };
    }

    private static MongoTvLifecycleEventDocument CreateEvent(string generationId, int suffix)
    {
        return new MongoTvLifecycleEventDocument
        {
            Id = $"generation:{generationId}:event-{suffix}",
            EventId = $"event-{suffix}",
            TraktId = suffix,
            LifecycleVersion = 1,
            GenerationId = generationId,
            EventType = "added",
            OccurredAt = BaseTime,
            PredicateHash = new string('c', 64),
            Reason = "synthetic-retention-test"
        };
    }

    private static MongoTvSyncManifestDocument CreateManifest(
        string generationId,
        DateTimeOffset publishedAt)
    {
        return new MongoTvSyncManifestDocument
        {
            Id = $"generation:{generationId}",
            DocumentKind = MongoTvSyncManifestDocument.ManifestDocumentKind,
            GenerationId = generationId,
            StartedAt = publishedAt.AddMinutes(-1),
            CompletedAt = publishedAt,
            PublishedAt = publishedAt,
            MembershipHash = new string('a', 64),
            ProgressHash = new string('b', 64)
        };
    }

    private static MongoTvPublishedPointerDocument CreatePointer(
        string generationId,
        DateTimeOffset publishedAt,
        string? membershipHash = null,
        string? progressHash = null)
    {
        return new MongoTvPublishedPointerDocument
        {
            Id = MongoTvPublishedPointerDocument.PublishedPointerId,
            DocumentKind = MongoTvPublishedPointerDocument.PointerDocumentKind,
            GenerationId = generationId,
            ManifestId = $"generation:{generationId}",
            ShowCount = 1,
            LifecycleEventCount = 1,
            MembershipHash = membershipHash ?? new string('a', 64),
            ProgressHash = progressHash ?? new string('b', 64),
            PublishedAt = publishedAt
        };
    }

    private static BsonDocument CreateRawShow(string id, string malformedKind)
    {
        BsonDocument document = CreateShow("placeholder", 1).ToBsonDocument();
        document["_id"] = id;
        SetMalformedGenerationId(document, malformedKind);
        return document;
    }

    private static BsonDocument CreateRawEvent(string id, string malformedKind)
    {
        BsonDocument document = CreateEvent("placeholder", 1).ToBsonDocument();
        document["_id"] = id;
        SetMalformedGenerationId(document, malformedKind);
        return document;
    }

    private static BsonDocument CreateRawManifest(string id, string malformedKind)
    {
        BsonDocument document = CreateManifest("placeholder", BaseTime).ToBsonDocument();
        document["_id"] = id;
        SetMalformedGenerationId(document, malformedKind);
        return document;
    }

    private static BsonDocument CreateRawManifestWithInvalidPublishedAt(
        string id,
        string generationId,
        string malformedKind)
    {
        BsonDocument document = CreateManifest(generationId, BaseTime).ToBsonDocument();
        document["_id"] = id;
        switch (malformedKind)
        {
            case "missing":
                document.Remove("publishedAt");
                break;
            case "null":
                document["publishedAt"] = BsonNull.Value;
                break;
            case "string":
                document["publishedAt"] = "not-a-timestamp";
                break;
            case "default":
                document["publishedAt"] = new BsonArray([0L, 0]);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(malformedKind),
                    malformedKind,
                    "Unknown malformed published timestamp fixture.");
        }

        return document;
    }

    private static void SetMalformedManifestDocumentKind(
        BsonDocument document,
        string malformedKind)
    {
        switch (malformedKind)
        {
            case "array":
                document["documentKind"] = new BsonArray(
                    [MongoTvSyncManifestDocument.ManifestDocumentKind]);
                break;
            case "missing":
                document.Remove("documentKind");
                break;
            case "null":
                document["documentKind"] = BsonNull.Value;
                break;
            case "non-string":
                document["documentKind"] = 42;
                break;
            case "empty":
                document["documentKind"] = string.Empty;
                break;
            case "whitespace":
                document["documentKind"] = " ";
                break;
            case "wrong-string":
                document["documentKind"] =
                    MongoTvShowDocument.GenerationDocumentKind;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(malformedKind),
                    malformedKind,
                    "Unknown malformed manifest document kind fixture.");
        }
    }

    private static void SetMalformedGenerationId(
        BsonDocument document,
        string malformedKind)
    {
        switch (malformedKind)
        {
            case "missing":
                document.Remove("generationId");
                break;
            case "null":
                document["generationId"] = BsonNull.Value;
                break;
            case "empty":
                document["generationId"] = string.Empty;
                break;
            case "whitespace":
                document["generationId"] = " ";
                break;
            case "non-string":
                document["generationId"] = 42;
                break;
            case "array":
                document["generationId"] = new BsonArray(
                    ["tv-20260730120000000-11111111111111111111111111111111"]);
                break;
            case "empty-array":
                document["generationId"] = new BsonArray();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(malformedKind),
                    malformedKind,
                    "Unknown malformed generation identity fixture.");
        }
    }

    private static TvGenerationRetentionPlan CreatePlan(
        string? expectedCurrentGenerationId,
        IReadOnlyList<string> retainedGenerationIds,
        IReadOnlyList<string> expiredManifestGenerationIds,
        IReadOnlyList<string> expiredOrphanGenerationIds,
        IReadOnlyList<string>? deferredOrphanGenerationIds = null,
        IReadOnlyList<string>? uncertainOrphanGenerationIds = null)
    {
        return new TvGenerationRetentionPlan(
            expectedCurrentGenerationId,
            retainedGenerationIds,
            expiredManifestGenerationIds,
            expiredOrphanGenerationIds,
            deferredOrphanGenerationIds ?? [],
            uncertainOrphanGenerationIds ?? []);
    }

    private static string CreateProductionGenerationId(
        DateTimeOffset createdAt,
        int suffix)
    {
        return FormattableString.Invariant($"tv-{createdAt:yyyyMMddHHmmssfff}-{suffix:x32}");
    }
}
