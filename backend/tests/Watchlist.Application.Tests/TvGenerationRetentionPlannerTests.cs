using System.Globalization;
using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class TvGenerationRetentionPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 29, 16, 0, 0, TimeSpan.Zero);
    private static readonly TvGenerationRetentionPolicy DefaultPolicy = new(
        TimeSpan.FromDays(7),
        48,
        TimeSpan.FromHours(24));

    private readonly TvGenerationRetentionPlanner planner = new();

    [Fact]
    public void Create_CurrentManifestOlderThanMaximumAge_RetainsCurrent()
    {
        TvStoredGenerationSummary current = CreateManifest(Now.AddDays(-30), 1);
        TvGenerationRetentionSnapshot snapshot = new(current.GenerationId, [current], []);

        TvGenerationRetentionPlan result = planner.Create(snapshot, DefaultPolicy, Now);

        result.ExpectedCurrentGenerationId.Should().Be(current.GenerationId);
        result.RetainedGenerationIds.Should().Equal(current.GenerationId);
        result.ExpiredManifestGenerationIds.Should().BeEmpty();
    }

    [Fact]
    public void Create_SixtyManifestsInsideWindow_RetainsNewestFortyEightIncludingCurrent()
    {
        TvStoredGenerationSummary[] manifests = Enumerable.Range(0, 60)
            .Select(index => CreateManifest(Now.AddMinutes(-index), index))
            .ToArray();
        TvGenerationRetentionSnapshot snapshot = new(
            manifests[0].GenerationId,
            manifests.Reverse().ToArray(),
            []);

        TvGenerationRetentionPlan result = planner.Create(snapshot, DefaultPolicy, Now);

        result.RetainedGenerationIds.Should().HaveCount(48);
        result.RetainedGenerationIds.Should().Contain(manifests[0].GenerationId);
        result.ExpiredManifestGenerationIds.Should().HaveCount(12);
        result.RetainedGenerationIds
            .Intersect(result.ExpiredManifestGenerationIds, StringComparer.Ordinal)
            .Should()
            .BeEmpty();
        result.RetainedGenerationIds
            .Concat(result.ExpiredManifestGenerationIds)
            .Should()
            .BeEquivalentTo(manifests.Select(item => item.GenerationId));
    }

    [Fact]
    public void Create_ManifestAtMaximumAgeBoundary_RetainsBoundaryAndExpiresOneTickOlder()
    {
        TvStoredGenerationSummary current = CreateManifest(Now, 1);
        TvStoredGenerationSummary boundary = CreateManifest(Now.AddDays(-7), 2);
        TvStoredGenerationSummary older = CreateManifest(Now.AddDays(-7).AddTicks(-1), 3);
        TvGenerationRetentionSnapshot snapshot = new(
            current.GenerationId,
            [older, current, boundary],
            []);

        TvGenerationRetentionPlan result = planner.Create(snapshot, DefaultPolicy, Now);

        result.RetainedGenerationIds.Should().BeEquivalentTo(
            [current.GenerationId, boundary.GenerationId]);
        result.ExpiredManifestGenerationIds.Should().Equal(older.GenerationId);
    }

    [Fact]
    public void Create_EqualPublishedTimes_RetainsOrdinallyHigherGenerationBeforeLower()
    {
        string timestamp = Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        TvStoredGenerationSummary current = new($"tv-{timestamp}-{new string('b', 32)}", Now);
        TvStoredGenerationSummary higher = new($"tv-{timestamp}-{new string('c', 32)}", Now);
        TvStoredGenerationSummary lower = new($"tv-{timestamp}-{new string('a', 32)}", Now);
        TvGenerationRetentionPolicy policy = new(
            TimeSpan.FromDays(7),
            2,
            TimeSpan.FromHours(24));
        TvGenerationRetentionSnapshot snapshot = new(
            current.GenerationId,
            [lower, current, higher],
            []);

        TvGenerationRetentionPlan result = planner.Create(snapshot, policy, Now);

        result.RetainedGenerationIds.Should().BeEquivalentTo(
            [current.GenerationId, higher.GenerationId]);
        result.ExpiredManifestGenerationIds.Should().Equal(lower.GenerationId);
    }

    [Fact]
    public void Create_OrphansAtAndInsideGraceBoundary_ClassifiesSafely()
    {
        string expired = CreateGenerationId(Now.AddHours(-24), 1);
        string deferred = CreateGenerationId(Now.AddHours(-24).AddMilliseconds(1), 2);
        const string malformed = "generation-test-fixture";
        TvStoredGenerationSummary current = CreateManifest(Now, 3);
        TvGenerationRetentionSnapshot snapshot = new(
            current.GenerationId,
            [current],
            [malformed, deferred, expired]);

        TvGenerationRetentionPlan result = planner.Create(snapshot, DefaultPolicy, Now);

        result.ExpiredOrphanGenerationIds.Should().Equal(expired);
        result.DeferredOrphanGenerationIds.Should().Equal(deferred);
        result.UncertainOrphanGenerationIds.Should().Equal(malformed);
    }

    [Fact]
    public void Create_ProductionLookingOrphanIdsWithTrailingCharacters_MarksEachUncertain()
    {
        string productionId = CreateGenerationId(Now.AddDays(-2), 1);
        string trailingLineFeed = productionId + "\n";
        string trailingWhitespace = productionId + " ";
        TvStoredGenerationSummary current = CreateManifest(Now, 2);
        TvGenerationRetentionSnapshot snapshot = new(
            current.GenerationId,
            [current],
            [trailingWhitespace, trailingLineFeed]);

        TvGenerationRetentionPlan result = planner.Create(snapshot, DefaultPolicy, Now);

        result.ExpiredOrphanGenerationIds.Should().BeEmpty();
        result.DeferredOrphanGenerationIds.Should().BeEmpty();
        result.UncertainOrphanGenerationIds.Should().Equal(
            new[] { trailingLineFeed, trailingWhitespace }.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Create_ProductionLookingOrphanIdsWithUppercaseHexOrInvalidTimestamp_MarksEachUncertain()
    {
        string uppercaseHex = $"tv-20260727160000000-{new string('A', 32)}";
        string invalidTimestamp = $"tv-20260230160000000-{new string('0', 32)}";
        TvStoredGenerationSummary current = CreateManifest(Now, 1);
        TvGenerationRetentionSnapshot snapshot = new(
            current.GenerationId,
            [current],
            [uppercaseHex, invalidTimestamp]);

        TvGenerationRetentionPlan result = planner.Create(snapshot, DefaultPolicy, Now);

        result.ExpiredOrphanGenerationIds.Should().BeEmpty();
        result.DeferredOrphanGenerationIds.Should().BeEmpty();
        result.UncertainOrphanGenerationIds.Should().Equal(
            new[] { invalidTimestamp, uppercaseHex }.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Create_CurrentManifestAlsoListedAsOrphan_RejectsSnapshot()
    {
        TvStoredGenerationSummary current = CreateManifest(Now, 1);
        TvGenerationRetentionSnapshot snapshot = new(
            current.GenerationId,
            [current],
            [current.GenerationId]);

        Action action = () => planner.Create(snapshot, DefaultPolicy, Now);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("tv_generation_retention_orphan_manifest_overlap");
    }

    [Fact]
    public void Create_NoncurrentManifestAlsoListedAsOrphan_RejectsSnapshot()
    {
        TvStoredGenerationSummary current = CreateManifest(Now, 1);
        TvStoredGenerationSummary other = CreateManifest(Now.AddMinutes(-1), 2);
        TvGenerationRetentionSnapshot snapshot = new(
            current.GenerationId,
            [current, other],
            [other.GenerationId]);

        Action action = () => planner.Create(snapshot, DefaultPolicy, Now);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("tv_generation_retention_orphan_manifest_overlap");
    }

    [Fact]
    public void Create_NoCurrentPointer_PreservesAllDataAndMarksEveryOrphanUncertain()
    {
        TvStoredGenerationSummary first = CreateManifest(Now.AddDays(-30), 1);
        TvStoredGenerationSummary second = CreateManifest(Now, 2);
        TvStoredGenerationSummary third = CreateManifest(Now, 3);
        string oldOrphan = CreateGenerationId(Now.AddDays(-10), 4);
        const string malformed = "generation-test-fixture";
        TvGenerationRetentionSnapshot snapshot = new(
            null,
            [first, second, third],
            [oldOrphan, malformed, oldOrphan]);

        TvGenerationRetentionPlan result = planner.Create(snapshot, DefaultPolicy, Now);

        result.ExpectedCurrentGenerationId.Should().BeNull();
        result.RetainedGenerationIds.Should().Equal(
            third.GenerationId,
            second.GenerationId,
            first.GenerationId);
        result.ExpiredManifestGenerationIds.Should().BeEmpty();
        result.ExpiredOrphanGenerationIds.Should().BeEmpty();
        result.DeferredOrphanGenerationIds.Should().BeEmpty();
        result.UncertainOrphanGenerationIds.Should().Equal(
            new[] { oldOrphan, malformed }.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Create_CurrentPointerWithoutManifest_RejectsSnapshot()
    {
        TvStoredGenerationSummary manifest = CreateManifest(Now, 1);
        TvGenerationRetentionSnapshot snapshot = new(
            CreateGenerationId(Now, 2),
            [manifest],
            []);

        Action action = () => planner.Create(snapshot, DefaultPolicy, Now);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("tv_generation_retention_current_manifest_missing");
    }

    [Fact]
    public void Create_DuplicateManifestGenerationId_RejectsSnapshot()
    {
        TvStoredGenerationSummary manifest = CreateManifest(Now, 1);
        TvGenerationRetentionSnapshot snapshot = new(
            manifest.GenerationId,
            [manifest, manifest with { PublishedAt = Now.AddMinutes(-1) }],
            []);

        Action action = () => planner.Create(snapshot, DefaultPolicy, Now);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("tv_generation_retention_manifest_duplicate");
    }

    [Fact]
    public void Constructor_InvalidPolicyBoundaries_RejectsValues()
    {
        Action zeroAge = () => new TvGenerationRetentionPolicy(
            TimeSpan.Zero,
            1,
            TimeSpan.FromHours(1));
        Action zeroGenerations = () => new TvGenerationRetentionPolicy(
            TimeSpan.FromTicks(1),
            0,
            TimeSpan.FromHours(1));
        Action shortGrace = () => new TvGenerationRetentionPolicy(
            TimeSpan.FromTicks(1),
            1,
            TimeSpan.FromHours(1).Add(TimeSpan.FromTicks(-1)));

        zeroAge.Should().Throw<ArgumentOutOfRangeException>();
        zeroGenerations.Should().Throw<ArgumentOutOfRangeException>();
        shortGrace.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_ExactValidPolicyBoundaries_AcceptsValues()
    {
        Func<TvGenerationRetentionPolicy> action = () => new TvGenerationRetentionPolicy(
            TimeSpan.FromTicks(1),
            1,
            TimeSpan.FromHours(1));

        TvGenerationRetentionPolicy result = action.Should().NotThrow().Which;

        result.MaxAge.Should().Be(TimeSpan.FromTicks(1));
        result.MaxGenerations.Should().Be(1);
        result.OrphanGracePeriod.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Create_DefaultOrNonUtcNow_RejectsTimestamp()
    {
        TvStoredGenerationSummary current = CreateManifest(Now, 1);
        TvGenerationRetentionSnapshot snapshot = new(current.GenerationId, [current], []);
        Action defaultNow = () => planner.Create(snapshot, DefaultPolicy, default);
        Action nonUtcNow = () => planner.Create(
            snapshot,
            DefaultPolicy,
            new DateTimeOffset(2026, 7, 29, 18, 0, 0, TimeSpan.FromHours(2)));

        defaultNow.Should().Throw<ArgumentException>();
        nonUtcNow.Should().Throw<ArgumentException>();
    }

    private static TvStoredGenerationSummary CreateManifest(DateTimeOffset publishedAt, int suffix)
    {
        return new TvStoredGenerationSummary(CreateGenerationId(publishedAt, suffix), publishedAt);
    }

    private static string CreateGenerationId(DateTimeOffset createdAt, int suffix)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"tv-{createdAt:yyyyMMddHHmmssfff}-{suffix:x32}");
    }
}
