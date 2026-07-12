using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoWatchlistExportRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : IWatchlistExportRepository
{
    private readonly IMongoCollection<MongoWatchlistItemDocument> watchlistItems =
        database.GetCollection<MongoWatchlistItemDocument>(options.Value.WatchlistItemsCollectionName);

    private readonly ILetterboxdSourceSnapshotRepository sourceSnapshots =
        new MongoLetterboxdSourceSnapshotRepository(database, options);

    public async Task<WatchlistMovieLifecycleExport> GetMovieLifecycleAsync(
        CancellationToken cancellationToken)
    {
        LetterboxdSourceSnapshot? snapshot = await sourceSnapshots.GetLatestAsync(
            cancellationToken);
        IReadOnlyList<WatchlistExportMovieModel> activeMovies =
            await GetActiveMoviesAsync(snapshot, cancellationToken);
        IReadOnlyList<WatchlistWatchedMovieModel> watchedMovies =
            await GetWatchedMoviesAsync(snapshot, cancellationToken);

        return new WatchlistMovieLifecycleExport(snapshot, activeMovies, watchedMovies);
    }

    private async Task<IReadOnlyList<WatchlistExportMovieModel>> GetActiveMoviesAsync(
        LetterboxdSourceSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        FilterDefinition<MongoWatchlistItemDocument> filter =
            MongoLetterboxdLifecycleFilters.ActiveLetterboxdMovies(snapshot);

        List<MongoWatchlistItemDocument> documents = await watchlistItems
            .Find(filter)
            .ToListAsync(cancellationToken);

        return documents
            .Select(ToExportModel)
            .ToList();
    }

    private async Task<IReadOnlyList<WatchlistWatchedMovieModel>> GetWatchedMoviesAsync(
        LetterboxdSourceSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot is null || snapshot.WatchedMovies.Count == 0)
        {
            return [];
        }

        HashSet<string> watchedSourceIds = snapshot.WatchedMovies
            .Select(movie => movie.SourceId)
            .ToHashSet(StringComparer.Ordinal);
        FilterDefinitionBuilder<MongoWatchlistItemDocument> filter =
            Builders<MongoWatchlistItemDocument>.Filter;
        List<MongoWatchlistItemDocument> documents = await watchlistItems
            .Find(MongoLetterboxdLifecycleFilters.LetterboxdMovies()
                & filter.In(document => document.SourceId, watchedSourceIds))
            .ToListAsync(cancellationToken);
        Dictionary<string, MongoWatchlistItemDocument> documentsBySourceId = documents
            .ToDictionary(document => document.SourceId, StringComparer.Ordinal);

        return snapshot.WatchedMovies.Select(watched =>
        {
            if (!documentsBySourceId.TryGetValue(
                    watched.SourceId,
                    out MongoWatchlistItemDocument? document))
            {
                throw new InvalidOperationException(
                    $"Published watched movie {watched.SourceId} is missing its retained document.");
            }

            return new WatchlistWatchedMovieModel(
                document.TmdbId,
                document.ImdbId,
                document.Title,
                document.Year,
                document.SourceId,
                watched.WatchedAt,
                watched.LifecycleVersion,
                watched.LifecycleEventId);
        }).ToList();
    }

    public static WatchlistExportMovieModel ToExportModel(MongoWatchlistItemDocument document)
    {
        return new WatchlistExportMovieModel(
            document.SourceId,
            document.ImdbId,
            document.Title,
            document.Year,
            document.LetterboxdPath,
            document.OwnedServiceAvailability,
            document.TmdbId,
            document.TmdbMetadataStatus,
            document.AvailabilityStatus);
    }
}
