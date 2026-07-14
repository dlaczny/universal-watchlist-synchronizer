namespace Watchlist.Application;

/// <summary>
/// Supplies a valid access token for authenticated Trakt API calls.
/// </summary>
public interface ITraktAccessTokenProvider
{
    Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken);

    Task<string> ForceRefreshAsync(CancellationToken cancellationToken);
}
