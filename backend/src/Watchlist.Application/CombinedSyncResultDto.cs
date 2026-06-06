namespace Watchlist.Application;

public sealed record CombinedSyncResultDto(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    LetterboxdSyncResultDto Letterboxd,
    TmdbMovieEnrichmentResultDto TmdbMovies,
    TmdbTvSyncResultDto TmdbTv,
    PlexMovieSyncResultDto PlexMovies);
