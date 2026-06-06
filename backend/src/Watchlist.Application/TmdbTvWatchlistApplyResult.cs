namespace Watchlist.Application;

public sealed record TmdbTvWatchlistApplyResult(
    int ItemsUpserted,
    int ItemsDeleted);
