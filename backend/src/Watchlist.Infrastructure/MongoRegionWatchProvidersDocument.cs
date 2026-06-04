namespace Watchlist.Infrastructure;

public sealed class MongoRegionWatchProvidersDocument
{
    public IReadOnlyList<MongoWatchProviderDocument> Flatrate { get; init; } = [];

    public IReadOnlyList<MongoWatchProviderDocument> Rent { get; init; } = [];

    public IReadOnlyList<MongoWatchProviderDocument> Buy { get; init; } = [];
}
