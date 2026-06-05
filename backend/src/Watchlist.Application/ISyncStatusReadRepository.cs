namespace Watchlist.Application;

/// <summary>
/// Reads the latest normalized sync state.
/// </summary>
public interface ISyncStatusReadRepository
{
    /// <summary>
    /// Gets the latest sync status, if one has been persisted.
    /// </summary>
    Task<SyncStatusDto?> GetLatestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the latest sync status for one persisted sync run status.
    /// </summary>
    Task<SyncStatusDto?> GetLatestByStatusAsync(
        string status,
        CancellationToken cancellationToken);
}
