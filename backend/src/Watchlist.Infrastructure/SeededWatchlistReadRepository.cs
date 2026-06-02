using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class SeededWatchlistReadRepository : IWatchlistReadRepository
{
    public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(SeedData.WatchlistItems);
    }
}
