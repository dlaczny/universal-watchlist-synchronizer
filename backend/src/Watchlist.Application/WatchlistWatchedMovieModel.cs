namespace Watchlist.Application;

public sealed record WatchlistWatchedMovieModel(
    int? TmdbId,
    string? ImdbId,
    string Title,
    int? Year,
    string SourceId,
    DateTimeOffset WatchedAt,
    long LifecycleVersion,
    string LifecycleEventId);
