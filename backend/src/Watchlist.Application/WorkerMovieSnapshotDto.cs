namespace Watchlist.Application;

public sealed record WorkerMovieSnapshotDto(
    DateTimeOffset GeneratedAt,
    DateTimeOffset? LastSuccessfulMovieSyncAt,
    IReadOnlyList<WorkerMovieDto> Movies);
