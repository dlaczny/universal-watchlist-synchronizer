namespace Watchlist.Application;

public interface IWatchlistExportRepository
{
    Task<IReadOnlyList<WatchlistExportMovieModel>> GetLetterboxdMoviesAsync(
        CancellationToken cancellationToken);
}
