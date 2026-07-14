namespace Watchlist.Application;

/// <summary>
/// Represents one show from the authenticated Trakt watchlist.
/// </summary>
public sealed record TraktWatchlistShow(
    TraktShowIds Ids,
    string Title,
    int? Year,
    DateTimeOffset ListedAt);
