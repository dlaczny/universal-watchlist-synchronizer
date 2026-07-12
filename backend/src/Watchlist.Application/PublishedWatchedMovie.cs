namespace Watchlist.Application;

public sealed record PublishedWatchedMovie(
    string SourceId,
    string LifecycleEventId,
    DateTimeOffset WatchedAt,
    long LifecycleVersion);
