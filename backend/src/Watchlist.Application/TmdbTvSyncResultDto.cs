namespace Watchlist.Application;

public sealed record TmdbTvSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ItemsFetched,
    int ItemsUpserted,
    int ItemsDeleted,
    int ItemsEnriched,
    int ItemsNotFound,
    int ItemsFailed);
