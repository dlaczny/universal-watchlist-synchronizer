namespace Watchlist.Application;

/// <summary>
/// Describes the public, non-sensitive state of the single Trakt connection.
/// </summary>
public sealed record TraktConnectionStatusDto(
    string Status,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? AccessTokenExpiresAt,
    string? LastErrorCode);
