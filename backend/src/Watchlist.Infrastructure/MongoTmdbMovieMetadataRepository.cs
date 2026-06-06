using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTmdbMovieMetadataRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : ITmdbMovieMetadataRepository
{
    private const string FailedStatus = "failed";
    private const string NotFoundStatus = "not_found";

    private readonly IMongoCollection<MongoWatchlistItemDocument> watchlistItems =
        database.GetCollection<MongoWatchlistItemDocument>(options.Value.WatchlistItemsCollectionName);

    public async Task<IReadOnlyList<WatchlistItemWriteModel>> GetLetterboxdMoviesAsync(
        CancellationToken cancellationToken)
    {
        List<MongoWatchlistItemDocument> documents = await watchlistItems
            .Find(CreateLetterboxdMovieFilter())
            .ToListAsync(cancellationToken);

        return documents.Select(ToWriteModel).ToList();
    }

    public async Task<WatchlistItemWriteModel?> GetLetterboxdMovieAsync(
        string id,
        CancellationToken cancellationToken)
    {
        FilterDefinition<MongoWatchlistItemDocument> filter =
            CreateLetterboxdMovieFilter() & Builders<MongoWatchlistItemDocument>.Filter.Eq(
                document => document.Id,
                id);

        MongoWatchlistItemDocument? document = await watchlistItems
            .Find(filter)
            .SingleOrDefaultAsync(cancellationToken);

        return document is null ? null : ToWriteModel(document);
    }

    public async Task ApplyTmdbMetadataAsync(
        string id,
        TmdbMovieMetadataUpdate update,
        CancellationToken cancellationToken)
    {
        FilterDefinition<MongoWatchlistItemDocument> filter =
            CreateLetterboxdMovieFilter() & Builders<MongoWatchlistItemDocument>.Filter.Eq(
                document => document.Id,
                id);

        await watchlistItems.UpdateOneAsync(
            filter,
            CreateTmdbMetadataUpdate(update),
            cancellationToken: cancellationToken);
    }

    private static FilterDefinition<MongoWatchlistItemDocument> CreateLetterboxdMovieFilter()
    {
        FilterDefinitionBuilder<MongoWatchlistItemDocument> filter = Builders<MongoWatchlistItemDocument>.Filter;

        return filter.Eq(document => document.MediaType, MediaType.Movie)
            & filter.Eq(document => document.Source, WatchlistSource.Letterboxd);
    }

    private static UpdateDefinition<MongoWatchlistItemDocument> CreateTmdbMetadataUpdate(
        TmdbMovieMetadataUpdate metadata)
    {
        UpdateDefinitionBuilder<MongoWatchlistItemDocument> update = Builders<MongoWatchlistItemDocument>.Update;

        if (metadata.MetadataStatus is FailedStatus or NotFoundStatus)
        {
            return update
                .Set(document => document.TmdbMetadataUpdatedAt, metadata.UpdatedAt)
                .Set(document => document.TmdbMetadataStatus, metadata.MetadataStatus)
                .Set(document => document.TmdbMetadataError, metadata.MetadataError);
        }

        return update
            .Set(document => document.TmdbId, metadata.TmdbId)
            .Set(document => document.ImdbId, metadata.ImdbId)
            .Set(document => document.TmdbTitle, metadata.TmdbTitle)
            .Set(document => document.OriginalTitle, metadata.OriginalTitle)
            .Set(document => document.Overview, metadata.Overview)
            .Set(document => document.ReleaseDate, metadata.ReleaseDate)
            .Set(document => document.Genres, metadata.Genres)
            .Set(document => document.RuntimeMinutes, metadata.RuntimeMinutes)
            .Set(document => document.OriginalLanguage, metadata.OriginalLanguage)
            .Set(document => document.TmdbVoteAverage, metadata.TmdbVoteAverage)
            .Set(document => document.TmdbVoteCount, metadata.TmdbVoteCount)
            .Set(document => document.PosterPath, metadata.PosterPath)
            .Set(document => document.BackdropPath, metadata.BackdropPath)
            .Set(document => document.PosterUrl, metadata.PosterUrl)
            .Set(document => document.BackdropUrl, metadata.BackdropUrl)
            .Set(document => document.WatchProviders, ToProviderDocuments(metadata.Providers))
            .Set(document => document.OwnedServiceAvailability, metadata.OwnedServiceAvailability)
            .Set(document => document.ReleasedOnVod, metadata.ReleasedOnVod)
            .Set(document => document.VodRegions, metadata.VodRegions)
            .Set(document => document.TmdbMetadataUpdatedAt, metadata.UpdatedAt)
            .Set(document => document.TmdbMetadataStatus, metadata.MetadataStatus)
            .Set(document => document.TmdbMetadataError, metadata.MetadataError);
    }

    private static IReadOnlyDictionary<string, MongoRegionWatchProvidersDocument> ToProviderDocuments(
        TmdbMovieProviderDataDto providers)
    {
        return providers.Regions.ToDictionary(
            region => region.Key,
            region => new MongoRegionWatchProvidersDocument
            {
                Flatrate = ToProviderDocuments(region.Value.Flatrate),
                Rent = ToProviderDocuments(region.Value.Rent),
                Buy = ToProviderDocuments(region.Value.Buy)
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MongoWatchProviderDocument> ToProviderDocuments(
        IReadOnlyList<TmdbWatchProviderDto> providers)
    {
        return providers
            .Select(provider => new MongoWatchProviderDocument
            {
                ProviderId = provider.ProviderId,
                ProviderName = provider.ProviderName,
                LogoPath = provider.LogoPath,
                DisplayPriority = provider.DisplayPriority
            })
            .ToList();
    }

    private static WatchlistItemWriteModel ToWriteModel(MongoWatchlistItemDocument document)
    {
        return new WatchlistItemWriteModel(
            document.ToDomain(),
            document.ImdbId,
            document.LetterboxdPath,
            document.TmdbId);
    }
}
