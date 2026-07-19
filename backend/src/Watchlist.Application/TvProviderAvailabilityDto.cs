namespace Watchlist.Application;

public sealed record TvProviderAvailabilityDto(
    string State,
    string Region,
    DateTimeOffset? FetchedAt,
    string? Link,
    IReadOnlyList<TvProviderOfferDto> Offers);
