namespace Watchlist.Application;

/// <summary>
/// Read model returned by the application query service.
/// </summary>
public sealed record WatchlistItemDto(
    string Id,
    string MediaType,
    string Source,
    string SourceId,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    string ReleaseStatus,
    string AvailabilityStatus,
    bool VodReleaseKnown,
    bool ReleasedOnVod,
    IReadOnlyList<string> VodRegions,
    IReadOnlyList<string> OwnedServiceAvailability,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt);
