namespace Watchlist.Application;

public sealed record WorkerTvShowDto(
    long TraktId,
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    string Title,
    int? Year,
    string IdentityStatus,
    bool InTraktWatchlist,
    string LifecycleState,
    long LifecycleVersion,
    string TraktStatus,
    int Aired,
    int Completed,
    WorkerTvEpisodeDto? LastWatchedEpisode,
    WorkerTvEpisodeDto? NextEpisode,
    bool SonarrDesired,
    bool SonarrMonitoredDesired,
    bool PlexWatchlistDesired,
    IReadOnlyList<WorkerTvSeasonDto> Seasons,
    TvProviderAvailabilityDto PolandAvailability,
    IReadOnlyList<string> Blockers);
