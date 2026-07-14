namespace Watchlist.Domain;

public sealed record TvSpecialEpisodeIdentity(
    long TraktEpisodeId,
    int? TvdbId,
    int SeasonNumber,
    int EpisodeNumber);
