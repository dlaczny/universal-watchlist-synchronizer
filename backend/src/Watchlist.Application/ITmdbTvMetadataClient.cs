namespace Watchlist.Application;

public interface ITmdbTvMetadataClient
{
    Task<TmdbTvMetadataDto> GetTvMetadataAsync(int tmdbId, CancellationToken cancellationToken);

    Task<TmdbTvProviderDataDto> GetTvProvidersAsync(
        int tmdbId,
        CancellationToken cancellationToken);

    Task<TmdbTvProviderDataDto> GetSeasonProvidersAsync(
        int tmdbId,
        int seasonNumber,
        CancellationToken cancellationToken);

    Task<TmdbWatchProviderCatalogDto> GetProviderCatalogAsync(
        CancellationToken cancellationToken);

    Task<TmdbWatchProviderRegionsDto> GetProviderRegionsAsync(
        CancellationToken cancellationToken);
}
