namespace Watchlist.Application;

public sealed record WatchlistMovieLifecycleExport(
    LetterboxdSourceSnapshot? SourceSnapshot,
    IReadOnlyList<WatchlistExportMovieModel> ActiveMovies,
    IReadOnlyList<WatchlistWatchedMovieModel> WatchedMovies);
