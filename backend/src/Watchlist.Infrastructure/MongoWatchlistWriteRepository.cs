using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoWatchlistWriteRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : IWatchlistWriteRepository
{
    private readonly IMongoCollection<MongoWatchlistItemDocument> watchlistItems =
        database.GetCollection<MongoWatchlistItemDocument>(options.Value.WatchlistItemsCollectionName);

    private readonly IMongoCollection<MongoSyncRunDocument> syncRuns =
        database.GetCollection<MongoSyncRunDocument>(options.Value.SyncRunsCollectionName);

    public async Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
    {
        List<MongoWatchlistItemDocument> documents = await watchlistItems
            .Find(FilterDefinition<MongoWatchlistItemDocument>.Empty)
            .ToListAsync(cancellationToken);

        return documents.Select(document => document.ToDomain()).ToList();
    }

    public async Task<int> ApplyLetterboxdMovieSyncAsync(
        IReadOnlyList<WatchlistItemWriteModel> items,
        IReadOnlySet<string> sourceIds,
        string completedStatus,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        foreach (WatchlistItemWriteModel item in items)
        {
            MongoWatchlistItemDocument document = MongoWatchlistItemDocument.FromDomain(
                item.Item,
                item.ImdbId,
                item.LetterboxdPath);

            await watchlistItems.ReplaceOneAsync(
                stored => stored.Id == document.Id,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }

        DeleteResult deleteResult = await watchlistItems.DeleteManyAsync(
            CreateRemovedLetterboxdMovieFilter(sourceIds),
            cancellationToken);

        MongoSyncRunDocument syncRun = new()
        {
            Id = $"letterboxd-{completedAt:yyyyMMddHHmmssfffffff}",
            Status = completedStatus,
            LastSuccessfulSyncAt = completedAt
        };

        await syncRuns.InsertOneAsync(syncRun, cancellationToken: cancellationToken);

        return (int)deleteResult.DeletedCount;
    }

    private static FilterDefinition<MongoWatchlistItemDocument> CreateRemovedLetterboxdMovieFilter(
        IReadOnlySet<string> sourceIds)
    {
        FilterDefinitionBuilder<MongoWatchlistItemDocument> filter = Builders<MongoWatchlistItemDocument>.Filter;

        return filter.Eq(document => document.MediaType, MediaType.Movie)
            & filter.Eq(document => document.Source, WatchlistSource.Letterboxd)
            & filter.Nin(document => document.SourceId, sourceIds);
    }
}
