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
    /// Upserts normalized watchlist items with backend-only source trace fields.
    /// </summary>
    Task UpsertItemsAsync(
        IReadOnlyList<WatchlistItemWriteModel> items,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes Letterboxd movie records not present in the supplied source identifiers.
    /// </summary>
    Task<int> DeleteLetterboxdMoviesExceptAsync(
        IReadOnlySet<string> sourceIds,
        CancellationToken cancellationToken);
}
