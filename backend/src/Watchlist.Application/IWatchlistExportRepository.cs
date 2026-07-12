namespace Watchlist.Application;

public interface IWatchlistExportRepository
{
    Task<WatchlistMovieLifecycleExport> GetMovieLifecycleAsync(
        CancellationToken cancellationToken);
}
