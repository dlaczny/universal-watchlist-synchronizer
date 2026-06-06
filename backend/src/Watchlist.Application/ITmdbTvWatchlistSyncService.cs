namespace Watchlist.Application;

public interface ITmdbTvWatchlistSyncService
{
    Task<TmdbTvSyncResultDto> SyncAsync(CancellationToken cancellationToken);
}
