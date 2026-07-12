namespace Watchlist.Application;

public sealed record WorkerMovieSnapshotDto(
    string SourceSnapshotId,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? LastSuccessfulMovieSyncAt,
    IReadOnlyList<WorkerMovieDto> Movies,
    IReadOnlyList<WorkerWatchedMovieDto> WatchedMovies);
