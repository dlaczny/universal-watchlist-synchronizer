namespace Watchlist.Application;

public sealed record LetterboxdSourceSnapshot(
    string SnapshotId,
    DateTimeOffset PublishedAt,
    IReadOnlySet<string> SourceIds,
    IReadOnlyList<PublishedWatchedMovie> WatchedMovies);
