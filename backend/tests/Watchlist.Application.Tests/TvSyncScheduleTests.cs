using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class TvSyncScheduleTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-18T12:00:00Z");
    private static readonly TimeSpan FullSyncInterval = TimeSpan.FromHours(1);
    private static readonly TraktActivityCursor CurrentActivity = new(
        Now.AddMinutes(-5),
        Now.AddMinutes(-4));

    [Fact]
    public void Decide_ConnectedWithoutPublishedGeneration_ReturnsScheduledFull()
    {
        TvGenerationKind? result = TvSyncSchedule.Decide(
            Connected(),
            publishedManifest: null,
            CurrentActivity,
            Now,
            FullSyncInterval);

        result.Should().Be(TvGenerationKind.ScheduledFull);
    }

    [Fact]
    public void Decide_LastScheduledFullAtIntervalBoundary_ReturnsScheduledFull()
    {
        TvGenerationManifest published = CreateManifest(
            TvGenerationKind.ScheduledFull,
            Now.Subtract(FullSyncInterval));

        TvGenerationKind? result = TvSyncSchedule.Decide(
            Connected(),
            published,
            CurrentActivity,
            Now,
            FullSyncInterval);

        result.Should().Be(TvGenerationKind.ScheduledFull);
    }

    [Fact]
    public void Decide_FreshScheduledFullWithChangedRelevantActivity_ReturnsActivityFull()
    {
        TvGenerationManifest published = CreateManifest(
            TvGenerationKind.ScheduledFull,
            Now.AddMinutes(-30));
        TraktActivityCursor changed = published.ActivityCursor with
        {
            EpisodeWatchedAt = published.ActivityCursor.EpisodeWatchedAt.AddSeconds(1)
        };

        TvGenerationKind? result = TvSyncSchedule.Decide(
            Connected(),
            published,
            changed,
            Now,
            FullSyncInterval);

        result.Should().Be(TvGenerationKind.ActivityFull);
    }

    [Fact]
    public void Decide_FreshScheduledFullWithoutChangedActivity_ReturnsNoRefresh()
    {
        TvGenerationManifest published = CreateManifest(
            TvGenerationKind.ScheduledFull,
            Now.AddMinutes(-30));

        TvGenerationKind? result = TvSyncSchedule.Decide(
            Connected(),
            published,
            published.ActivityCursor,
            Now,
            FullSyncInterval);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("disconnected")]
    [InlineData("pending")]
    [InlineData("refresh_required")]
    [InlineData("revoked")]
    public void Decide_ConnectionIsNotConnected_ReturnsNoRefresh(string status)
    {
        TraktConnectionStatusDto connection = new(status, null, null, null);

        TvGenerationKind? result = TvSyncSchedule.Decide(
            connection,
            publishedManifest: null,
            currentActivity: null,
            Now,
            FullSyncInterval);

        result.Should().BeNull();
    }

    [Fact]
    public void Decide_HourlyRefreshAndActivityAreBothDue_PrefersScheduledFull()
    {
        TvGenerationManifest published = CreateManifest(
            TvGenerationKind.ScheduledFull,
            Now.AddHours(-2));
        TraktActivityCursor changed = published.ActivityCursor with
        {
            ShowWatchlistedAt = published.ActivityCursor.ShowWatchlistedAt.AddSeconds(1)
        };

        TvGenerationKind? result = TvSyncSchedule.Decide(
            Connected(),
            published,
            changed,
            Now,
            FullSyncInterval);

        result.Should().Be(TvGenerationKind.ScheduledFull);
    }

    [Fact]
    public void Decide_ActivityFullPreservesRecentScheduledFullCadence()
    {
        DateTimeOffset scheduledAt = Now.AddMinutes(-10);
        TvGenerationManifest published = CreateManifest(
            TvGenerationKind.ActivityFull,
            Now.AddMinutes(-5)) with
        {
            LastScheduledFullAt = scheduledAt
        };

        TvGenerationKind? result = TvSyncSchedule.Decide(
            Connected(),
            published,
            published.ActivityCursor,
            Now,
            FullSyncInterval);

        result.Should().BeNull();
    }

    [Fact]
    public void Decide_ScheduledAbsenceThenActivity_DoesNotScheduleAgainBeforeHourlyBoundary()
    {
        DateTimeOffset firstAbsenceAt = Now.AddHours(-1);
        TvGenerationManifest activity = CreateManifest(
            TvGenerationKind.ActivityFull,
            firstAbsenceAt.AddMinutes(5)) with
        {
            LastScheduledFullAt = firstAbsenceAt
        };

        TvGenerationKind? beforeBoundary = TvSyncSchedule.Decide(
            Connected(),
            activity,
            activity.ActivityCursor,
            firstAbsenceAt.AddMinutes(10),
            FullSyncInterval);
        TvGenerationKind? atBoundary = TvSyncSchedule.Decide(
            Connected(),
            activity,
            activity.ActivityCursor,
            firstAbsenceAt.Add(FullSyncInterval),
            FullSyncInterval);

        beforeBoundary.Should().BeNull();
        atBoundary.Should().Be(TvGenerationKind.ScheduledFull);
    }

    [Fact]
    public void Decide_PublishedTimestampIsInFuture_ReturnsNoRefresh()
    {
        TvGenerationManifest future = CreateManifest(
            TvGenerationKind.ActivityFull,
            Now.AddMinutes(5)) with
        {
            LastScheduledFullAt = Now.AddMinutes(-30)
        };

        TvGenerationKind? result = TvSyncSchedule.Decide(
            Connected(),
            future,
            future.ActivityCursor,
            Now,
            FullSyncInterval);

        result.Should().BeNull();
    }

    private static TraktConnectionStatusDto Connected()
    {
        return new TraktConnectionStatusDto(
            "connected",
            Now.AddDays(-1),
            Now.AddDays(30),
            null);
    }

    private static TvGenerationManifest CreateManifest(
        TvGenerationKind kind,
        DateTimeOffset publishedAt)
    {
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            $"tv-{publishedAt:yyyyMMddHHmmssfff}-11111111111111111111111111111111",
            42,
            "Show",
            completedAt: publishedAt) with
        {
            Kind = kind,
            ActivityBefore = new TraktActivityCursor(
                publishedAt.AddMinutes(-5),
                publishedAt.AddMinutes(-4)),
            ActivityAfter = new TraktActivityCursor(
                publishedAt.AddMinutes(-5),
                publishedAt.AddMinutes(-4))
        };
        return TvGenerationManifest.CreatePhaseOne(
            draft,
            previousGenerationId: null,
            publishedAt,
            providerEnrichmentCompletedAt: publishedAt);
    }
}
