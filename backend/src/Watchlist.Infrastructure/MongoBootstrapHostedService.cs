using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Watchlist.Infrastructure;

public sealed class MongoBootstrapHostedService(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options,
    ILogger<MongoBootstrapHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BootstrapAsync(stoppingToken);
                return;
            }
            catch (MongoException exception)
            {
                logger.LogWarning(exception, "MongoDB bootstrap failed. Retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        IMongoCollection<MongoWatchlistItemDocument> watchlistItems =
            database.GetCollection<MongoWatchlistItemDocument>(options.Value.WatchlistItemsCollectionName);
        IMongoCollection<MongoSyncRunDocument> syncRuns =
            database.GetCollection<MongoSyncRunDocument>(options.Value.SyncRunsCollectionName);

        if (await watchlistItems.CountDocumentsAsync(
                FilterDefinition<MongoWatchlistItemDocument>.Empty,
                cancellationToken: cancellationToken) == 0)
        {
            IEnumerable<MongoWatchlistItemDocument> documents =
                SeedData.WatchlistItems.Select(item => MongoWatchlistItemDocument.FromDomain(item));
            await watchlistItems.InsertManyAsync(documents, cancellationToken: cancellationToken);
        }

        if (await syncRuns.CountDocumentsAsync(
                FilterDefinition<MongoSyncRunDocument>.Empty,
                cancellationToken: cancellationToken) == 0)
        {
            await syncRuns.InsertManyAsync(SeedData.SyncRuns, cancellationToken: cancellationToken);
        }
    }
}
