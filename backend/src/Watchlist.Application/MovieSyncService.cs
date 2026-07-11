namespace Watchlist.Application;

public sealed class MovieSyncService(
    ILetterboxdMovieSyncService letterboxdSyncService,
    ITmdbMovieEnrichmentService tmdbMovieEnrichmentService,
    IPlexMovieSyncService plexMovieSyncService,
    TimeProvider timeProvider) : IMovieSyncService
{
    public async Task<MovieSyncResultDto> SyncAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();

        LetterboxdSyncResultDto letterboxd =
            await letterboxdSyncService.SyncAsync(cancellationToken);
        TmdbMovieEnrichmentResultDto tmdbMovies =
            await tmdbMovieEnrichmentService.SyncMoviesAsync(cancellationToken);
        PlexMovieSyncResultDto plexMovies =
            await plexMovieSyncService.SyncMoviesAsync(cancellationToken);

        DateTimeOffset finishedAt = timeProvider.GetUtcNow();
        string status = IsComplete(letterboxd, tmdbMovies, plexMovies)
            ? "completed"
            : "partial";

        return new MovieSyncResultDto(
            status,
            startedAt,
            finishedAt,
            letterboxd,
            tmdbMovies,
            plexMovies);
    }

    private static bool IsComplete(
        LetterboxdSyncResultDto letterboxd,
        TmdbMovieEnrichmentResultDto tmdbMovies,
        PlexMovieSyncResultDto plexMovies)
    {
        return string.Equals(letterboxd.Status, "completed", StringComparison.Ordinal)
            && string.Equals(tmdbMovies.Status, "completed", StringComparison.Ordinal)
            && tmdbMovies.ItemsNotFound == 0
            && tmdbMovies.ItemsFailed == 0
            && string.Equals(plexMovies.Status, "completed", StringComparison.Ordinal);
    }
}
