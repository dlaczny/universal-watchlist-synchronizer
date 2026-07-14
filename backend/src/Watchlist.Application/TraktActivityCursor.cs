namespace Watchlist.Application;

/// <summary>
/// Identifies the Trakt TV activities that make a source read race-sensitive.
/// </summary>
public sealed record TraktActivityCursor(
    DateTimeOffset ShowWatchlistedAt,
    DateTimeOffset EpisodeWatchedAt);
