namespace Watchlist.Application;

public interface ITvStatusService
{
    Task<TvSyncStatusDto> GetStatusAsync(CancellationToken cancellationToken);
}
