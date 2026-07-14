namespace Watchlist.Domain;

public sealed record TvSeasonProgress(
    int SeasonNumber,
    int AiredEpisodes,
    int CompletedEpisodes,
    bool HasKnownFutureEpisode,
    TvProviderAvailability Availability,
    IReadOnlyList<TvEpisodeProgress> Episodes);
