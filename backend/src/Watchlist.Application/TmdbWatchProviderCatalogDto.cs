namespace Watchlist.Application;

public sealed record TmdbWatchProviderCatalogDto(
    DateTimeOffset FetchedAt,
    IReadOnlyList<TmdbWatchProviderCatalogEntryDto> Providers)
{
    private IReadOnlyList<TmdbWatchProviderCatalogEntryDto> _providers = Snapshot(Providers);

    public DateTimeOffset FetchedAt { get; init; } =
        TmdbContractValidation.EnsureUtc(FetchedAt, nameof(FetchedAt));

    public IReadOnlyList<TmdbWatchProviderCatalogEntryDto> Providers
    {
        get => _providers;
        init => _providers = Snapshot(value);
    }

    private static IReadOnlyList<TmdbWatchProviderCatalogEntryDto> Snapshot(
        IReadOnlyList<TmdbWatchProviderCatalogEntryDto> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        HashSet<int> providerIds = [];
        TmdbWatchProviderCatalogEntryDto[] snapshot = values.ToArray();
        foreach (TmdbWatchProviderCatalogEntryDto? provider in snapshot)
        {
            if (provider is null || !providerIds.Add(provider.ProviderId))
            {
                throw new ArgumentException("Catalog providers must be non-null with unique IDs.", nameof(Providers));
            }
        }

        return Array.AsReadOnly(snapshot);
    }
}
