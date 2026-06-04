namespace Watchlist.Application;

public interface ITmdbMovieClient
{
    Task<TmdbMovieMetadataDto> GetMovieMetadataAsync(
        int candidateTmdbId,
        string? imdbId,
        CancellationToken cancellationToken);
}
