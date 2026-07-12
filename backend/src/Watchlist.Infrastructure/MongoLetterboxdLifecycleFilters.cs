using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

internal static class MongoLetterboxdLifecycleFilters
{
    public static FilterDefinition<MongoWatchlistItemDocument> LetterboxdMovies()
    {
        FilterDefinitionBuilder<MongoWatchlistItemDocument> filter =
            Builders<MongoWatchlistItemDocument>.Filter;
        return filter.Eq(document => document.MediaType, MediaType.Movie)
            & filter.Eq(document => document.Source, WatchlistSource.Letterboxd);
    }

    public static FilterDefinition<MongoWatchlistItemDocument> ActiveLetterboxdMovies(
        LetterboxdSourceSnapshot? snapshot)
    {
        FilterDefinitionBuilder<MongoWatchlistItemDocument> filter =
            Builders<MongoWatchlistItemDocument>.Filter;
        FilterDefinition<MongoWatchlistItemDocument> letterboxdMovies = LetterboxdMovies();
        return snapshot is null
            ? letterboxdMovies
            : letterboxdMovies & filter.In(
                document => document.SourceId,
                snapshot.SourceIds);
    }

    public static FilterDefinition<MongoWatchlistItemDocument> VisibleWatchlistItems(
        LetterboxdSourceSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return FilterDefinition<MongoWatchlistItemDocument>.Empty;
        }

        FilterDefinitionBuilder<MongoWatchlistItemDocument> filter =
            Builders<MongoWatchlistItemDocument>.Filter;
        FilterDefinition<MongoWatchlistItemDocument> letterboxdMovies = LetterboxdMovies();
        return filter.Not(letterboxdMovies) | ActiveLetterboxdMovies(snapshot);
    }
}
