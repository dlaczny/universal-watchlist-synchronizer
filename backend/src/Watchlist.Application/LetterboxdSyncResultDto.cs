namespace Watchlist.Application;

/// <summary>
/// Result returned after a Letterboxd movie sync run.
/// </summary>
public sealed record LetterboxdSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ItemsFetched,
    int ItemsUpserted,
    int ItemsDeleted);
