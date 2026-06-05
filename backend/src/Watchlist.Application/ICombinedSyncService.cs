namespace Watchlist.Application;

public interface ICombinedSyncService
{
    Task<CombinedSyncResultDto> SyncAllAsync(CancellationToken cancellationToken);
}
