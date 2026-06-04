namespace Watchlist.Application;

public sealed record TmdbMovieEnrichmentResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int ItemsMatched,
    int ItemsEnriched,
    int ItemsNotFound,
    int ItemsFailed);
