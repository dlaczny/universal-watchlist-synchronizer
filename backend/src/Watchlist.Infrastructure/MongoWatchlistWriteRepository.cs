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
            UpdateDefinition<MongoWatchlistItemDocument> update = CreateLetterboxdMovieUpsertUpdate(document);

            await watchlistItems.UpdateOneAsync(
                stored => stored.Id == document.Id,
                update,
                new UpdateOptions { IsUpsert = true },
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

    private static UpdateDefinition<MongoWatchlistItemDocument> CreateLetterboxdMovieUpsertUpdate(
        MongoWatchlistItemDocument document)
    {
        UpdateDefinitionBuilder<MongoWatchlistItemDocument> update = Builders<MongoWatchlistItemDocument>.Update;

        return update
            .SetOnInsert(stored => stored.Id, document.Id)
            .Set(stored => stored.MediaType, document.MediaType)
            .Set(stored => stored.Source, document.Source)
            .Set(stored => stored.SourceId, document.SourceId)
            .Set(stored => stored.Title, document.Title)
            .Set(stored => stored.Year, document.Year)
            .Set(stored => stored.ImdbId, document.ImdbId)
            .Set(stored => stored.LetterboxdPath, document.LetterboxdPath)
            .Set(stored => stored.Overview, document.Overview)
            .Set(stored => stored.PosterUrl, document.PosterUrl)
            .Set(stored => stored.BackdropUrl, document.BackdropUrl)
            .Set(stored => stored.ReleaseStatus, document.ReleaseStatus)
            .Set(stored => stored.AvailabilityStatus, document.AvailabilityStatus)
            .Set(stored => stored.AddedAt, document.AddedAt)
            .Set(stored => stored.UpdatedAt, document.UpdatedAt);
    }
}
