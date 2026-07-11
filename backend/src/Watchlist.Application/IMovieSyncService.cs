namespace Watchlist.Application;

public interface IMovieSyncService
{
    Task<MovieSyncResultDto> SyncAsync(CancellationToken cancellationToken);
}
