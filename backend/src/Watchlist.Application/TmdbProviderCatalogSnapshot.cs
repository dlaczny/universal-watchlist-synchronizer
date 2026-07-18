namespace Watchlist.Application;

public sealed record TmdbProviderCatalogSnapshot(
    DateTimeOffset CatalogFetchedAt,
    DateTimeOffset RegionsFetchedAt,
    bool Stale,
    string? LastErrorCode,
    DateTimeOffset? LastErrorAt,
    IReadOnlyList<TmdbWatchProviderCatalogEntryDto> Providers,
    IReadOnlyList<string> RegionCodes)
{
    private IReadOnlyList<TmdbWatchProviderCatalogEntryDto> _providers =
        new TmdbWatchProviderCatalogDto(CatalogFetchedAt, Providers).Providers;
    private IReadOnlyList<string> _regionCodes =
        new TmdbWatchProviderRegionsDto(RegionsFetchedAt, RegionCodes).RegionCodes;

    public DateTimeOffset CatalogFetchedAt { get; init; } =
        TmdbContractValidation.EnsureUtc(CatalogFetchedAt, nameof(CatalogFetchedAt));

    public DateTimeOffset RegionsFetchedAt { get; init; } =
        TmdbContractValidation.EnsureUtc(RegionsFetchedAt, nameof(RegionsFetchedAt));

    public string? LastErrorCode { get; init; } = ValidateErrorCode(Stale, LastErrorCode, LastErrorAt);

    public DateTimeOffset? LastErrorAt { get; init; } = ValidateErrorAt(Stale, LastErrorCode, LastErrorAt);

    public IReadOnlyList<TmdbWatchProviderCatalogEntryDto> Providers
    {
        get => _providers;
        init => _providers = new TmdbWatchProviderCatalogDto(CatalogFetchedAt, value).Providers;
    }

    public IReadOnlyList<string> RegionCodes
    {
        get => _regionCodes;
        init => _regionCodes = new TmdbWatchProviderRegionsDto(RegionsFetchedAt, value).RegionCodes;
    }

    private static string? ValidateErrorCode(
        bool stale,
        string? errorCode,
        DateTimeOffset? errorAt)
    {
        if (!stale)
        {
            if (errorCode is not null || errorAt is not null)
            {
                throw new ArgumentException("Fresh catalog health cannot contain an error.");
            }

            return null;
        }

        if (errorAt is null)
        {
            throw new ArgumentException("Stale catalog health requires an error time.");
        }

        return TmdbContractValidation.EnsureStableErrorCode(
            errorCode ?? string.Empty,
            nameof(LastErrorCode));
    }

    private static DateTimeOffset? ValidateErrorAt(
        bool stale,
        string? errorCode,
        DateTimeOffset? errorAt)
    {
        if (!stale)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(errorCode) || errorAt is null)
        {
            throw new ArgumentException("Stale catalog health requires a stable error.");
        }

        return TmdbContractValidation.EnsureUtc(errorAt.Value, nameof(LastErrorAt));
    }
}
