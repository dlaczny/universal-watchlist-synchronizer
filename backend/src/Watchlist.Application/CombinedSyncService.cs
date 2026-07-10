namespace Watchlist.Application;

public sealed class CombinedSyncService(
    ILetterboxdMovieSyncService letterboxdSyncService,
    ITmdbMovieEnrichmentService tmdbMovieEnrichmentService,
    ITmdbTvWatchlistSyncService tmdbTvWatchlistSyncService,
    IPlexMovieSyncService plexMovieSyncService,
    TimeProvider timeProvider) : ICombinedSyncService
{
    public async Task<CombinedSyncResultDto> SyncAllAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();

        LetterboxdSyncResultDto letterboxd = await letterboxdSyncService.SyncAsync(cancellationToken);
        TmdbMovieEnrichmentResultDto tmdb = await tmdbMovieEnrichmentService.SyncMoviesAsync(cancellationToken);
        TmdbTvSyncResultDto tmdbTv = await SyncTmdbTvOrSkipMissingConfigAsync(startedAt, cancellationToken);
        PlexMovieSyncResultDto plex = await plexMovieSyncService.SyncMoviesAsync(cancellationToken);

        DateTimeOffset finishedAt = timeProvider.GetUtcNow();
        string status = tmdbTv.Status == "skipped_missing_config" ? "partial" : "completed";

        return new CombinedSyncResultDto(
            status,
            startedAt,
            finishedAt,
            letterboxd,
            tmdb,
            tmdbTv,
            plex);
    }

    private async Task<TmdbTvSyncResultDto> SyncTmdbTvOrSkipMissingConfigAsync(
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await tmdbTvWatchlistSyncService.SyncAsync(cancellationToken);
        }
        catch (TmdbUnavailableException exception) when (IsMissingTmdbTvConfig(exception))
        {
            DateTimeOffset finishedAt = timeProvider.GetUtcNow();
            return new TmdbTvSyncResultDto(
                "skipped_missing_config",
                startedAt,
                finishedAt,
                0,
                0,
                0,
                0,
                0,
                0);
        }
    }

    private static bool IsMissingTmdbTvConfig(TmdbUnavailableException exception)
    {
        return exception.Message.Contains("TMDB account ID is not configured.", StringComparison.Ordinal)
            || exception.Message.Contains("TMDB session ID is not configured.", StringComparison.Ordinal);
    }
}
