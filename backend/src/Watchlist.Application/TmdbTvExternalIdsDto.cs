namespace Watchlist.Application;

public sealed record TmdbTvExternalIdsDto(
    string? ImdbId,
    int? TvdbId);
