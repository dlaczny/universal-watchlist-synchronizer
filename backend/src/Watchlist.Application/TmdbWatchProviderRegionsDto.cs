namespace Watchlist.Application;

public sealed record TmdbWatchProviderRegionsDto(
    DateTimeOffset FetchedAt,
    IReadOnlyList<string> RegionCodes)
{
    private IReadOnlyList<string> _regionCodes = Snapshot(RegionCodes);

    public DateTimeOffset FetchedAt { get; init; } =
        TmdbContractValidation.EnsureUtc(FetchedAt, nameof(FetchedAt));

    public IReadOnlyList<string> RegionCodes
    {
        get => _regionCodes;
        init => _regionCodes = Snapshot(value);
    }

    private static IReadOnlyList<string> Snapshot(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        HashSet<string> uniqueCodes = new(StringComparer.Ordinal);
        string[] snapshot = values.ToArray();
        for (int index = 0; index < snapshot.Length; index++)
        {
            string code = TmdbContractValidation.EnsureRegionCode(snapshot[index], nameof(RegionCodes));
            if (!uniqueCodes.Add(code))
            {
                throw new ArgumentException("Provider region codes must be unique.", nameof(RegionCodes));
            }
        }

        return Array.AsReadOnly(snapshot);
    }
}
