namespace Watchlist.Application;

public interface ITmdbMovieEnrichmentService
{
    Task<TmdbMovieEnrichmentResultDto> SyncMoviesAsync(CancellationToken cancellationToken);

    Task<TmdbSingleMovieEnrichmentResultDto?> SyncMovieAsync(string id, CancellationToken cancellationToken);
}
