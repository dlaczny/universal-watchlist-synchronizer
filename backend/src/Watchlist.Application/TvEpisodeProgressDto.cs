namespace Watchlist.Application;

public sealed record TvEpisodeProgressDto(
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    DateTimeOffset? AiredAt,
    bool Watched,
    DateTimeOffset? WatchedAt);
