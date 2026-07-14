namespace Watchlist.Application;

/// <summary>
/// Persists the single Trakt account connection.
/// </summary>
public interface ITraktConnectionRepository
{
    Task<TraktConnection?> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(TraktConnection connection, CancellationToken cancellationToken);

    Task DeleteAsync(CancellationToken cancellationToken);
}
