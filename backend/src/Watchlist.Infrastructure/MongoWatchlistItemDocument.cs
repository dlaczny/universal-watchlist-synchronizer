using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoWatchlistItemDocument
{
    [BsonId]
    public string Id { get; init; } = string.Empty;

    public MediaType MediaType { get; init; }

    public WatchlistSource Source { get; init; }

    public string SourceId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public int? Year { get; init; }

    public string? ImdbId { get; init; }

    public string? LetterboxdPath { get; init; }

    public string? Overview { get; init; }

    public string? PosterUrl { get; init; }

    public string? BackdropUrl { get; init; }

    public int? TmdbId { get; init; }

    public string? TmdbTitle { get; init; }

    public string? OriginalTitle { get; init; }

    public string? ReleaseDate { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public string? PosterPath { get; init; }

    public string? BackdropPath { get; init; }

    public IReadOnlyDictionary<string, MongoRegionWatchProvidersDocument> WatchProviders { get; init; }
        = new Dictionary<string, MongoRegionWatchProvidersDocument>();

    public IReadOnlyList<string> OwnedServiceAvailability { get; init; } = [];

    public bool ReleasedOnVod { get; init; }

    public IReadOnlyList<string> VodRegions { get; init; } = [];

    public DateTimeOffset? TmdbMetadataUpdatedAt { get; init; }

    public string TmdbMetadataStatus { get; init; } = "not_synced";

    public string? TmdbMetadataError { get; init; }

    public ReleaseStatus ReleaseStatus { get; init; }

    public AvailabilityStatus AvailabilityStatus { get; init; }

    public DateTimeOffset? AddedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string? PlexRatingKey { get; init; }

    public DateTimeOffset? PlexMatchedAt { get; init; }

    public string PlexMatchReason { get; init; } = "none";

    public string PlexMatchConfidence { get; init; } = "none";

    public WatchlistItem ToDomain()
    {
        if (MediaType == MediaType.Unspecified
            || Source == WatchlistSource.Unspecified
            || AvailabilityStatus == AvailabilityStatus.Unspecified)
        {
            throw new InvalidOperationException("MongoDB watchlist document contains an unspecified enum value.");
        }

        return new WatchlistItem(
            Id,
            MediaType,
            Source,
            SourceId,
            Title,
            Year,
            Overview,
            PosterUrl,
            BackdropUrl,
            ReleaseStatus,
            AvailabilityStatus,
            AddedAt ?? UpdatedAt,
            UpdatedAt)
        {
            VodReleaseKnown = string.Equals(TmdbMetadataStatus, "enriched", StringComparison.Ordinal),
            ReleasedOnVod = ReleasedOnVod,
            VodRegions = VodRegions,
            OwnedServiceAvailability = OwnedServiceAvailability
        };
    }

    public static MongoWatchlistItemDocument FromDomain(
        WatchlistItem item,
        string? imdbId = null,
        string? letterboxdPath = null)
    {
        return new MongoWatchlistItemDocument
        {
            Id = item.Id,
            MediaType = item.MediaType,
            Source = item.Source,
            SourceId = item.SourceId,
            Title = item.Title,
            Year = item.Year,
            ImdbId = imdbId,
            LetterboxdPath = letterboxdPath,
            Overview = item.Overview,
            PosterUrl = item.PosterUrl,
            BackdropUrl = item.BackdropUrl,
            ReleasedOnVod = item.ReleasedOnVod,
            VodRegions = item.VodRegions,
            OwnedServiceAvailability = item.OwnedServiceAvailability,
            ReleaseStatus = item.ReleaseStatus,
            AvailabilityStatus = item.AvailabilityStatus,
            AddedAt = item.AddedAt,
            UpdatedAt = item.UpdatedAt
        };
    }
}
