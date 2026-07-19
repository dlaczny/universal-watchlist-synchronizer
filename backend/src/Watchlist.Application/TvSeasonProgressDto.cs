namespace Watchlist.Application;

public sealed record TvSeasonProgressDto(
    int SeasonNumber,
    int AiredEpisodes,
    int CompletedEpisodes,
    bool HasKnownFutureEpisode,
    string CleanupState,
    TvProviderAvailabilityDto Availability,
    IReadOnlyList<TvEpisodeProgressDto> Episodes);
