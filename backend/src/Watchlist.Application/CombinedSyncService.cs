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
        TmdbTvSyncResultDto tmdbTv = await tmdbTvWatchlistSyncService.SyncAsync(cancellationToken);
        PlexMovieSyncResultDto plex = await plexMovieSyncService.SyncMoviesAsync(cancellationToken);

        DateTimeOffset finishedAt = timeProvider.GetUtcNow();

        return new CombinedSyncResultDto(
            "completed",
            startedAt,
            finishedAt,
            letterboxd,
            tmdb,
            tmdbTv,
            plex);
    }
}
