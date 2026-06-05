using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoPlexMovieInventoryRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : IPlexMovieInventoryRepository
{
    private readonly IMongoCollection<MongoPlexLibraryItemDocument> plexItems =
        database.GetCollection<MongoPlexLibraryItemDocument>(options.Value.PlexLibraryItemsCollectionName);

    private readonly IMongoCollection<MongoWatchlistItemDocument> watchlistItems =
        database.GetCollection<MongoWatchlistItemDocument>(options.Value.WatchlistItemsCollectionName);

    private readonly IMongoCollection<MongoSyncRunDocument> syncRuns =
        database.GetCollection<MongoSyncRunDocument>(options.Value.SyncRunsCollectionName);

    public async Task<PlexInventoryApplyResult> ApplyMovieInventoryAsync(
        IReadOnlyList<PlexMovieDto> movies,
        IReadOnlySet<string> scannedSectionKeys,
        DateTimeOffset syncTime,
        CancellationToken cancellationToken)
    {
        foreach (PlexMovieDto movie in movies)
        {
            MongoPlexLibraryItemDocument document = MongoPlexLibraryItemDocument.FromDto(movie, syncTime);
            await plexItems.ReplaceOneAsync(
                item => item.Id == document.Id,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }

        HashSet<string> currentRatingKeys = movies
            .Select(movie => movie.RatingKey)
            .ToHashSet(StringComparer.Ordinal);
        FilterDefinitionBuilder<MongoPlexLibraryItemDocument> filter = Builders<MongoPlexLibraryItemDocument>.Filter;
        DeleteResult deleteResult = await plexItems.DeleteManyAsync(
            filter.Eq(item => item.MediaType, MediaType.Movie)
            & filter.In(item => item.LibrarySectionKey, scannedSectionKeys)
            & filter.Nin(item => item.RatingKey, currentRatingKeys),
            cancellationToken);

        return new PlexInventoryApplyResult(movies.Count, (int)deleteResult.DeletedCount);
    }

    public async Task<IReadOnlyList<PlexMovieDto>> GetMoviesAsync(CancellationToken cancellationToken)
    {
        List<MongoPlexLibraryItemDocument> documents = await plexItems
            .Find(item => item.MediaType == MediaType.Movie)
            .ToListAsync(cancellationToken);

        return documents.Select(document => document.ToDto()).ToList();
    }

    public async Task<IReadOnlyList<WatchlistItemWriteModel>> GetWatchlistMoviesAsync(CancellationToken cancellationToken)
    {
        List<MongoWatchlistItemDocument> documents = await watchlistItems
            .Find(item => item.MediaType == MediaType.Movie && item.Source == WatchlistSource.Letterboxd)
            .ToListAsync(cancellationToken);

        return documents.Select(document => new WatchlistItemWriteModel(
            document.ToDomain(),
            document.ImdbId,
            document.LetterboxdPath,
            document.TmdbId)).ToList();
    }

    public async Task ApplyMatchUpdatesAsync(
        IReadOnlyList<PlexMovieMatchUpdate> updates,
        string completedStatus,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        foreach (PlexMovieMatchUpdate update in updates)
        {
            await watchlistItems.UpdateOneAsync(
                item => item.Id == update.WatchlistItemId
                    && item.MediaType == MediaType.Movie
                    && item.Source == WatchlistSource.Letterboxd,
                Builders<MongoWatchlistItemDocument>.Update
                    .Set(item => item.AvailabilityStatus, update.AvailabilityStatus)
                    .Set(item => item.PlexRatingKey, update.PlexRatingKey)
                    .Set(item => item.PlexMatchedAt, update.PlexMatchedAt)
                    .Set(item => item.PlexMatchReason, update.PlexMatchReason)
                    .Set(item => item.PlexMatchConfidence, update.PlexMatchConfidence)
                    .Set(item => item.UpdatedAt, completedAt),
                cancellationToken: cancellationToken);
        }

        await syncRuns.InsertOneAsync(
            new MongoSyncRunDocument
            {
                Id = $"plex-movies-{completedAt:yyyyMMddHHmmssfffffff}",
                Status = completedStatus,
                LastSuccessfulSyncAt = completedAt
            },
            cancellationToken: cancellationToken);
    }
}
