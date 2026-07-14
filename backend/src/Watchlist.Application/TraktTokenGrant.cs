namespace Watchlist.Application;

/// <summary>
/// Represents an access and refresh token grant returned by Trakt.
/// </summary>
public sealed record TraktTokenGrant(
    string AccessToken,
    string RefreshToken,
    TimeSpan ExpiresIn,
    DateTimeOffset CreatedAt);
