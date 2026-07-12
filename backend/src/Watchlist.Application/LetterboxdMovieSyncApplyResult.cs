namespace Watchlist.Application;

public sealed record LetterboxdMovieSyncApplyResult(
    string SourceSnapshotId,
    int ItemsMarkedWatched);
