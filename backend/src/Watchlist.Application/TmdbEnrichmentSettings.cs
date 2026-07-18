namespace Watchlist.Application;

public sealed record TmdbEnrichmentSettings(
    string ProviderRegion,
    IReadOnlyList<int> OwnedProviderIds,
    TimeSpan MetadataRefreshInterval,
    TimeSpan ProviderCacheLifetime)
{
    private IReadOnlyList<int> _ownedProviderIds = Snapshot(OwnedProviderIds);

    public string ProviderRegion { get; init; } =
        TmdbContractValidation.EnsureRegionCode(ProviderRegion, nameof(ProviderRegion));

    public IReadOnlyList<int> OwnedProviderIds
    {
        get => _ownedProviderIds;
        init => _ownedProviderIds = Snapshot(value);
    }

    public TimeSpan MetadataRefreshInterval { get; init; } =
        EnsurePositive(MetadataRefreshInterval, nameof(MetadataRefreshInterval));

    public TimeSpan ProviderCacheLifetime { get; init; } =
        EnsurePositive(ProviderCacheLifetime, nameof(ProviderCacheLifetime));

    private static IReadOnlyList<int> Snapshot(IReadOnlyList<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int[] snapshot = values.ToArray();
        if (snapshot.Length == 0
            || snapshot.Any(providerId => providerId <= 0)
            || snapshot.Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("Owned provider IDs must be positive and unique.", nameof(OwnedProviderIds));
        }

        return Array.AsReadOnly(snapshot);
    }

    private static TimeSpan EnsurePositive(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }
}
