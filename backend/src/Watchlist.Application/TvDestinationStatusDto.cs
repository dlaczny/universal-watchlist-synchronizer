namespace Watchlist.Application;

public sealed record TvDestinationStatusDto(
    string SonarrState,
    string PlexWatchlistState,
    DateTimeOffset? ObservedAt);
