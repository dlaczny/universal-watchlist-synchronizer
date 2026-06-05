using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class AvailabilityRefreshServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-05T12:00:00Z");

    [Fact]
    public async Task RefreshAsync_WhenLatestPlexSyncIsFresh_SkipsPlexSync()
    {
        FakeSyncStatusReadRepository statusRepository = new(
            new SyncStatusDto(SyncRunStatuses.PlexMoviesCompleted, Now.AddMinutes(-10)));
        FakePlexMovieSyncService plexSyncService = new();
        AvailabilityRefreshService service = new(
            statusRepository,
            plexSyncService,
            new FakeTimeProvider(Now));

        AvailabilityRefreshResultDto result = await service.RefreshAsync(CancellationToken.None);

        result.Status.Should().Be("skipped");
        result.RanPlexSync.Should().BeFalse();
        result.Reason.Should().Be("fresh");
        result.StartedAt.Should().Be(Now);
        result.FinishedAt.Should().Be(Now);
        result.Plex.Should().BeNull();
        plexSyncService.CallCount.Should().Be(0);
        statusRepository.RequestedStatus.Should().Be(SyncRunStatuses.PlexMoviesCompleted);
    }

    [Fact]
    public async Task RefreshAsync_WhenLatestPlexSyncIsStale_RunsPlexSync()
    {
        FakeSyncStatusReadRepository statusRepository = new(
            new SyncStatusDto(SyncRunStatuses.PlexMoviesCompleted, Now.AddMinutes(-16)));
        FakePlexMovieSyncService plexSyncService = new();
        AvailabilityRefreshService service = new(
            statusRepository,
            plexSyncService,
            new FakeTimeProvider(Now));

        AvailabilityRefreshResultDto result = await service.RefreshAsync(CancellationToken.None);

        result.Status.Should().Be("completed");
        result.RanPlexSync.Should().BeTrue();
        result.Reason.Should().Be("stale");
        result.Plex.Should().NotBeNull();
        result.Plex!.WatchlistItemsMatched.Should().Be(40);
        plexSyncService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAsync_WhenLatestPlexSyncIsMissing_RunsPlexSync()
    {
        FakeSyncStatusReadRepository statusRepository = new(null);
        FakePlexMovieSyncService plexSyncService = new();
        AvailabilityRefreshService service = new(
            statusRepository,
            plexSyncService,
            new FakeTimeProvider(Now));

        AvailabilityRefreshResultDto result = await service.RefreshAsync(CancellationToken.None);

        result.Status.Should().Be("completed");
        result.RanPlexSync.Should().BeTrue();
        result.Reason.Should().Be("missing");
        plexSyncService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshAsync_WhenPlexSyncFails_PropagatesException()
    {
        FakeSyncStatusReadRepository statusRepository = new(null);
        FakePlexMovieSyncService plexSyncService = new()
        {
            Exception = new PlexUnavailableException("Plex is unavailable.")
        };
        AvailabilityRefreshService service = new(
            statusRepository,
            plexSyncService,
            new FakeTimeProvider(Now));

        Func<Task> act = () => service.RefreshAsync(CancellationToken.None);

        await act.Should().ThrowAsync<PlexUnavailableException>();
    }

    private sealed class FakeSyncStatusReadRepository(SyncStatusDto? latest) : ISyncStatusReadRepository
    {
        public string? RequestedStatus { get; private set; }

        public Task<SyncStatusDto?> GetLatestAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(latest);
        }

        public Task<SyncStatusDto?> GetLatestByStatusAsync(
            string status,
            CancellationToken cancellationToken)
        {
            RequestedStatus = status;
            return Task.FromResult(latest);
        }
    }

    private sealed class FakePlexMovieSyncService : IPlexMovieSyncService
    {
        public int CallCount { get; private set; }

        public Exception? Exception { get; init; }

        public Task<PlexMovieSyncResultDto> SyncMoviesAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            if (Exception is not null)
            {
                return Task.FromException<PlexMovieSyncResultDto>(Exception);
            }

            PlexMovieSyncResultDto result = new(
                "completed",
                Now,
                Now.AddSeconds(5),
                1,
                500,
                500,
                2,
                40,
                220,
                3);

            return Task.FromResult(result);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
