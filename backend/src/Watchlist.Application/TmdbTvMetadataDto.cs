namespace Watchlist.Application;

public sealed record TmdbTvMetadataDto(
    int TmdbId,
    string Name,
    string OriginalName,
    string? Overview,
    string? FirstAirDate,
    string? Status,
    string? PosterPath,
    string? BackdropPath,
    string? PosterUrl,
    string? BackdropUrl,
    IReadOnlyList<string> Genres,
    string? OriginalLanguage,
    double? TmdbVoteAverage,
    int? TmdbVoteCount,
    TmdbTvExternalIdsDto ExternalIds);
