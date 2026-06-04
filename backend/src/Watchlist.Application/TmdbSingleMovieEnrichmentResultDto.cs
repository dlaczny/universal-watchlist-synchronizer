namespace Watchlist.Application;

public sealed record TmdbSingleMovieEnrichmentResultDto(
    string Status,
    string Id,
    int? TmdbId);
