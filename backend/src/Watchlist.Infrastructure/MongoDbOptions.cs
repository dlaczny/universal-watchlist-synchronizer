namespace Watchlist.Infrastructure;

public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    public string ConnectionString { get; init; } = string.Empty;

    public string DatabaseName { get; init; } = string.Empty;

    public string WatchlistItemsCollectionName { get; init; } = string.Empty;

    public string SyncRunsCollectionName { get; init; } = string.Empty;

    public string PlexLibraryItemsCollectionName { get; init; } = string.Empty;
}
