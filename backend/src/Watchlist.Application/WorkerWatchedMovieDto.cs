namespace Watchlist.Application;

public sealed record WorkerWatchedMovieDto(
    int? TmdbId,
    string? ImdbId,
    string Title,
    int? Year,
    string SourceId,
    DateTimeOffset WatchedAt,
    long LifecycleVersion,
    string LifecycleEventId);
