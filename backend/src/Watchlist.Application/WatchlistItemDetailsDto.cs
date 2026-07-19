using System.Text.Json.Serialization;

namespace Watchlist.Application;

public sealed record WatchlistItemDetailsDto(
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
    string LibraryMembership,
    bool VodReleaseKnown,
    bool ReleasedOnVod,
    IReadOnlyList<string> VodRegions,
    IReadOnlyList<string> OwnedServiceAvailability,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Genres,
    int? RuntimeMinutes,
    string? OriginalLanguage,
    double? TmdbVoteAverage,
    int? TmdbVoteCount,
    string PrimaryActionLabel,
    bool PrimaryActionEnabled,
    string? PrimaryActionTarget)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TvDetailsDto? Tv { get; init; }
}
