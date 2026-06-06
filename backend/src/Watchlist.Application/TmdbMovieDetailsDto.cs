namespace Watchlist.Application;

public sealed record TmdbMovieDetailsDto(
    int TmdbId,
    string? ImdbId,
    string Title,
    string OriginalTitle,
    string? Overview,
    string? ReleaseDate,
    string? PosterPath,
    string? BackdropPath,
    string? PosterUrl,
    string? BackdropUrl,
    IReadOnlyList<string> Genres,
    int? RuntimeMinutes,
    string? OriginalLanguage,
    double? TmdbVoteAverage,
    int? TmdbVoteCount);
