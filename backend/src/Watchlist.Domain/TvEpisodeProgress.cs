namespace Watchlist.Domain;

public sealed record TvEpisodeProgress(
    long TraktEpisodeId,
    int? TvdbId,
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    DateTimeOffset? AiredAt,
    bool Watched,
    DateTimeOffset? WatchedAt);
