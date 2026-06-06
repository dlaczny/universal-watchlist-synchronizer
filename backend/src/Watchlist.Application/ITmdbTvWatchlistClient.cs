namespace Watchlist.Application;

public interface ITmdbTvWatchlistClient
{
    Task<IReadOnlyList<TmdbTvWatchlistItemDto>> GetWatchlistAsync(CancellationToken cancellationToken);
}
