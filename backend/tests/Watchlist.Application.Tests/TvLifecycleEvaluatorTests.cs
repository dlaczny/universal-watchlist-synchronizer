using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class TvLifecycleEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly TvLifecycleEvaluator evaluator = new();

    [Fact]
    public void Evaluate_NewExplicitWatchlistShow_EmitsAddedActiveVersionOne()
    {
        TvLifecycleDecision result = EvaluateNew(inWatchlist: true, airedEpisodes: 0, completedEpisodes: 0);

        result.State.Should().Be(TvLifecycleState.Active);
        result.LifecycleVersion.Should().Be(1);
        result.MissingScheduledConfirmations.Should().Be(0);
        result.Event.Should().NotBeNull();
        result.Event!.Id.Should().Be("tv:42:1:added");
        result.Event.EventType.Should().Be("added");
        result.Event.Reason.Should().Be("tracked_source_added");
    }

    [Fact]
    public void Evaluate_NewUnfinishedProgressOnlyShow_EmitsAddedActiveVersionOne()
    {
        TvLifecycleDecision result = EvaluateNew(inWatchlist: false, airedEpisodes: 5, completedEpisodes: 2);

        result.State.Should().Be(TvLifecycleState.Active);
        result.Event!.EventType.Should().Be("added");
    }

    [Fact]
    public void Evaluate_NewCompletedProgressOnlyShow_EmitsAddedCaughtUpVersionOne()
    {
        TvLifecycleDecision result = EvaluateNew(inWatchlist: false, airedEpisodes: 5, completedEpisodes: 5);

        result.State.Should().Be(TvLifecycleState.CaughtUp);
        result.LifecycleVersion.Should().Be(1);
        result.Event!.EventType.Should().Be("added");
    }

    [Fact]
    public void Evaluate_ActiveCompletesAllAiredEpisodes_EmitsCaughtUp()
    {
        TvShow previous = CreatePrevious(TvLifecycleState.Active, airedEpisodes: 5, completedEpisodes: 4);

        TvLifecycleDecision result = evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: true,
            inWatchlist: false,
            airedEpisodes: 5,
            completedEpisodes: 5,
            TvGenerationKind.ScheduledFull,
            "generation-2",
            Now);

        result.State.Should().Be(TvLifecycleState.CaughtUp);
        result.LifecycleVersion.Should().Be(2);
        result.Event!.Id.Should().Be("tv:42:2:caught_up");
        result.Event.EventType.Should().Be("caught_up");
        result.Event.Reason.Should().Be("all_aired_episodes_watched");
    }

    [Fact]
    public void Evaluate_CaughtUpShowGainsUnwatchedEpisode_EmitsReactivatedActive()
    {
        TvShow previous = CreatePrevious(TvLifecycleState.CaughtUp, airedEpisodes: 5, completedEpisodes: 5);

        TvLifecycleDecision result = evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: true,
            inWatchlist: false,
            airedEpisodes: 6,
            completedEpisodes: 5,
            TvGenerationKind.ScheduledFull,
            "generation-2",
            Now);

        result.State.Should().Be(TvLifecycleState.Active);
        result.LifecycleVersion.Should().Be(2);
        result.Event!.EventType.Should().Be("reactivated");
        result.Event.Reason.Should().Be("tracked_source_reactivated");
    }

    [Fact]
    public void Evaluate_SourceRemovedShowIsExplicitlyReadded_EmitsReactivatedActive()
    {
        TvShow previous = CreatePrevious(
            TvLifecycleState.SourceRemoved,
            airedEpisodes: 5,
            completedEpisodes: 5,
            missingScheduledConfirmations: 2);

        TvLifecycleDecision result = evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: true,
            inWatchlist: true,
            airedEpisodes: 5,
            completedEpisodes: 5,
            TvGenerationKind.ActivityFull,
            "generation-2",
            Now);

        result.State.Should().Be(TvLifecycleState.Active);
        result.MissingScheduledConfirmations.Should().Be(0);
        result.Event!.EventType.Should().Be("reactivated");
    }

    [Fact]
    public void Evaluate_SourceRemovedCompletedProgressOnlyShowReappears_EmitsReactivatedCaughtUp()
    {
        TvShow previous = CreatePrevious(
            TvLifecycleState.SourceRemoved,
            airedEpisodes: 5,
            completedEpisodes: 5,
            missingScheduledConfirmations: 2);

        TvLifecycleDecision result = evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: true,
            inWatchlist: false,
            airedEpisodes: 5,
            completedEpisodes: 5,
            TvGenerationKind.ActivityFull,
            "generation-2",
            Now);

        result.State.Should().Be(TvLifecycleState.CaughtUp);
        result.MissingScheduledConfirmations.Should().Be(0);
        result.Event!.EventType.Should().Be("reactivated");
    }

    [Fact]
    public void Evaluate_ActiveFirstScheduledAbsence_IncrementsConfirmationWithoutEvent()
    {
        TvShow previous = CreatePrevious(TvLifecycleState.Active, airedEpisodes: 5, completedEpisodes: 2);

        TvLifecycleDecision result = EvaluateAbsent(previous, TvGenerationKind.ScheduledFull, "generation-2");

        result.State.Should().Be(TvLifecycleState.Active);
        result.LifecycleVersion.Should().Be(1);
        result.MissingScheduledConfirmations.Should().Be(1);
        result.Event.Should().BeNull();
    }

    [Fact]
    public void Evaluate_ActiveSecondDistinctScheduledAbsence_EmitsSourceRemoved()
    {
        TvShow previous = CreatePrevious(
            TvLifecycleState.Active,
            airedEpisodes: 5,
            completedEpisodes: 2,
            generationId: "generation-2",
            missingScheduledConfirmations: 1);

        TvLifecycleDecision result = EvaluateAbsent(previous, TvGenerationKind.ScheduledFull, "generation-3");

        result.State.Should().Be(TvLifecycleState.SourceRemoved);
        result.LifecycleVersion.Should().Be(2);
        result.MissingScheduledConfirmations.Should().Be(2);
        result.Event!.Id.Should().Be("tv:42:2:source_removed");
        result.Event.EventType.Should().Be("source_removed");
        result.Event.Reason.Should().Be("source_absent_two_scheduled_generations");
    }

    [Fact]
    public void Evaluate_ActivityTriggeredAbsence_PreservesStateAndConfirmationCount()
    {
        TvShow previous = CreatePrevious(
            TvLifecycleState.CaughtUp,
            airedEpisodes: 5,
            completedEpisodes: 5,
            missingScheduledConfirmations: 1);

        TvLifecycleDecision result = EvaluateAbsent(previous, TvGenerationKind.ActivityFull, "generation-2");

        result.State.Should().Be(TvLifecycleState.CaughtUp);
        result.LifecycleVersion.Should().Be(1);
        result.MissingScheduledConfirmations.Should().Be(1);
        result.Event.Should().BeNull();
    }

    [Fact]
    public void Evaluate_RepeatedSameScheduledGeneration_DoesNotAdvanceAbsence()
    {
        TvShow previous = CreatePrevious(
            TvLifecycleState.Active,
            airedEpisodes: 5,
            completedEpisodes: 2,
            generationId: "generation-2",
            missingScheduledConfirmations: 1);

        TvLifecycleDecision result = EvaluateAbsent(previous, TvGenerationKind.ScheduledFull, "generation-2");

        result.State.Should().Be(TvLifecycleState.Active);
        result.LifecycleVersion.Should().Be(1);
        result.MissingScheduledConfirmations.Should().Be(1);
        result.Event.Should().BeNull();
    }

    [Fact]
    public void Evaluate_RepeatedSameActivityGenerationWatchlistAbsence_IsIdempotent()
    {
        TvShow previous = CreatePrevious(
            TvLifecycleState.Active,
            airedEpisodes: 5,
            completedEpisodes: 2,
            generationId: "generation-2") with
        {
            InWatchlist = true
        };

        TvLifecycleDecision result = EvaluateAbsent(
            previous,
            TvGenerationKind.ActivityFull,
            "generation-2");

        result.State.Should().Be(TvLifecycleState.Active);
        result.LifecycleVersion.Should().Be(1);
        result.MissingScheduledConfirmations.Should().Be(0);
        result.Event.Should().BeNull();
    }

    [Theory]
    [InlineData(TvGenerationKind.ActivityFull, "generation-1")]
    [InlineData(TvGenerationKind.ScheduledFull, "generation-2")]
    public void Evaluate_AbsenceWithChangedProgressFacts_IsRejected(
        TvGenerationKind kind,
        string generationId)
    {
        TvShow previous = CreatePrevious(
            TvLifecycleState.Active,
            airedEpisodes: 5,
            completedEpisodes: 2);

        Action action = () => evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: false,
            inWatchlist: false,
            airedEpisodes: 5,
            completedEpisodes: 3,
            kind,
            generationId,
            Now);

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Theory]
    [InlineData("ended")]
    [InlineData("canceled")]
    public void Evaluate_CaughtUpTerminalStatus_RemainsCaughtUpInPhaseOne(string status)
    {
        TvShow previous = CreatePrevious(
            TvLifecycleState.CaughtUp,
            airedEpisodes: 5,
            completedEpisodes: 5,
            traktStatus: status);

        TvLifecycleDecision result = evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: true,
            inWatchlist: false,
            airedEpisodes: 5,
            completedEpisodes: 5,
            TvGenerationKind.ScheduledFull,
            "generation-2",
            Now);

        result.State.Should().Be(TvLifecycleState.CaughtUp);
        result.Event.Should().BeNull();
        result.State.Should().NotBe(TvLifecycleState.TerminalCleanupPending);
    }

    [Fact]
    public void Evaluate_ExplicitWatchlistWinsOverCompletedProgress()
    {
        TvShow previous = CreatePrevious(TvLifecycleState.CaughtUp, airedEpisodes: 5, completedEpisodes: 5);

        TvLifecycleDecision result = evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: true,
            inWatchlist: true,
            airedEpisodes: 5,
            completedEpisodes: 5,
            TvGenerationKind.ActivityFull,
            "generation-2",
            Now);

        result.State.Should().Be(TvLifecycleState.Active);
        result.Event!.EventType.Should().Be("reactivated");
    }

    [Fact]
    public void Evaluate_ExplicitWatchlistWithNoAiredEpisodes_IsActive()
    {
        TvLifecycleDecision result = EvaluateNew(inWatchlist: true, airedEpisodes: 0, completedEpisodes: 0);

        result.State.Should().Be(TvLifecycleState.Active);
    }

    [Fact]
    public void Evaluate_ProgressOnlyWithNoAiredEpisodes_IsRejected()
    {
        Action action = () => EvaluateNew(inWatchlist: false, airedEpisodes: 0, completedEpisodes: 0);

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Fact]
    public void Evaluate_RepeatedEquivalentCaughtUpGeneration_IsIdempotent()
    {
        TvShow previous = CreatePrevious(TvLifecycleState.CaughtUp, airedEpisodes: 5, completedEpisodes: 5);

        TvLifecycleDecision result = evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: true,
            inWatchlist: false,
            airedEpisodes: 5,
            completedEpisodes: 5,
            TvGenerationKind.ActivityFull,
            "generation-2",
            Now);

        result.LifecycleVersion.Should().Be(previous.LifecycleVersion);
        result.Event.Should().BeNull();
    }

    [Fact]
    public void Evaluate_EventPredicateHash_UsesExactCanonicalSemanticFacts()
    {
        TvLifecycleDecision result = EvaluateNew(inWatchlist: true, airedEpisodes: 5, completedEpisodes: 0);
        string canonicalFacts = "[\"tv_lifecycle_event_v1\",42,\"added\",null,0,true,true,5,0,0]";
        string expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalFacts)))
            .ToLowerInvariant();

        result.Event!.PredicateHash.Should().Be(expected);
        result.Event.PredicateHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Evaluate_EquivalentEventFactsAcrossGenerationAndTime_KeepPredicateHashStable()
    {
        TvLifecycleDecision first = evaluator.Evaluate(
            previous: null,
            traktId: 42,
            presentInCurrentSource: true,
            inWatchlist: true,
            airedEpisodes: 5,
            completedEpisodes: 0,
            TvGenerationKind.ScheduledFull,
            "generation-a",
            Now);
        TvLifecycleDecision second = evaluator.Evaluate(
            previous: null,
            traktId: 42,
            presentInCurrentSource: true,
            inWatchlist: true,
            airedEpisodes: 5,
            completedEpisodes: 0,
            TvGenerationKind.ActivityFull,
            "generation-b",
            Now.AddDays(1));

        second.Event!.PredicateHash.Should().Be(first.Event!.PredicateHash);
    }

    [Fact]
    public void Evaluate_EquivalentEventFactsAcrossLifecycleVersions_KeepPredicateHashStable()
    {
        TvShow firstPrevious = CreatePrevious(TvLifecycleState.Active, airedEpisodes: 5, completedEpisodes: 4);
        TvShow laterPrevious = firstPrevious with
        {
            LifecycleVersion = 9,
            LastLifecycleEvent = "tv:42:9:reactivated"
        };

        TvLifecycleDecision first = evaluator.Evaluate(
            firstPrevious,
            42,
            true,
            false,
            5,
            5,
            TvGenerationKind.ScheduledFull,
            "generation-2",
            Now);
        TvLifecycleDecision later = evaluator.Evaluate(
            laterPrevious,
            42,
            true,
            false,
            5,
            5,
            TvGenerationKind.ScheduledFull,
            "generation-10",
            Now.AddDays(1));

        later.Event!.PredicateHash.Should().Be(first.Event!.PredicateHash);
    }

    [Fact]
    public void Evaluate_TransitionAtMaximumVersion_IsRejectedBeforeOverflow()
    {
        TvShow previous = CreatePrevious(TvLifecycleState.Active, airedEpisodes: 5, completedEpisodes: 4) with
        {
            LifecycleVersion = long.MaxValue,
            LastLifecycleEvent = $"tv:42:{long.MaxValue}:added"
        };

        Action action = () => evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: true,
            inWatchlist: false,
            airedEpisodes: 5,
            completedEpisodes: 5,
            TvGenerationKind.ScheduledFull,
            "generation-2",
            Now);

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Theory]
    [InlineData(TvLifecycleState.TerminalCleanupPending)]
    [InlineData(TvLifecycleState.RetiredTerminal)]
    [InlineData((TvLifecycleState)99)]
    public void Evaluate_InvalidPreviousPhaseOneState_IsRejected(TvLifecycleState state)
    {
        TvShow previous = CreatePrevious(state, airedEpisodes: 5, completedEpisodes: 4);

        Action action = () => EvaluateAbsent(previous, TvGenerationKind.ScheduledFull, "generation-2");

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(-1L, 0)]
    [InlineData(1L, -1)]
    [InlineData(1L, 3)]
    public void Evaluate_InvalidPreviousVersionOrConfirmationCount_IsRejected(
        long lifecycleVersion,
        int confirmations)
    {
        TvShow previous = CreatePrevious(TvLifecycleState.Active, airedEpisodes: 5, completedEpisodes: 4) with
        {
            LifecycleVersion = lifecycleVersion,
            MissingScheduledConfirmations = confirmations
        };

        Action action = () => EvaluateAbsent(previous, TvGenerationKind.ScheduledFull, "generation-2");

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Theory]
    [InlineData(TvLifecycleState.Active, "caught_up", 2L, 0)]
    [InlineData(TvLifecycleState.CaughtUp, "source_removed", 2L, 0)]
    [InlineData(TvLifecycleState.SourceRemoved, "added", 1L, 2)]
    [InlineData(TvLifecycleState.Active, "added", 2L, 0)]
    [InlineData(TvLifecycleState.Active, "reactivated", 1L, 0)]
    public void Evaluate_ImpossiblePreviousLifecycleEventStateOrVersion_IsRejected(
        TvLifecycleState state,
        string eventType,
        long version,
        int confirmations)
    {
        int completedEpisodes = state == TvLifecycleState.CaughtUp ? 5 : 4;
        TvShow previous = CreatePrevious(
            state,
            airedEpisodes: 5,
            completedEpisodes,
            missingScheduledConfirmations: confirmations) with
        {
            LifecycleVersion = version,
            LastLifecycleEvent = $"tv:42:{version}:{eventType}"
        };

        Action action = () => EvaluateAbsent(
            previous,
            TvGenerationKind.ActivityFull,
            "generation-2");

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Fact]
    public void Evaluate_UnfinishedPreviousCaughtUpState_IsRejected()
    {
        TvShow previous = CreatePrevious(
            TvLifecycleState.CaughtUp,
            airedEpisodes: 5,
            completedEpisodes: 4);

        Action action = () => EvaluateAbsent(
            previous,
            TvGenerationKind.ActivityFull,
            "generation-2");

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Theory]
    [InlineData(TvLifecycleState.CaughtUp, 0)]
    [InlineData(TvLifecycleState.SourceRemoved, 2)]
    public void Evaluate_NonActivePreviousWatchlistState_IsRejected(
        TvLifecycleState state,
        int confirmations)
    {
        TvShow previous = CreatePrevious(
            state,
            airedEpisodes: 5,
            completedEpisodes: 5,
            missingScheduledConfirmations: confirmations) with
        {
            InWatchlist = true
        };

        Action action = () => EvaluateAbsent(
            previous,
            TvGenerationKind.ActivityFull,
            "generation-2");

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public void Evaluate_InvalidCurrentCounts_AreRejected(int airedEpisodes, int completedEpisodes)
    {
        Action action = () => EvaluateNew(inWatchlist: true, airedEpisodes, completedEpisodes);

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Fact]
    public void Evaluate_NewAbsentRow_IsRejected()
    {
        Action action = () => evaluator.Evaluate(
            previous: null,
            traktId: 42,
            presentInCurrentSource: false,
            inWatchlist: false,
            airedEpisodes: 0,
            completedEpisodes: 0,
            TvGenerationKind.ScheduledFull,
            "generation-1",
            Now);

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Theory]
    [InlineData(0L, TvGenerationKind.ScheduledFull, "generation-1")]
    [InlineData(-1L, TvGenerationKind.ScheduledFull, "generation-1")]
    [InlineData(42L, (TvGenerationKind)99, "generation-1")]
    [InlineData(42L, TvGenerationKind.ScheduledFull, "")]
    [InlineData(42L, TvGenerationKind.ScheduledFull, "  ")]
    public void Evaluate_InvalidIdentityGenerationKindOrGenerationId_IsRejected(
        long traktId,
        TvGenerationKind kind,
        string generationId)
    {
        Action action = () => evaluator.Evaluate(
            previous: null,
            traktId,
            presentInCurrentSource: true,
            inWatchlist: true,
            airedEpisodes: 1,
            completedEpisodes: 0,
            kind,
            generationId,
            Now);

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Fact]
    public void Evaluate_NonUtcOccurrenceTime_IsRejected()
    {
        Action action = () => evaluator.Evaluate(
            previous: null,
            traktId: 42,
            presentInCurrentSource: true,
            inWatchlist: true,
            airedEpisodes: 1,
            completedEpisodes: 0,
            TvGenerationKind.ScheduledFull,
            "generation-1",
            Now.ToOffset(TimeSpan.FromHours(2)));

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Fact]
    public void Evaluate_AbsentRowMarkedWatchlisted_IsRejected()
    {
        TvShow previous = CreatePrevious(TvLifecycleState.Active, airedEpisodes: 5, completedEpisodes: 4);

        Action action = () => evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: false,
            inWatchlist: true,
            airedEpisodes: 5,
            completedEpisodes: 4,
            TvGenerationKind.ScheduledFull,
            "generation-2",
            Now);

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    private TvLifecycleDecision EvaluateNew(bool inWatchlist, int airedEpisodes, int completedEpisodes)
    {
        return evaluator.Evaluate(
            previous: null,
            traktId: 42,
            presentInCurrentSource: true,
            inWatchlist,
            airedEpisodes,
            completedEpisodes,
            TvGenerationKind.ScheduledFull,
            "generation-1",
            Now);
    }

    private TvLifecycleDecision EvaluateAbsent(
        TvShow previous,
        TvGenerationKind kind,
        string generationId)
    {
        return evaluator.Evaluate(
            previous,
            previous.TraktId,
            presentInCurrentSource: false,
            inWatchlist: false,
            previous.AiredEpisodes,
            previous.CompletedEpisodes,
            kind,
            generationId,
            Now);
    }

    private static TvShow CreatePrevious(
        TvLifecycleState state,
        int airedEpisodes,
        int completedEpisodes,
        string generationId = "generation-1",
        int missingScheduledConfirmations = 0,
        string traktStatus = "returning series")
    {
        long lifecycleVersion = state == TvLifecycleState.SourceRemoved ? 2 : 1;
        string lifecycleEventType = state == TvLifecycleState.SourceRemoved
            ? "source_removed"
            : "added";
        return new TvShow(
            "tv-trakt-42",
            42,
            420,
            4_200,
            "tt0000042",
            TvIdentityStatus.Verified,
            "Example",
            2026,
            null,
            null,
            null,
            traktStatus,
            false,
            airedEpisodes,
            completedEpisodes,
            null,
            null,
            [],
            [],
            TvProviderAvailability.Unknown("PL"),
            state,
            $"tv:42:{lifecycleVersion}:{lifecycleEventType}",
            lifecycleVersion,
            missingScheduledConfirmations,
            Now.AddDays(-10),
            Now.AddDays(-1),
            Now.AddDays(-1),
            generationId,
            null);
    }
}
