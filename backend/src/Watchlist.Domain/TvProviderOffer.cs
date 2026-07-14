namespace Watchlist.Domain;

public sealed record TvProviderOffer(
    int ProviderId,
    string ProviderName,
    TvProviderCategory Category,
    string? LogoUrl);
