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

    public async Task<IReadOnlyList<WatchlistExportMovieModel>> GetLetterboxdMoviesAsync(
        CancellationToken cancellationToken)
    {
        FilterDefinition<MongoWatchlistItemDocument> filter =
            Builders<MongoWatchlistItemDocument>.Filter.Eq(document => document.MediaType, MediaType.Movie)
            & Builders<MongoWatchlistItemDocument>.Filter.Eq(document => document.Source, WatchlistSource.Letterboxd);

        List<MongoWatchlistItemDocument> documents = await watchlistItems
            .Find(filter)
            .ToListAsync(cancellationToken);

        return documents
            .Select(ToExportModel)
            .ToList();
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
