using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoSyncStatusReadRepository : ISyncStatusReadRepository
{
    private readonly IMongoCollection<MongoSyncRunDocument> collection;

    public MongoSyncStatusReadRepository(IMongoDatabase database, IOptions<MongoDbOptions> options)
    {
        collection = database.GetCollection<MongoSyncRunDocument>(
            options.Value.SyncRunsCollectionName);
    }

    public async Task<SyncStatusDto?> GetLatestAsync(CancellationToken cancellationToken)
    {
        MongoSyncRunDocument? document = await collection
            .Find(FilterDefinition<MongoSyncRunDocument>.Empty)
            .SortByDescending(syncRun => syncRun.LastSuccessfulSyncAt)
            .FirstOrDefaultAsync(cancellationToken);

        return document?.ToDto();
    }
}
