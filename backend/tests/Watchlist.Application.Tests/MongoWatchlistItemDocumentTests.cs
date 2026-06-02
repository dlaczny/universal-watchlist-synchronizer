using FluentAssertions;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoWatchlistItemDocumentTests
{
    [Fact]
    public void ToDomain_WhenDocumentIsComplete_MapsAllFields()
    {
        DateTimeOffset updatedAt = DateTimeOffset.Parse("2026-06-02T10:00:00+02:00");
        MongoWatchlistItemDocument document = new()
        {
            Id = "movie-example",
            MediaType = MediaType.Movie,
            Source = WatchlistSource.Letterboxd,
            SourceId = "letterboxd-example",
            Title = "Example Movie",
            Year = 2025,
            Overview = "Overview",
            PosterUrl = "https://example.com/poster.jpg",
            BackdropUrl = "https://example.com/backdrop.jpg",
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.AvailableOnPlex,
            UpdatedAt = updatedAt
        };

        WatchlistItem item = document.ToDomain();

        item.Should().Be(new WatchlistItem(
            "movie-example",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            "letterboxd-example",
            "Example Movie",
            2025,
            "Overview",
            "https://example.com/poster.jpg",
            "https://example.com/backdrop.jpg",
            ReleaseStatus.Released,
            AvailabilityStatus.AvailableOnPlex,
            updatedAt));
    }

    [Fact]
    public void ToDomain_WhenEnumIsUnspecified_Throws()
    {
        MongoWatchlistItemDocument document = new()
        {
            Id = "movie-example",
            MediaType = MediaType.Unspecified,
            Source = WatchlistSource.Letterboxd,
            SourceId = "letterboxd-example",
            Title = "Example Movie",
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.NotOnPlex,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        Action action = () => document.ToDomain();

        action.Should().Throw<InvalidOperationException>();
    }
}
