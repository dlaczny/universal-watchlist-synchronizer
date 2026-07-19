namespace Watchlist.Application;

public sealed record TvDetailsDto(
    int ContractVersion,
    string LifecycleState,
    string? LastLifecycleEvent,
    string TraktStatus,
    bool InWatchlist,
    string IdentityStatus,
    int AiredEpisodes,
    int CompletedEpisodes,
    TvEpisodeProgressDto? LastWatchedEpisode,
    TvEpisodeProgressDto? NextEpisode,
    TvProviderAvailabilityDto Availability,
    TvDestinationStatusDto Destinations,
    IReadOnlyList<TvSeasonProgressDto> Seasons);
