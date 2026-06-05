namespace Watchlist.Application;

public sealed record PlexMovieDto(
    string RatingKey,
    string Title,
    int? Year,
    string LibrarySectionKey,
    string LibrarySectionTitle,
    string? PlexGuid,
    string? ImdbId,
    int? TmdbId,
    int? TvdbId);
