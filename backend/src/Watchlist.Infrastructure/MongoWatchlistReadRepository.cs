using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoWatchlistReadRepository : IWatchlistReadRepository
{
    private readonly IMongoCollection<MongoWatchlistItemDocument> collection;
    private readonly ILetterboxdSourceSnapshotRepository sourceSnapshots;

    public MongoWatchlistReadRepository(IMongoDatabase database, IOptions<MongoDbOptions> options)
    {
        collection = database.GetCollection<MongoWatchlistItemDocument>(
            options.Value.WatchlistItemsCollectionName);
        sourceSnapshots = new MongoLetterboxdSourceSnapshotRepository(database, options);
    }

    public async Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
    {
        LetterboxdSourceSnapshot? snapshot = await sourceSnapshots.GetLatestAsync(
            cancellationToken);
        List<MongoWatchlistItemDocument> documents = await collection
            .Find(MongoLetterboxdLifecycleFilters.VisibleWatchlistItems(snapshot))
            .ToListAsync(cancellationToken);

        return documents.Select(document => document.ToDomain()).ToList();
    }
}
