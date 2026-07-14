namespace Watchlist.Domain;

public sealed record TvProviderAvailability(
    TvProviderState State,
    string Region,
    DateTimeOffset? FetchedAt,
    string? Link,
    IReadOnlyList<TvProviderOffer> Offers)
{
    public static TvProviderAvailability Unknown(string region) =>
        new(TvProviderState.Unknown, region, null, null, []);
}
