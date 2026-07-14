namespace Watchlist.Application;

/// <summary>
/// Indicates that Trakt definitely rejected a stored refresh token.
/// </summary>
public sealed class TraktRefreshRejectedException()
    : Exception("Trakt rejected the stored refresh token.");
