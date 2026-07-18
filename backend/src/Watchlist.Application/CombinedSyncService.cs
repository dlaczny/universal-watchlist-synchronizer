namespace Watchlist.Application;

public sealed class CombinedSyncService(
    ILetterboxdMovieSyncService letterboxdSyncService,
    ITmdbMovieEnrichmentService tmdbMovieEnrichmentService,
    IPlexMovieSyncService plexMovieSyncService,
    TimeProvider timeProvider) : ICombinedSyncService
{
    public async Task<CombinedSyncResultDto> SyncAllAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();

        LetterboxdSyncResultDto letterboxd = await letterboxdSyncService.SyncAsync(cancellationToken);
        TmdbMovieEnrichmentResultDto tmdb = await tmdbMovieEnrichmentService.SyncMoviesAsync(cancellationToken);
        DateTimeOffset tvDisabledAt = timeProvider.GetUtcNow();
        TmdbTvSyncResultDto tmdbTv = new(
            "disabled",
            tvDisabledAt,
            tvDisabledAt,
            0,
            0,
            0,
            0,
            0,
            0);
        PlexMovieSyncResultDto plex = await plexMovieSyncService.SyncMoviesAsync(cancellationToken);

        DateTimeOffset finishedAt = timeProvider.GetUtcNow();

        return new CombinedSyncResultDto(
            "partial",
            startedAt,
            finishedAt,
            letterboxd,
            tmdb,
            tmdbTv,
            plex);
    }
}
