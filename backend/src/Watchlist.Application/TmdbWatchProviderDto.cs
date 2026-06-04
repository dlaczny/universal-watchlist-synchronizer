namespace Watchlist.Application;

public sealed record TmdbWatchProviderDto(
    int ProviderId,
    string ProviderName,
    string? LogoPath,
    int DisplayPriority);
