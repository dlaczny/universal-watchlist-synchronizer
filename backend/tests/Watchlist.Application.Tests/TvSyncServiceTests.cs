using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class TvSyncServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-18T12:00:00Z");
    private static readonly IReadOnlyList<string> LockedHealthReasons =
    [
        "plex_history_phase_not_implemented",
        "worker_tv_mutation_disabled"
    ];

    [Fact]
    public async Task SyncAsync_WatchlistOnlyUnstartedShow_PublishesActive()
    {
        Harness harness = Harness.Create();
        harness.SetWatchlistOnlyUnstarted();

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvShow show = harness.PublishedShow();
        show.InWatchlist.Should().BeTrue();
        show.AiredEpisodes.Should().Be(0);
        show.CompletedEpisodes.Should().Be(0);
        show.LifecycleState.Should().Be(TvLifecycleState.Active);
        show.Seasons.Should().BeEmpty();
        harness.Trakt.SeasonRequests.Should().Equal((42L, 0));
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_UnfinishedProgressOnlyShow_RemainsActiveAfterWatchlistAutoRemoval()
    {
        Harness harness = Harness.Create();

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvShow show = harness.PublishedShow();
        show.InWatchlist.Should().BeFalse();
        show.AiredEpisodes.Should().Be(2);
        show.CompletedEpisodes.Should().Be(1);
        show.LifecycleState.Should().Be(TvLifecycleState.Active);
        result.WatchlistItemsFetched.Should().Be(0);
        result.ProgressItemsFetched.Should().Be(1);
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_CompletedProgressOnlyShow_PublishesCaughtUp()
    {
        Harness harness = Harness.Create();
        harness.SetCompletedProgress(includeWatchlist: false);

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvShow show = harness.PublishedShow();
        show.InWatchlist.Should().BeFalse();
        show.CompletedEpisodes.Should().Be(show.AiredEpisodes);
        show.LifecycleState.Should().Be(TvLifecycleState.CaughtUp);
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_ExplicitlyReaddedCompletedShow_PublishesActive()
    {
        Harness harness = Harness.Create();
        harness.SetCompletedProgress(includeWatchlist: true);

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ActivityFull);

        TvShow show = harness.PublishedShow();
        show.InWatchlist.Should().BeTrue();
        show.CompletedEpisodes.Should().Be(show.AiredEpisodes);
        show.LifecycleState.Should().Be(TvLifecycleState.Active);
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_PreviousRowAbsentOnce_RemainsVisibleWithOneConfirmation()
    {
        Harness harness = Harness.Create();
        await harness.SyncAsync(TvGenerationKind.ScheduledFull);
        harness.ClearCurrentSources();
        harness.Trakt.Seasons[(42, 0)] =
        [
            new TraktSeasonEpisode(8_003, 9_003, 0, 3, "New special", Now.AddYears(-1))
        ];
        harness.Trakt.SeasonRequests.Clear();
        harness.Advance(TimeSpan.FromHours(1));

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvShow show = harness.PublishedShow();
        show.LifecycleState.Should().Be(TvLifecycleState.Active);
        show.MissingScheduledConfirmations.Should().Be(1);
        show.SpecialEpisodeIdentities.Should().Equal(
            new TvSpecialEpisodeIdentity(8_003, 9_003, 0, 3));
        harness.Trakt.SeasonRequests.Should().Equal((42L, 0));
        result.ShowsPublished.Should().Be(1);
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_PreviousRowAbsentInTwoScheduledGenerations_BecomesSourceRemoved()
    {
        Harness harness = Harness.Create();
        await harness.SyncAsync(TvGenerationKind.ScheduledFull);
        harness.ClearCurrentSources();
        harness.Advance(TimeSpan.FromHours(1));
        await harness.SyncAsync(TvGenerationKind.ScheduledFull);
        harness.Advance(TimeSpan.FromHours(1));

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvShow show = harness.PublishedShow();
        show.LifecycleState.Should().Be(TvLifecycleState.SourceRemoved);
        show.MissingScheduledConfirmations.Should().Be(2);
        show.LastLifecycleEvent.Should().Be("tv:42:2:source_removed");
        harness.Repository.Published!.Manifest.LifecycleEventIds.Should()
            .Equal("tv:42:2:source_removed");
        AssertPhaseOneSafety(result, harness.Repository.Published.Manifest);
    }

    [Fact]
    public async Task SyncAsync_ActivityGenerationCannotAdvanceAbsenceConfirmation()
    {
        Harness harness = Harness.Create();
        await harness.SyncAsync(TvGenerationKind.ScheduledFull);
        harness.ClearCurrentSources();
        harness.Advance(TimeSpan.FromMinutes(5));

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ActivityFull);

        TvShow show = harness.PublishedShow();
        show.LifecycleState.Should().Be(TvLifecycleState.Active);
        show.MissingScheduledConfirmations.Should().Be(0);
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_AbsentRetainedShowHasMalformedSeasonZero_PerformsNoNewStageOrPublish()
    {
        Harness harness = Harness.Create();
        await harness.SyncAsync(TvGenerationKind.ScheduledFull);
        harness.ClearCurrentSources();
        harness.Trakt.Seasons[(42, 0)] =
        [
            new TraktSeasonEpisode(0, 9_003, 0, 3, "Malformed", Now.AddYears(-1))
        ];
        int stageCallsBefore = harness.Repository.StageCalls;
        int publishCallsBefore = harness.Repository.PublishCalls;
        harness.Advance(TimeSpan.FromHours(1));

        Func<Task> action = async () =>
            await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvSourceSnapshotRejectedException exception = (await action.Should()
            .ThrowAsync<TvSourceSnapshotRejectedException>())
            .Which;
        exception.Message.Should().Be("tv_special_schedule_invalid");
        harness.Repository.StageCalls.Should().Be(stageCallsBefore);
        harness.Repository.PublishCalls.Should().Be(publishCallsBefore);
    }

    [Fact]
    public async Task SyncAsync_NewAiredEpisode_ReactivatesCaughtUpShow()
    {
        Harness harness = Harness.Create();
        harness.SetCompletedProgress(includeWatchlist: false);
        await harness.SyncAsync(TvGenerationKind.ScheduledFull);
        harness.Advance(TimeSpan.FromHours(1));
        harness.SetThreeEpisodeProgress();

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvShow show = harness.PublishedShow();
        show.LifecycleState.Should().Be(TvLifecycleState.Active);
        show.AiredEpisodes.Should().Be(3);
        show.CompletedEpisodes.Should().Be(2);
        show.LastLifecycleEvent.Should().Be("tv:42:2:reactivated");
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_FullSourceFailure_PerformsNoStageOrPublish()
    {
        Harness harness = Harness.Create();
        harness.Trakt.WatchlistException = new TraktUnavailableException();

        Func<Task> action = async () =>
            await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        await action.Should().ThrowAsync<TraktUnavailableException>();
        harness.Repository.StageCalls.Should().Be(0);
        harness.Repository.PublishCalls.Should().Be(0);
        harness.Repository.Published.Should().BeNull();
    }

    [Fact]
    public async Task SyncAsync_TmdbProviderFailure_PublishesUnknownProviderState()
    {
        Harness harness = Harness.Create();
        harness.Enrichment.Handler = (current, seasons, previous, now) =>
            SuccessfulEnrichment(current, seasons, previous, now) with
            {
                Availability = TvProviderAvailability.Unknown("PL"),
                SeasonAvailability = seasons.ToDictionary(
                    season => season,
                    _ => TvProviderAvailability.Unknown("PL")),
                Errors =
                [
                    "trakt_id=42;tmdb_id=420;stage=show_providers;code=tmdb_unavailable",
                    "trakt_id=42;tmdb_id=420;stage=season_1_providers;code=tmdb_unavailable"
                ]
            };

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvShow show = harness.PublishedShow();
        show.Availability.State.Should().Be(TvProviderState.Unknown);
        show.Seasons.Should().OnlyContain(season =>
            season.Availability.State == TvProviderState.Unknown);
        result.ProviderFailures.Should().Be(2);
        harness.Repository.PublishCalls.Should().Be(1);
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_DetailedEpisodeMissingFromCompleteSchedule_RejectsBeforeStage()
    {
        Harness harness = Harness.Create();
        harness.Trakt.Seasons[(42, 1)] = [Harness.EpisodeOne()];

        Func<Task> action = async () =>
            await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvSourceSnapshotRejectedException exception = (await action.Should()
            .ThrowAsync<TvSourceSnapshotRejectedException>())
            .Which;
        exception.Message.Should().Be("tv_schedule_episode_missing");
        harness.Repository.StageCalls.Should().Be(0);
        harness.Repository.PublishCalls.Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_CompleteScheduleContainsDuplicateEpisodeKey_RejectsBeforeStage()
    {
        Harness harness = Harness.Create();
        harness.Trakt.Seasons[(42, 1)] =
        [
            Harness.EpisodeOne(),
            Harness.EpisodeTwo(),
            Harness.EpisodeTwo() with { TraktEpisodeId = 9_999, TvdbId = 8_888 }
        ];

        Func<Task> action = async () =>
            await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvSourceSnapshotRejectedException exception = (await action.Should()
            .ThrowAsync<TvSourceSnapshotRejectedException>())
            .Which;
        exception.Message.Should().Be("tv_schedule_episode_duplicate");
        harness.Repository.StageCalls.Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_SummaryEpisodeConflictsWithScheduleIdentity_RejectsBeforeStage()
    {
        Harness harness = Harness.Create();
        TraktWatchedShowProgress progress = harness.Trakt.Progress.Items.Single();
        harness.Trakt.Progress = new TraktPagedResult<TraktWatchedShowProgress>(
            1,
            100,
            [progress with
            {
                NextEpisode = Harness.EpisodeTwo() with { TraktEpisodeId = 9_999 }
            }]);

        Func<Task> action = async () =>
            await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvSourceSnapshotRejectedException exception = (await action.Should()
            .ThrowAsync<TvSourceSnapshotRejectedException>())
            .Which;
        exception.Message.Should().Be("tv_schedule_summary_identity_conflict");
        harness.Repository.StageCalls.Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_CompleteSeasonZeroSchedule_PersistsExactIdentityOnlyRows()
    {
        Harness harness = Harness.Create();
        harness.Trakt.Seasons[(42, 0)] =
        [
            new TraktSeasonEpisode(8_003, 9_003, 0, 3, "Special", Now.AddYears(-1))
        ];

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvShow show = harness.PublishedShow();
        show.SpecialEpisodeIdentities.Should().Equal(
            new TvSpecialEpisodeIdentity(8_003, 9_003, 0, 3));
        show.Seasons.Should().OnlyContain(season => season.SeasonNumber > 0);
        show.Seasons.SelectMany(season => season.Episodes).Should()
            .OnlyContain(episode => episode.SeasonNumber > 0);
        show.AiredEpisodes.Should().Be(2);
        show.CompletedEpisodes.Should().Be(1);
        TvSnapshotValidator validator = new();
        harness.Repository.Published!.Manifest.MembershipHash.Should()
            .Be(validator.ComputeMembershipHash([show]));
        AssertPhaseOneSafety(result, harness.Repository.Published.Manifest);
    }

    [Theory]
    [InlineData("wrong_season")]
    [InlineData("missing_trakt_identity")]
    [InlineData("invalid_tvdb_identity")]
    [InlineData("duplicate_episode")]
    public async Task SyncAsync_MalformedOrPartialSeasonZeroSchedule_RejectsBeforeStage(
        string invalidPart)
    {
        Harness harness = Harness.Create();
        TraktSeasonEpisode valid = new(8_001, 9_001, 0, 1, "Special", Now.AddYears(-1));
        harness.Trakt.Seasons[(42, 0)] = invalidPart switch
        {
            "wrong_season" => [valid with { SeasonNumber = 1 }],
            "missing_trakt_identity" => [valid with { TraktEpisodeId = 0 }],
            "invalid_tvdb_identity" => [valid with { TvdbId = 0 }],
            "duplicate_episode" => [valid, valid with { TraktEpisodeId = 8_002, TvdbId = 9_002 }],
            _ => throw new InvalidOperationException()
        };

        Func<Task> action = async () =>
            await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvSourceSnapshotRejectedException exception = (await action.Should()
            .ThrowAsync<TvSourceSnapshotRejectedException>())
            .Which;
        exception.Message.Should().Be("tv_special_schedule_invalid");
        harness.Repository.StageCalls.Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_SpecialWithoutTvdbIdentity_IsExcludedWithoutRejectingSnapshot()
    {
        Harness harness = Harness.Create();
        harness.Trakt.Seasons[(42, 0)] =
        [
            new TraktSeasonEpisode(8_001, null, 0, 1, "Special", Now.AddYears(-1))
        ];

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        harness.PublishedShow().SpecialEpisodeIdentities.Should().BeEmpty();
        result.ShowsPublished.Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_ExplicitEmptySeasonZeroSchedule_PersistsEmptyIdentityList()
    {
        Harness harness = Harness.Create();
        harness.Trakt.Seasons[(42, 0)] = [];

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        harness.PublishedShow().SpecialEpisodeIdentities.Should().BeEmpty();
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_ActivityCursorChangesDuringCollection_PerformsNoStageOrPublish()
    {
        Harness harness = Harness.Create();
        TraktActivityCursor before = new(Now.AddMinutes(-10), Now.AddMinutes(-9));
        TraktActivityCursor after = before with
        {
            EpisodeWatchedAt = before.EpisodeWatchedAt.AddSeconds(1)
        };
        harness.Trakt.ActivityResponses.Enqueue(before);
        harness.Trakt.ActivityResponses.Enqueue(after);

        Func<Task> action = async () =>
            await harness.SyncAsync(TvGenerationKind.ActivityFull);

        TvSourceSnapshotRejectedException exception = (await action.Should()
            .ThrowAsync<TvSourceSnapshotRejectedException>())
            .Which;
        exception.Message.Should().Be("tv_snapshot_activity_changed");
        harness.Repository.StageCalls.Should().Be(0);
        harness.Repository.PublishCalls.Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_StageFails_DoesNotAdvancePublishedPointer()
    {
        Harness harness = Harness.Create();
        InvalidOperationException failure = new("stage failed");
        harness.Repository.StageException = failure;

        Func<Task> action = async () =>
            await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>())
            .Which;
        exception.Should().BeSameAs(failure);
        harness.Repository.StageCalls.Should().Be(1);
        harness.Repository.PublishCalls.Should().Be(0);
        harness.Repository.Published.Should().BeNull();
        harness.Repository.CallOrder.Should().Equal("stage");
    }

    [Fact]
    public async Task SyncAsync_PreservesExactPaginationProvenanceInDraftAndManifest()
    {
        Harness harness = Harness.Create();
        harness.Trakt.Watchlist = new TraktPagedResult<TraktWatchlistShow>(
            3,
            37,
            []);
        harness.Trakt.Progress = new TraktPagedResult<TraktWatchedShowProgress>(
            7,
            37,
            harness.Trakt.Progress.Items);

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvGenerationDraft draft = harness.Repository.LastStaged!;
        draft.WatchlistPageCount.Should().Be(3);
        draft.ProgressPageCount.Should().Be(7);
        draft.RequestFilters["watchlist.limit"].Should().Be("37");
        draft.RequestFilters["watched.limit"].Should().Be("37");
        harness.Repository.Published!.Manifest.WatchlistPageCount.Should().Be(3);
        harness.Repository.Published.Manifest.ProgressPageCount.Should().Be(7);
        AssertPhaseOneSafety(result, harness.Repository.Published.Manifest);
    }

    [Fact]
    public async Task SyncAsync_WatchlistAndProgressPageSizesDisagree_RejectsBeforeStage()
    {
        Harness harness = Harness.Create();
        harness.Trakt.Watchlist = new TraktPagedResult<TraktWatchlistShow>(1, 50, []);

        Func<Task> action = async () =>
            await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvSourceSnapshotRejectedException exception = (await action.Should()
            .ThrowAsync<TvSourceSnapshotRejectedException>())
            .Which;
        exception.Message.Should().Be("tv_source_pagination_mismatch");
        harness.Repository.StageCalls.Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_WatchlistAndProgressSameTraktIdHaveConflictingIds_RejectsBeforeStage()
    {
        Harness harness = Harness.Create();
        harness.Trakt.Watchlist = new TraktPagedResult<TraktWatchlistShow>(
            1,
            100,
            [new TraktWatchlistShow(
                new TraktShowIds(42, 9_999, 420, "tt0000042"),
                "Show",
                2024,
                Now.AddDays(-1))]);

        Func<Task> action = async () =>
            await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvSourceSnapshotRejectedException exception = (await action.Should()
            .ThrowAsync<TvSourceSnapshotRejectedException>())
            .Which;
        exception.Message.Should().Be("tv_source_identity_conflict");
        harness.Repository.StageCalls.Should().Be(0);
    }

    [Fact]
    public async Task SyncAsync_FetchesDetailedSeasonsNextSeasonAndSeasonZeroExactlyOnce()
    {
        Harness harness = Harness.Create();
        TraktWatchedShowProgress progress = harness.Trakt.Progress.Items.Single();
        TraktSeasonEpisode next = new(2_001, 3_001, 2, 1, "Next season", Now.AddDays(10));
        harness.Trakt.Progress = new TraktPagedResult<TraktWatchedShowProgress>(
            1,
            100,
            [progress with { NextEpisode = next }]);
        harness.Trakt.Seasons[(42, 2)] = [next];

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        harness.Trakt.SeasonRequests.Should().Equal((42L, 0), (42L, 1), (42L, 2));
        harness.PublishedShow().Seasons.Select(season => season.SeasonNumber)
            .Should().Equal(1, 2);
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_FreshPreviousMetadata_ReusesTraktMetadataUntilDailyBoundary()
    {
        Harness harness = Harness.Create();
        await harness.SyncAsync(TvGenerationKind.ScheduledFull);
        harness.Advance(TimeSpan.FromHours(23));

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        harness.Trakt.MetadataRequests.Should().Equal(42L);
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_PreviousMetadataAtDailyBoundary_RefreshesTraktMetadata()
    {
        Harness harness = Harness.Create();
        await harness.SyncAsync(TvGenerationKind.ScheduledFull);
        harness.Advance(TimeSpan.FromDays(1));

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        harness.Trakt.MetadataRequests.Should().Equal(42L, 42L);
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_AbsentPreviousWithEnrichedIdsAndRawMetadataMissingIds_AdvancesLifecycle()
    {
        Harness harness = Harness.Create();
        await harness.SyncAsync(TvGenerationKind.ScheduledFull);
        harness.ClearCurrentSources();
        harness.Advance(TimeSpan.FromDays(1));
        harness.Trakt.Metadata[42] = new TraktShowMetadata(
            new TraktShowIds(42, null, null, null),
            "Show",
            2024,
            "Overview",
            "returning series");

        TvSyncResultDto result = await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        TvShow show = harness.PublishedShow();
        show.TvdbId.Should().Be(4_200);
        show.TmdbId.Should().Be(420);
        show.ImdbId.Should().Be("tt0000042");
        show.MissingScheduledConfirmations.Should().Be(1);
        AssertPhaseOneSafety(result, harness.Repository.Published!.Manifest);
    }

    [Fact]
    public async Task SyncAsync_ProviderEnrichmentTimestamp_IsCapturedAfterEnrichmentCompletes()
    {
        Harness harness = Harness.Create();
        harness.Enrichment.Handler = (current, seasons, previous, now) =>
        {
            TmdbTvEnrichmentResult result = SuccessfulEnrichment(
                current,
                seasons,
                previous,
                now);
            harness.Time.UtcNow = now.AddMinutes(2);
            return result;
        };

        await harness.SyncAsync(TvGenerationKind.ScheduledFull);

        harness.Repository.Published!.Manifest.ProviderEnrichmentCompletedAt.Should()
            .Be(Now.AddMinutes(2));
    }

    [Fact]
    public async Task SyncAsync_ActivityManifest_PreservesPriorScheduledFullPublicationTime()
    {
        Harness harness = Harness.Create();
        await harness.SyncAsync(TvGenerationKind.ScheduledFull);
        DateTimeOffset scheduledAt = harness.Repository.Published!.Manifest.PublishedAt;
        harness.Advance(TimeSpan.FromMinutes(5));

        await harness.SyncAsync(TvGenerationKind.ActivityFull);

        TvGenerationManifest activity = harness.Repository.Published!.Manifest;
        activity.Kind.Should().Be(TvGenerationKind.ActivityFull);
        activity.LastScheduledFullAt.Should().Be(scheduledAt);
        activity.PublishedAt.Should().Be(scheduledAt.AddMinutes(5));
    }

    [Fact]
    public async Task Coordinator_SecondLeaseWaitsUntilFirstLeaseIsDisposed()
    {
        TraktOperationCoordinator coordinator = new();
        IAsyncDisposable first = await coordinator.AcquireAsync(CancellationToken.None);

        Task<IAsyncDisposable> secondTask = coordinator
            .AcquireAsync(CancellationToken.None)
            .AsTask();

        secondTask.IsCompleted.Should().BeFalse();
        await first.DisposeAsync();
        IAsyncDisposable second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        await second.DisposeAsync();
    }

    private static void AssertPhaseOneSafety(
        TvSyncResultDto result,
        TvGenerationManifest manifest)
    {
        result.Status.Should().Be("completed");
        result.GenerationId.Should().MatchRegex(
            "^tv-[0-9]{17}-[0-9a-f]{32}$");
        result.MutationCapable.Should().BeFalse();
        result.HealthReasons.Should().Equal(LockedHealthReasons);
        manifest.MutationCapable.Should().BeFalse();
        manifest.HealthReasons.Should().Equal(LockedHealthReasons);
        manifest.CleanupEventIds.Should().BeEmpty();
    }

    private static TmdbTvEnrichmentResult SuccessfulEnrichment(
        TraktShowMetadata current,
        IReadOnlyList<int> seasons,
        TvShow? previous,
        DateTimeOffset now)
    {
        Dictionary<int, TvProviderAvailability> seasonAvailability = seasons.ToDictionary(
            season => season,
            _ => new TvProviderAvailability(
                TvProviderState.ConfirmedUnavailable,
                "PL",
                now,
                null,
                []));
        return new TmdbTvEnrichmentResult(
            current.Ids.TvdbId,
            current.Ids.TmdbId,
            current.Ids.ImdbId,
            current.Ids.TvdbId is > 0 ? TvIdentityStatus.Verified : TvIdentityStatus.Missing,
            current.Title,
            current.Year,
            current.Overview,
            previous?.PosterUrl,
            previous?.BackdropUrl,
            now,
            new TvProviderAvailability(
                TvProviderState.ConfirmedUnavailable,
                "PL",
                now,
                null,
                []),
            seasonAvailability,
            []);
    }

    private sealed class Harness
    {
        private static readonly TraktShowIds ShowIds = new(
            42,
            4_200,
            420,
            "tt0000042");

        private Harness()
        {
            Time = new MutableTimeProvider(Now);
            TokenProvider = new FakeTokenProvider();
            Trakt = new FakeTraktTvClient();
            Enrichment = new FakeEnrichmentService();
            Repository = new FakeGenerationRepository();
            Service = new TvSyncService(
                TokenProvider,
                Trakt,
                Enrichment,
                Repository,
                new TraktOperationCoordinator(),
                Time,
                TimeSpan.FromDays(1));
            SetUnfinishedProgress();
        }

        public MutableTimeProvider Time { get; }

        public FakeTokenProvider TokenProvider { get; }

        public FakeTraktTvClient Trakt { get; }

        public FakeEnrichmentService Enrichment { get; }

        public FakeGenerationRepository Repository { get; }

        public TvSyncService Service { get; }

        public static Harness Create()
        {
            return new Harness();
        }

        public async Task<TvSyncResultDto> SyncAsync(TvGenerationKind kind)
        {
            return await Service.SyncAsync(kind, CancellationToken.None);
        }

        public TvShow PublishedShow()
        {
            return Repository.Published!.Shows.Single();
        }

        public void Advance(TimeSpan amount)
        {
            Time.UtcNow = Time.UtcNow.Add(amount);
            Trakt.Activity = new TraktActivityCursor(
                Time.UtcNow.AddMinutes(-10),
                Time.UtcNow.AddMinutes(-9));
        }

        public void ClearCurrentSources()
        {
            Trakt.Watchlist = new TraktPagedResult<TraktWatchlistShow>(1, 100, []);
            Trakt.Progress = new TraktPagedResult<TraktWatchedShowProgress>(1, 100, []);
        }

        public void SetWatchlistOnlyUnstarted()
        {
            Trakt.Watchlist = new TraktPagedResult<TraktWatchlistShow>(
                1,
                100,
                [new TraktWatchlistShow(ShowIds, "Show", 2024, Time.UtcNow.AddDays(-30))]);
            Trakt.Progress = new TraktPagedResult<TraktWatchedShowProgress>(1, 100, []);
            Trakt.Detailed[42] = new TraktDetailedShowProgress(0, 0, []);
            Trakt.Seasons[(42, 0)] = [];
        }

        public void SetCompletedProgress(bool includeWatchlist)
        {
            SetUnfinishedProgress();
            TraktDetailedEpisodeProgress first = DetailedEpisodeOne();
            TraktDetailedEpisodeProgress second = new(
                1,
                2,
                true,
                Time.UtcNow.AddDays(-1));
            Trakt.Detailed[42] = new TraktDetailedShowProgress(
                2,
                2,
                [new TraktDetailedSeasonProgress(1, 2, 2, [first, second])]);
            Trakt.Progress = new TraktPagedResult<TraktWatchedShowProgress>(
                1,
                100,
                [new TraktWatchedShowProgress(
                    ShowIds,
                    "Show",
                    2024,
                    2,
                    2,
                    null,
                    EpisodeTwo())]);
            Trakt.Watchlist = includeWatchlist
                ? new TraktPagedResult<TraktWatchlistShow>(
                    1,
                    100,
                    [new TraktWatchlistShow(
                        ShowIds,
                        "Show",
                        2024,
                        Time.UtcNow.AddMinutes(-1))])
                : new TraktPagedResult<TraktWatchlistShow>(1, 100, []);
        }

        public void SetThreeEpisodeProgress()
        {
            TraktSeasonEpisode third = new(
                1_003,
                2_003,
                1,
                3,
                "Third",
                Time.UtcNow.AddMinutes(-1));
            Trakt.Detailed[42] = new TraktDetailedShowProgress(
                3,
                2,
                [new TraktDetailedSeasonProgress(
                    1,
                    3,
                    2,
                    [
                        DetailedEpisodeOne(),
                        new TraktDetailedEpisodeProgress(
                            1,
                            2,
                            true,
                            Time.UtcNow.AddDays(-1)),
                        new TraktDetailedEpisodeProgress(1, 3, false, null)
                    ])]);
            Trakt.Progress = new TraktPagedResult<TraktWatchedShowProgress>(
                1,
                100,
                [new TraktWatchedShowProgress(
                    ShowIds,
                    "Show",
                    2024,
                    3,
                    2,
                    third,
                    EpisodeTwo())]);
            Trakt.Seasons[(42, 1)] = [EpisodeOne(), EpisodeTwo(), third];
            Trakt.Watchlist = new TraktPagedResult<TraktWatchlistShow>(1, 100, []);
        }

        public static TraktSeasonEpisode EpisodeOne()
        {
            return new TraktSeasonEpisode(
                1_001,
                2_001,
                1,
                1,
                "Pilot",
                Now.AddDays(-10));
        }

        public static TraktSeasonEpisode EpisodeTwo()
        {
            return new TraktSeasonEpisode(
                1_002,
                2_002,
                1,
                2,
                "Second",
                Now.AddDays(-3));
        }

        private void SetUnfinishedProgress()
        {
            Trakt.Watchlist = new TraktPagedResult<TraktWatchlistShow>(1, 100, []);
            Trakt.Progress = new TraktPagedResult<TraktWatchedShowProgress>(
                1,
                100,
                [new TraktWatchedShowProgress(
                    ShowIds,
                    "Show",
                    2024,
                    2,
                    1,
                    EpisodeTwo(),
                    EpisodeOne())]);
            Trakt.Detailed[42] = new TraktDetailedShowProgress(
                2,
                1,
                [new TraktDetailedSeasonProgress(
                    1,
                    2,
                    1,
                    [DetailedEpisodeOne(), new TraktDetailedEpisodeProgress(1, 2, false, null)])]);
            Trakt.Metadata[42] = new TraktShowMetadata(
                ShowIds,
                "Show",
                2024,
                "Overview",
                "returning series");
            Trakt.Seasons[(42, 0)] = [];
            Trakt.Seasons[(42, 1)] = [EpisodeOne(), EpisodeTwo()];
        }

        private static TraktDetailedEpisodeProgress DetailedEpisodeOne()
        {
            return new TraktDetailedEpisodeProgress(
                1,
                1,
                true,
                Now.AddDays(-9));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }
    }

    private sealed class FakeTokenProvider : ITraktAccessTokenProvider
    {
        public int GetCalls { get; private set; }

        public Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult("access-token");
        }

        public Task<string> ForceRefreshAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeTraktTvClient : ITraktTvClient
    {
        public TraktActivityCursor Activity { get; set; } = new(
            Now.AddMinutes(-10),
            Now.AddMinutes(-9));

        public Queue<TraktActivityCursor> ActivityResponses { get; } = new();

        public TraktPagedResult<TraktWatchlistShow> Watchlist { get; set; } = new(1, 100, []);

        public TraktPagedResult<TraktWatchedShowProgress> Progress { get; set; } = new(1, 100, []);

        public Dictionary<long, TraktDetailedShowProgress> Detailed { get; } = [];

        public Dictionary<long, TraktShowMetadata> Metadata { get; } = [];

        public Dictionary<(long TraktId, int SeasonNumber), IReadOnlyList<TraktSeasonEpisode>> Seasons
        {
            get;
        } = [];

        public Exception? WatchlistException { get; set; }

        public List<long> DetailedRequests { get; } = [];

        public List<long> MetadataRequests { get; } = [];

        public List<(long TraktId, int SeasonNumber)> SeasonRequests { get; } = [];

        public Task<TraktActivityCursor> GetLastActivitiesAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ActivityResponses.Count > 0
                ? ActivityResponses.Dequeue()
                : Activity);
        }

        public Task<TraktPagedResult<TraktWatchlistShow>> GetWatchlistAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            return WatchlistException is null
                ? Task.FromResult(Watchlist)
                : Task.FromException<TraktPagedResult<TraktWatchlistShow>>(WatchlistException);
        }

        public Task<TraktPagedResult<TraktWatchedShowProgress>> GetWatchedProgressAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Progress);
        }

        public Task<TraktDetailedShowProgress> GetDetailedProgressAsync(
            string accessToken,
            long traktId,
            CancellationToken cancellationToken)
        {
            DetailedRequests.Add(traktId);
            return Task.FromResult(Detailed[traktId]);
        }

        public Task<TraktShowMetadata> GetShowMetadataAsync(
            string accessToken,
            long traktId,
            CancellationToken cancellationToken)
        {
            MetadataRequests.Add(traktId);
            return Task.FromResult(Metadata[traktId]);
        }

        public Task<IReadOnlyList<TraktSeasonEpisode>> GetSeasonAsync(
            string accessToken,
            long traktId,
            int seasonNumber,
            CancellationToken cancellationToken)
        {
            SeasonRequests.Add((traktId, seasonNumber));
            return Task.FromResult(Seasons[(traktId, seasonNumber)]);
        }
    }

    private sealed class FakeEnrichmentService : ITmdbTvEnrichmentService
    {
        public Func<
            TraktShowMetadata,
            IReadOnlyList<int>,
            TvShow?,
            DateTimeOffset,
            TmdbTvEnrichmentResult>? Handler
        { get; set; }

        public List<long> Requests { get; } = [];

        public Task<TmdbTvEnrichmentResult> EnrichAsync(
            TraktShowMetadata current,
            IReadOnlyList<int> numberedSeasonNumbers,
            TvShow? previous,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Requests.Add(current.Ids.TraktId);
            TmdbTvEnrichmentResult result = Handler is null
                ? SuccessfulEnrichment(current, numberedSeasonNumbers, previous, now)
                : Handler(current, numberedSeasonNumbers, previous, now);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeGenerationRepository : ITvGenerationRepository
    {
        private readonly Dictionary<string, TvGenerationDraft> staged = new(StringComparer.Ordinal);

        public PublishedTvGeneration? Published { get; private set; }

        public TvGenerationDraft? LastStaged { get; private set; }

        public Exception? StageException { get; set; }

        public int StageCalls { get; private set; }

        public int PublishCalls { get; private set; }

        public List<string> CallOrder { get; } = [];

        public Task StageAsync(TvGenerationDraft draft, CancellationToken cancellationToken)
        {
            StageCalls++;
            CallOrder.Add("stage");
            if (StageException is not null)
            {
                return Task.FromException(StageException);
            }

            LastStaged = draft;
            staged.Add(draft.GenerationId, draft);
            return Task.CompletedTask;
        }

        public Task PublishAsync(TvGenerationManifest manifest, CancellationToken cancellationToken)
        {
            PublishCalls++;
            CallOrder.Add("publish");
            TvGenerationDraft draft = staged[manifest.GenerationId];
            Published = new PublishedTvGeneration(manifest, draft.Shows);
            return Task.CompletedTask;
        }

        public Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Published);
        }
    }
}
