namespace Watchlist.Application;

public sealed class AvailabilityRefreshService(
    ISyncStatusReadRepository syncStatusRepository,
    IPlexMovieSyncService plexMovieSyncService,
    TimeProvider timeProvider) : IAvailabilityRefreshService
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(15);

    public async Task<AvailabilityRefreshResultDto> RefreshAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        SyncStatusDto? latestPlexSync = await syncStatusRepository.GetLatestByStatusAsync(
            SyncRunStatuses.PlexMoviesCompleted,
            cancellationToken);

        if (latestPlexSync?.LastSuccessfulSyncAt is DateTimeOffset lastSuccessfulSyncAt
            && startedAt - lastSuccessfulSyncAt <= FreshnessWindow)
        {
            return new AvailabilityRefreshResultDto(
                "skipped",
                false,
                "fresh",
                startedAt,
                timeProvider.GetUtcNow(),
                null);
        }

        string reason = latestPlexSync?.LastSuccessfulSyncAt is null ? "missing" : "stale";
        PlexMovieSyncResultDto plex = await plexMovieSyncService.SyncMoviesAsync(cancellationToken);

        return new AvailabilityRefreshResultDto(
            "completed",
            true,
            reason,
            startedAt,
            timeProvider.GetUtcNow(),
            plex);
    }
}
