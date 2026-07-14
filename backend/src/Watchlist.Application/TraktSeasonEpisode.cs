namespace Watchlist.Application;

/// <summary>
/// Identifies one episode from a complete Trakt season schedule.
/// </summary>
public sealed record TraktSeasonEpisode(
    long TraktEpisodeId,
    int? TvdbId,
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    DateTimeOffset? FirstAired);
