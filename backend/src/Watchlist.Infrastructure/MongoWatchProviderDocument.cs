namespace Watchlist.Infrastructure;

public sealed class MongoWatchProviderDocument
{
    public int ProviderId { get; init; }

    public string ProviderName { get; init; } = string.Empty;

    public string? LogoPath { get; init; }

    public int DisplayPriority { get; init; }
}
