using MongoDB.Bson.Serialization.Attributes;

namespace Watchlist.Infrastructure;

public sealed class MongoLetterboxdSourceSnapshotDocument
{
    [BsonId]
    public string Id { get; init; } = string.Empty;

    public DateTimeOffset PublishedAt { get; init; }

    public IReadOnlyList<string> SourceIds { get; init; } = [];

    public IReadOnlyList<MongoPublishedWatchedMovieDocument> WatchedMovies { get; init; } = [];

    public int ItemCount { get; init; }
}
