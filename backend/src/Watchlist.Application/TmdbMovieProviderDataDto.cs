namespace Watchlist.Application;

public sealed record TmdbMovieProviderDataDto(
    IReadOnlyDictionary<string, TmdbRegionWatchProvidersDto> Regions);
