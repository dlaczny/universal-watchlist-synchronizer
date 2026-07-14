namespace Watchlist.Application;

/// <summary>
/// Exchanges Trakt device codes and refresh tokens for OAuth grants.
/// </summary>
public interface ITraktOAuthClient
{
    Task<TraktDeviceCode> StartDeviceAsync(CancellationToken cancellationToken);

    Task<TraktTokenGrant?> PollDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken);

    Task<TraktTokenGrant> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);
}
