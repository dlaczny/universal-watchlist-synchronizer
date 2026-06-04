namespace Watchlist.Application;

/// <summary>
/// Persists Letterboxd sync run status metadata.
/// </summary>
public interface ILetterboxdSyncRunRepository
{
    /// <summary>
    /// Inserts a successful Letterboxd sync run marker.
    /// </summary>
    Task InsertSuccessfulRunAsync(
        string status,
        DateTimeOffset finishedAt,
        CancellationToken cancellationToken);
}
