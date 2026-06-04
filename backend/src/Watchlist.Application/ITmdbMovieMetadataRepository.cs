namespace Watchlist.Application;

public interface ITmdbMovieMetadataRepository
{
    Task<IReadOnlyList<WatchlistItemWriteModel>> GetLetterboxdMoviesAsync(CancellationToken cancellationToken);

    Task<WatchlistItemWriteModel?> GetLetterboxdMovieAsync(string id, CancellationToken cancellationToken);

    Task ApplyTmdbMetadataAsync(
        string id,
        TmdbMovieMetadataUpdate update,
        CancellationToken cancellationToken);
}
