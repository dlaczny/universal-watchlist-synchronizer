namespace Watchlist.Infrastructure;

public sealed class MongoPublishedWatchedMovieDocument
{
    public string SourceId { get; init; } = string.Empty;

    public string LifecycleEventId { get; init; } = string.Empty;

    public DateTimeOffset WatchedAt { get; init; }

    public long LifecycleVersion { get; init; }
}
