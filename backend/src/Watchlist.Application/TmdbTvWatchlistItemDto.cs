namespace Watchlist.Application;

public sealed record TmdbTvWatchlistItemDto(
    int TmdbId,
    string Name,
    string OriginalName,
    string? Overview,
    string? FirstAirDate,
    string? PosterPath,
    string? BackdropPath,
    string? OriginalLanguage,
    double? TmdbVoteAverage,
    int? TmdbVoteCount);
