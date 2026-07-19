namespace Watchlist.Application;

public sealed record TvBrowseDto(
    int ContractVersion,
    string LifecycleState,
    string? LastLifecycleEvent,
    string TraktStatus,
    bool InWatchlist,
    string IdentityStatus,
    int AiredEpisodes,
    int CompletedEpisodes,
    TvEpisodeProgressDto? NextEpisode,
    bool SeasonCleanupPending,
    string PlexAvailability,
    TvProviderAvailabilityDto Availability,
    int? RelevantSeasonNumber,
    TvProviderAvailabilityDto? RelevantSeasonAvailability);
