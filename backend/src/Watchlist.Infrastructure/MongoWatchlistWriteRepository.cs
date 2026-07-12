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

    private readonly IMongoCollection<MongoLetterboxdSourceSnapshotDocument> sourceSnapshots =
        database.GetCollection<MongoLetterboxdSourceSnapshotDocument>(
            options.Value.LetterboxdSourceSnapshotsCollectionName);

    public async Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
    {
        List<MongoWatchlistItemDocument> documents = await watchlistItems
            .Find(FilterDefinition<MongoWatchlistItemDocument>.Empty)
            .ToListAsync(cancellationToken);

        return documents.Select(document => document.ToDomain()).ToList();
    }

    public async Task<LetterboxdMovieSyncApplyResult> ApplyLetterboxdMovieSyncAsync(
        IReadOnlyList<WatchlistItemWriteModel> items,
        IReadOnlySet<string> sourceIds,
        string completedStatus,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        List<MongoWatchlistItemDocument> existingDocuments = await watchlistItems
            .Find(CreateLetterboxdMovieFilter())
            .ToListAsync(cancellationToken);
        Dictionary<string, MongoWatchlistItemDocument> existingBySourceId = existingDocuments
            .ToDictionary(document => document.SourceId, StringComparer.Ordinal);
        MongoLetterboxdSourceSnapshotDocument? previousSnapshot = await sourceSnapshots
            .Find(FilterDefinition<MongoLetterboxdSourceSnapshotDocument>.Empty)
            .SortByDescending(snapshot => snapshot.PublishedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .FirstOrDefaultAsync(cancellationToken);
        HashSet<string> previousActiveIds = previousSnapshot is null
            ? existingDocuments.Select(document => document.SourceId).ToHashSet(StringComparer.Ordinal)
            : previousSnapshot.SourceIds.ToHashSet(StringComparer.Ordinal);
        Dictionary<string, MongoPublishedWatchedMovieDocument> previousWatchedBySourceId =
            (previousSnapshot?.WatchedMovies ?? [])
                .ToDictionary(movie => movie.SourceId, StringComparer.Ordinal);
        Dictionary<string, MongoPublishedWatchedMovieDocument> watchedBySourceId =
            previousWatchedBySourceId.Values
                .Where(movie => !sourceIds.Contains(movie.SourceId))
                .ToDictionary(movie => movie.SourceId, StringComparer.Ordinal);
        string snapshotId =
            $"letterboxd-{completedAt:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}";

        foreach (WatchlistItemWriteModel item in items)
        {
            MongoWatchlistItemDocument document = MongoWatchlistItemDocument.FromDomain(
                item.Item,
                item.ImdbId,
                item.LetterboxdPath);
            existingBySourceId.TryGetValue(document.SourceId, out MongoWatchlistItemDocument? existing);
            MongoMovieLifecycleEventDocument? lifecycleEvent = null;
            if (!previousActiveIds.Contains(document.SourceId))
            {
                string eventType = previousWatchedBySourceId.ContainsKey(document.SourceId)
                    ? "reactivated"
                    : "added";
                lifecycleEvent = CreateLifecycleEvent(
                    document.Id,
                    eventType,
                    snapshotId,
                    (existing?.LifecycleVersion ?? 0) + 1,
                    completedAt);
            }

            watchedBySourceId.Remove(document.SourceId);
            UpdateDefinition<MongoWatchlistItemDocument> update =
                CreateLetterboxdMovieUpsertUpdate(document, completedAt, lifecycleEvent);

            await watchlistItems.UpdateOneAsync(
                stored => stored.Id == document.Id,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }

        int watchedCount = 0;
        foreach (string removedSourceId in previousActiveIds.Except(sourceIds, StringComparer.Ordinal))
        {
            if (!existingBySourceId.TryGetValue(
                    removedSourceId,
                    out MongoWatchlistItemDocument? document))
            {
                continue;
            }

            MongoMovieLifecycleEventDocument watchedEvent = CreateLifecycleEvent(
                document.Id,
                "watched",
                snapshotId,
                document.LifecycleVersion + 1,
                completedAt);
            UpdateDefinitionBuilder<MongoWatchlistItemDocument> update =
                Builders<MongoWatchlistItemDocument>.Update;
            await watchlistItems.UpdateOneAsync(
                stored => stored.Id == document.Id,
                update
                    .Set(stored => stored.LastWatchedAt, completedAt)
                    .Set(stored => stored.LifecycleVersion, watchedEvent.LifecycleVersion)
                    .Set(stored => stored.UpdatedAt, completedAt)
                    .Push(stored => stored.LifecycleEvents, watchedEvent),
                cancellationToken: cancellationToken);

            watchedBySourceId[removedSourceId] = new MongoPublishedWatchedMovieDocument
            {
                SourceId = removedSourceId,
                LifecycleEventId = watchedEvent.EventId,
                WatchedAt = completedAt,
                LifecycleVersion = watchedEvent.LifecycleVersion
            };
            watchedCount++;
        }

        MongoLetterboxdSourceSnapshotDocument sourceSnapshot = new()
        {
            Id = snapshotId,
            PublishedAt = completedAt,
            SourceIds = sourceIds.Order(StringComparer.Ordinal).ToList(),
            WatchedMovies = watchedBySourceId.Values
                .OrderBy(movie => movie.SourceId, StringComparer.Ordinal)
                .ToList(),
            ItemCount = sourceIds.Count
        };
        await sourceSnapshots.InsertOneAsync(
            sourceSnapshot,
            cancellationToken: cancellationToken);

        MongoSyncRunDocument syncRun = new()
        {
            Id = snapshotId,
            Status = completedStatus,
            LastSuccessfulSyncAt = completedAt
        };

        await syncRuns.InsertOneAsync(syncRun, cancellationToken: cancellationToken);

        return new LetterboxdMovieSyncApplyResult(
            snapshotId,
            watchedCount);
    }

    private static FilterDefinition<MongoWatchlistItemDocument> CreateLetterboxdMovieFilter()
    {
        FilterDefinitionBuilder<MongoWatchlistItemDocument> filter = Builders<MongoWatchlistItemDocument>.Filter;

        return filter.Eq(document => document.MediaType, MediaType.Movie)
            & filter.Eq(document => document.Source, WatchlistSource.Letterboxd);
    }

    public async Task<TmdbTvWatchlistApplyResult> ApplyTmdbTvWatchlistSyncAsync(
        IReadOnlyList<WatchlistItemWriteModel> items,
        IReadOnlySet<string> sourceIds,
        string completedStatus,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        foreach (WatchlistItemWriteModel item in items)
        {
            UpdateDefinition<MongoWatchlistItemDocument> update = CreateTmdbTvUpsertUpdate(item);

            await watchlistItems.UpdateOneAsync(
                stored => stored.Id == item.Item.Id,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }

        DeleteResult deleteResult = await watchlistItems.DeleteManyAsync(
            CreateRemovedTmdbTvFilter(sourceIds),
            cancellationToken);

        MongoSyncRunDocument syncRun = new()
        {
            Id = $"tmdb-tv-{completedAt:yyyyMMddHHmmssfffffff}",
            Status = completedStatus,
            LastSuccessfulSyncAt = completedAt
        };

        await syncRuns.InsertOneAsync(syncRun, cancellationToken: cancellationToken);

        return new TmdbTvWatchlistApplyResult(items.Count, (int)deleteResult.DeletedCount);
    }

    private static FilterDefinition<MongoWatchlistItemDocument> CreateRemovedTmdbTvFilter(
        IReadOnlySet<string> sourceIds)
    {
        FilterDefinitionBuilder<MongoWatchlistItemDocument> filter = Builders<MongoWatchlistItemDocument>.Filter;

        return filter.Eq(document => document.MediaType, MediaType.TvShow)
            & filter.Eq(document => document.Source, WatchlistSource.Tmdb)
            & filter.Nin(document => document.SourceId, sourceIds);
    }

    private static UpdateDefinition<MongoWatchlistItemDocument> CreateTmdbTvUpsertUpdate(
        WatchlistItemWriteModel item)
    {
        UpdateDefinitionBuilder<MongoWatchlistItemDocument> update = Builders<MongoWatchlistItemDocument>.Update;

        return update
            .SetOnInsert(stored => stored.Id, item.Item.Id)
            .Set(stored => stored.MediaType, item.Item.MediaType)
            .Set(stored => stored.Source, item.Item.Source)
            .Set(stored => stored.SourceId, item.Item.SourceId)
            .Set(stored => stored.Title, item.Item.Title)
            .Set(stored => stored.Year, item.Item.Year)
            .Set(stored => stored.ImdbId, item.ImdbId)
            .Set(stored => stored.Overview, item.Item.Overview)
            .Set(stored => stored.PosterUrl, item.Item.PosterUrl)
            .Set(stored => stored.BackdropUrl, item.Item.BackdropUrl)
            .Set(stored => stored.Genres, item.Item.Genres)
            .Set(stored => stored.OriginalLanguage, item.Item.OriginalLanguage)
            .Set(stored => stored.TmdbVoteAverage, item.Item.TmdbVoteAverage)
            .Set(stored => stored.TmdbVoteCount, item.Item.TmdbVoteCount)
            .Set(stored => stored.ReleaseStatus, item.Item.ReleaseStatus)
            .Set(stored => stored.AvailabilityStatus, item.Item.AvailabilityStatus)
            .Set(stored => stored.TmdbId, item.TmdbId)
            .Set(stored => stored.TvdbId, item.TvdbId)
            .Set(stored => stored.TmdbTitle, item.Item.Title)
            .Set(stored => stored.OriginalTitle, item.Item.Title)
            .Set(stored => stored.ReleaseDate, null as string)
            .Set(stored => stored.TmdbMetadataUpdatedAt, DateTimeOffset.UtcNow)
            .Set(stored => stored.TmdbMetadataStatus, "enriched")
            .Set(stored => stored.TmdbMetadataError, null as string)
            .SetOnInsert(stored => stored.AddedAt, item.Item.AddedAt)
            .Set(stored => stored.UpdatedAt, item.Item.UpdatedAt);
    }

    private static UpdateDefinition<MongoWatchlistItemDocument> CreateLetterboxdMovieUpsertUpdate(
        MongoWatchlistItemDocument document,
        DateTimeOffset completedAt,
        MongoMovieLifecycleEventDocument? lifecycleEvent)
    {
        UpdateDefinitionBuilder<MongoWatchlistItemDocument> update = Builders<MongoWatchlistItemDocument>.Update;

        UpdateDefinition<MongoWatchlistItemDocument> result = update
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
            .Set(stored => stored.UpdatedAt, document.UpdatedAt)
            .Set(stored => stored.LastSeenInSourceAt, completedAt);

        if (lifecycleEvent is not null)
        {
            result = update.Combine(
                result,
                update.Set(stored => stored.LifecycleVersion, lifecycleEvent.LifecycleVersion),
                update.Push(stored => stored.LifecycleEvents, lifecycleEvent));
        }

        return result;
    }

    private static MongoMovieLifecycleEventDocument CreateLifecycleEvent(
        string documentId,
        string eventType,
        string snapshotId,
        long lifecycleVersion,
        DateTimeOffset occurredAt)
    {
        return new MongoMovieLifecycleEventDocument
        {
            EventId = $"{documentId}:{eventType}:{lifecycleVersion}:{snapshotId}",
            EventType = eventType,
            SourceSnapshotId = snapshotId,
            LifecycleVersion = lifecycleVersion,
            OccurredAt = occurredAt
        };
    }
}
