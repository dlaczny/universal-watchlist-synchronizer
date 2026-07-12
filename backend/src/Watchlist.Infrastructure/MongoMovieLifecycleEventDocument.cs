namespace Watchlist.Infrastructure;

public sealed class MongoMovieLifecycleEventDocument
{
    public string EventId { get; init; } = string.Empty;

    public string EventType { get; init; } = string.Empty;

    public string SourceSnapshotId { get; init; } = string.Empty;

    public long LifecycleVersion { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}
