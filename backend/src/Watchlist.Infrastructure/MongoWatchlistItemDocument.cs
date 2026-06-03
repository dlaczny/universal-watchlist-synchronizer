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

    public string? Overview { get; init; }

    public string? PosterUrl { get; init; }

    public string? BackdropUrl { get; init; }

    public ReleaseStatus ReleaseStatus { get; init; }

    public AvailabilityStatus AvailabilityStatus { get; init; }

    public DateTimeOffset? AddedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

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
            UpdatedAt);
    }

    public static MongoWatchlistItemDocument FromDomain(WatchlistItem item)
    {
        return new MongoWatchlistItemDocument
        {
            Id = item.Id,
            MediaType = item.MediaType,
            Source = item.Source,
            SourceId = item.SourceId,
            Title = item.Title,
            Year = item.Year,
            Overview = item.Overview,
            PosterUrl = item.PosterUrl,
            BackdropUrl = item.BackdropUrl,
            ReleaseStatus = item.ReleaseStatus,
            AvailabilityStatus = item.AvailabilityStatus,
            AddedAt = item.AddedAt,
            UpdatedAt = item.UpdatedAt
        };
    }
}
