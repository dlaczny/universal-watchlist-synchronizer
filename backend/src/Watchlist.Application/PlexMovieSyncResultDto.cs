namespace Watchlist.Application;

public sealed record PlexMovieSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int SectionsScanned,
    int ItemsFetched,
    int ItemsUpserted,
    int ItemsDeleted,
    int WatchlistItemsMatched,
    int WatchlistItemsNotMatched,
    int WatchlistItemsUnknown);
