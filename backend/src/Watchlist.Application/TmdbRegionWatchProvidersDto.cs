namespace Watchlist.Application;

public sealed record TmdbRegionWatchProvidersDto(
    IReadOnlyList<TmdbWatchProviderDto> Flatrate,
    IReadOnlyList<TmdbWatchProviderDto> Rent,
    IReadOnlyList<TmdbWatchProviderDto> Buy);
