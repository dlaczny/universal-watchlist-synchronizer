namespace Watchlist.Application;

/// <summary>
/// Contains identity-free watched progress for one Trakt episode position.
/// </summary>
public sealed record TraktDetailedEpisodeProgress(
    int SeasonNumber,
    int EpisodeNumber,
    bool Completed,
    DateTimeOffset? LastWatchedAt);
