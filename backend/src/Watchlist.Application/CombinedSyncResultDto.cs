namespace Watchlist.Application;

public sealed record CombinedSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    LetterboxdSyncResultDto Letterboxd,
    TmdbMovieEnrichmentResultDto TmdbMovies,
    PlexMovieSyncResultDto PlexMovies);
