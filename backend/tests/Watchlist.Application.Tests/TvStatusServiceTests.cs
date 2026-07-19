using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class TvStatusServiceTests
{
    [Fact]
    public async Task Status_ReportsConnectionGenerationCountsAndLockedHealthReasons()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-19T13:00:00Z");
        TvStatusService service = new(
            new FakeRepository(CreatePublishedGeneration(now.AddHours(-1))),
            new FakeConnection(),
            new FixedTimeProvider(now));

        TvSyncStatusDto status = await service.GetStatusAsync(CancellationToken.None);

        status.ConnectionStatus.Should().Be("connected");
        status.LastActivityPoll.Should().BeNull();
        status.LastCompleteGeneration.Should().Be(now.AddHours(-1));
        status.GenerationAge.Should().Be(TimeSpan.FromHours(1));
        status.ActiveCount.Should().Be(1);
        status.CaughtUpCount.Should().Be(1);
        status.SourceRemovedCount.Should().Be(1);
        status.MutationCapable.Should().BeFalse();
        status.HealthReasons.Should().ContainInOrder(
            "plex_history_phase_not_implemented",
            "worker_tv_mutation_disabled");
    }

    [Fact]
    public async Task Status_UsesTheLastScheduledFullGenerationAfterAnActivityRefresh()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-19T13:00:00Z");
        DateTimeOffset lastScheduledFull = now.AddHours(-2);
        PublishedTvGeneration published = CreatePublishedGeneration(now.AddHours(-1)) with
        {
            Manifest = CreatePublishedGeneration(now.AddHours(-1)).Manifest with
            {
                Kind = TvGenerationKind.ActivityFull,
                LastScheduledFullAt = lastScheduledFull
            }
        };
        TvStatusService service = new(
            new FakeRepository(published),
            new FakeConnection(),
            new FixedTimeProvider(now));

        TvSyncStatusDto status = await service.GetStatusAsync(CancellationToken.None);

        status.LastActivityPoll.Should().BeNull();
        status.LastCompleteGeneration.Should().Be(lastScheduledFull);
        status.GenerationAge.Should().Be(TimeSpan.FromHours(2));
    }

    private static PublishedTvGeneration CreatePublishedGeneration(DateTimeOffset time)
    {
        TvProviderAvailability availability = TvProviderAvailability.Unknown("PL");
        TvShow active = CreateShow(1, TvLifecycleState.Active, availability, time);
        TvShow caughtUp = CreateShow(2, TvLifecycleState.CaughtUp, availability, time);
        TvShow removed = CreateShow(3, TvLifecycleState.SourceRemoved, availability, time);
        TvGenerationManifest manifest = new(
            "tv-status", null, TvGenerationKind.ScheduledFull, time.AddMinutes(-1), time, time,
            new TraktActivityCursor(time.AddMinutes(-2), time.AddMinutes(-2)), 1, 3, 1, 3,
            "v1", new Dictionary<string, string>(), "membership", "progress", null, null, null,
            "valid", [], [], [], false,
            ["plex_history_phase_not_implemented", "worker_tv_mutation_disabled"], ["tmdb_parse_error"])
        {
            LastScheduledFullAt = time
        };
        return new PublishedTvGeneration(manifest, [active, caughtUp, removed]);
    }

    private static TvShow CreateShow(long traktId, TvLifecycleState state, TvProviderAvailability availability, DateTimeOffset time) =>
        new($"tv-trakt-{traktId}", traktId, 1, 1, null, TvIdentityStatus.Verified, "Show", null,
            null, null, null, "returning series", state == TvLifecycleState.Active, 0, 0, null, null,
            [], [], availability, state, $"tv:{traktId}:{(state == TvLifecycleState.Active ? 1 : 2)}:{(state == TvLifecycleState.CaughtUp ? "caught_up" : state == TvLifecycleState.SourceRemoved ? "source_removed" : "added")}",
            state == TvLifecycleState.Active ? 1 : 2, state == TvLifecycleState.SourceRemoved ? 2 : 0,
            time, time, time, "tv-status", null);

    private sealed class FakeRepository(PublishedTvGeneration published) : ITvGenerationRepository
    {
        public Task StageAsync(TvGenerationDraft draft, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishAsync(TvGenerationManifest manifest, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken) => Task.FromResult<PublishedTvGeneration?>(published);
    }

    private sealed class FakeConnection : ITraktConnectionService
    {
        public Task<TraktDeviceStartDto> StartDeviceAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TraktConnectionStatusDto> PollPendingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TraktConnectionStatusDto> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new TraktConnectionStatusDto("connected", null, null, null));
        public Task<TraktConnectionStatusDto> DisconnectAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
