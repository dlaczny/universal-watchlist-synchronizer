using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Reads normalized watchlist items from the application read store.
/// </summary>
public interface IWatchlistReadRepository
{
    /// <summary>
    /// Gets all normalized watchlist items.
    /// </summary>
    Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken);
}
