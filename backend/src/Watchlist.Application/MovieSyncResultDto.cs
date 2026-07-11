namespace Watchlist.Application;

public sealed record MovieSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    LetterboxdSyncResultDto Letterboxd,
    TmdbMovieEnrichmentResultDto TmdbMovies,
    PlexMovieSyncResultDto PlexMovies);
