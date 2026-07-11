using Watchlist.Domain;

namespace Watchlist.Application;

public sealed record WatchlistExportMovieModel(
    string SourceId,
    string? ImdbId,
    string Title,
    int? Year,
    string? LetterboxdPath,
    IReadOnlyList<string> OwnedServiceAvailability,
    int? TmdbId = null,
    string MetadataStatus = "not_synced",
    AvailabilityStatus AvailabilityStatus = Watchlist.Domain.AvailabilityStatus.Unspecified);
