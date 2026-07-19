using Watchlist.Domain;

namespace Watchlist.Application;

public interface ITvSyncService
{
    Task<TvSyncResultDto> SyncAsync(
        TvGenerationKind kind,
        CancellationToken cancellationToken);
}
