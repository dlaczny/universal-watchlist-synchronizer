namespace Watchlist.Application;

public sealed record WorkerMovieDto(
    int? TmdbId,
    string? ImdbId,
    string Title,
    int? Year,
    string SourceId,
    string MetadataStatus,
    string AvailabilityStatus,
    IReadOnlyList<string> OwnedServiceAvailability,
    bool RadarrEligible,
    string RadarrEligibilityReason);
