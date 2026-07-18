namespace Watchlist.Application;

public sealed record TmdbTvProviderDataDto(
    string Region,
    TmdbProviderRegionPresence RegionPresence,
    DateTimeOffset FetchedAt,
    string? Link,
    IReadOnlyList<TmdbTvProviderOfferDto> Offers)
{
    private IReadOnlyList<TmdbTvProviderOfferDto> _offers = Snapshot(Offers, RegionPresence, Link);

    public string Region { get; init; } =
        TmdbContractValidation.EnsureRegionCode(Region, nameof(Region));

    public TmdbProviderRegionPresence RegionPresence { get; init; } =
        Enum.IsDefined(RegionPresence)
            ? RegionPresence
            : throw new ArgumentOutOfRangeException(nameof(RegionPresence));

    public DateTimeOffset FetchedAt { get; init; } =
        TmdbContractValidation.EnsureUtc(FetchedAt, nameof(FetchedAt));

    public string? Link { get; init; } = ValidateLink(Link, RegionPresence);

    public IReadOnlyList<TmdbTvProviderOfferDto> Offers
    {
        get => _offers;
        init => _offers = Snapshot(value, RegionPresence, Link);
    }

    private static string? ValidateLink(string? link, TmdbProviderRegionPresence presence)
    {
        string? normalized = TmdbContractValidation.NormalizeOptional(link);
        if (presence == TmdbProviderRegionPresence.Missing && normalized is not null)
        {
            throw new ArgumentException("A missing region cannot contain a provider link.", nameof(Link));
        }

        return normalized;
    }

    private static IReadOnlyList<TmdbTvProviderOfferDto> Snapshot(
        IReadOnlyList<TmdbTvProviderOfferDto> values,
        TmdbProviderRegionPresence presence,
        string? link)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (presence == TmdbProviderRegionPresence.Missing
            && (values.Count != 0 || !string.IsNullOrWhiteSpace(link)))
        {
            throw new ArgumentException("A missing region cannot contain provider data.", nameof(Offers));
        }

        HashSet<(int ProviderId, string Category)> identities = [];
        TmdbTvProviderOfferDto[] snapshot = values.ToArray();
        foreach (TmdbTvProviderOfferDto? offer in snapshot)
        {
            if (offer is null || !identities.Add((offer.ProviderId, offer.Category)))
            {
                throw new ArgumentException("Provider offers must be non-null and unique by ID/category.", nameof(Offers));
            }
        }

        return Array.AsReadOnly(snapshot);
    }
}
