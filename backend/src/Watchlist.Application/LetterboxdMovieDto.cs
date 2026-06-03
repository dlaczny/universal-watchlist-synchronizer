namespace Watchlist.Application;

public sealed record LetterboxdMovieDto(
    string SourceId,
    string? ImdbId,
    string Title,
    int? ReleaseYear,
    string? LetterboxdPath);
