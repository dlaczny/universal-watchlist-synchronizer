namespace Watchlist.Application;

public sealed record WorkerTvEpisodeDto(
    long TraktEpisodeId,
    int SeasonNumber,
    int EpisodeNumber,
    int? TvdbId,
    string? Title,
    DateTimeOffset? FirstAired,
    bool Aired,
    bool Watched,
    DateTimeOffset? LastWatchedAt,
    string? PlexRatingKey,
    bool? WatchedByConfiguredPlexAccount,
    DateTimeOffset? PlexLastViewedAt);
