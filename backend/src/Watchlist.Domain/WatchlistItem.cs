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

    public IReadOnlyList<string> Genres { get; init; } = [];

    public int? RuntimeMinutes { get; init; }

    public string? OriginalLanguage { get; init; }

    public double? TmdbVoteAverage { get; init; }

    public int? TmdbVoteCount { get; init; }
}
