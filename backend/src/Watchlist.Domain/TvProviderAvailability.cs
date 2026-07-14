namespace Watchlist.Domain;

public sealed record TvProviderAvailability(
    TvProviderState State,
    string Region,
    DateTimeOffset? FetchedAt,
    string? Link,
    IReadOnlyList<TvProviderOffer> Offers)
{
    private IReadOnlyList<TvProviderOffer> _offers = Snapshot(Offers);

    public IReadOnlyList<TvProviderOffer> Offers
    {
        get => _offers;
        init => _offers = Snapshot(value);
    }

    public static TvProviderAvailability Unknown(string region) =>
        new(TvProviderState.Unknown, region, null, null, []);

    private static IReadOnlyList<TvProviderOffer> Snapshot(IReadOnlyList<TvProviderOffer> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
