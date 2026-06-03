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
    DateTimeOffset UpdatedAt);
