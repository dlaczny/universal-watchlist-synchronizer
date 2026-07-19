namespace Watchlist.Application;

public sealed record TvProviderOfferDto(
    int ProviderId,
    string ProviderName,
    string Category,
    string? LogoUrl);
