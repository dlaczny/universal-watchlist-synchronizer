namespace Watchlist.Application;

public sealed record WatchlistExportMovieModel(
    string SourceId,
    string? ImdbId,
    string Title,
    int? Year,
    string? LetterboxdPath,
    IReadOnlyList<string> OwnedServiceAvailability);
