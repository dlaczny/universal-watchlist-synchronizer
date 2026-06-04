namespace Watchlist.Application;

/// <summary>
/// Runs the application-level Letterboxd movie watchlist sync.
/// </summary>
public interface ILetterboxdMovieSyncService
{
    /// <summary>
    /// Fetches Letterboxd movies, maps them to the normalized write model, and records a successful run.
    /// </summary>
    Task<LetterboxdSyncResultDto> SyncAsync(CancellationToken cancellationToken);
}
