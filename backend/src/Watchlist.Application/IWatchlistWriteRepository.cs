using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Write-side watchlist persistence boundary used by sync services.
/// </summary>
public interface IWatchlistWriteRepository
{
    /// <summary>
    /// Gets existing normalized watchlist items for preservation and deletion decisions.
    /// </summary>
    Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Applies one completed Letterboxd movie sync as a single persistence operation.
    /// </summary>
    Task<int> ApplyLetterboxdMovieSyncAsync(
        IReadOnlyList<WatchlistItemWriteModel> items,
        IReadOnlySet<string> sourceIds,
        string completedStatus,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
}
