namespace Watchlist.Domain;

public sealed record WatchlistItem(
    string Id,
    MediaType MediaType,
    WatchlistSource Source,
    string SourceId,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    ReleaseStatus ReleaseStatus,
    AvailabilityStatus AvailabilityStatus,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt)
{
    public bool VodReleaseKnown { get; init; }

    public bool ReleasedOnVod { get; init; }

    public IReadOnlyList<string> VodRegions { get; init; } = [];

    public IReadOnlyList<string> OwnedServiceAvailability { get; init; } = [];
}
