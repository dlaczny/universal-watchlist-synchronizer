using FluentAssertions;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoWatchlistItemDocumentTests
{
    [Fact]
    public void ToDomain_WhenDocumentIsComplete_MapsAllFields()
    {
        DateTimeOffset addedAt = DateTimeOffset.Parse("2026-05-20T10:00:00+02:00");
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
            AddedAt = addedAt,
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
            addedAt,
            updatedAt));
    }

    [Fact]
    public void ToDomain_WhenDocumentHasAddedAt_MapsAddedAtAndUpdatedAt()
    {
        DateTimeOffset addedAt = DateTimeOffset.Parse("2026-05-20T10:00:00+02:00");
        DateTimeOffset updatedAt = DateTimeOffset.Parse("2026-05-25T10:00:00+02:00");
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
            AddedAt = addedAt,
            UpdatedAt = updatedAt
        };

        WatchlistItem item = document.ToDomain();

        item.AddedAt.Should().Be(addedAt);
        item.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void ToDomain_WhenDocumentHasNoAddedAt_UsesUpdatedAtForLocalCompatibility()
    {
        DateTimeOffset updatedAt = DateTimeOffset.Parse("2026-05-25T10:00:00+02:00");
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

        item.AddedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void ToDomain_WhenDocumentHasTmdbMetadata_MapsDisplayFieldsFromDocument()
    {
        DateTimeOffset updatedAt = DateTimeOffset.Parse("2026-06-04T10:00:00+02:00");
        MongoWatchlistItemDocument document = new()
        {
            Id = "movie-example",
            MediaType = MediaType.Movie,
            Source = WatchlistSource.Letterboxd,
            SourceId = "letterboxd-example",
            Title = "Display Title",
            Year = 2026,
            Overview = "Display overview",
            PosterUrl = "https://example.com/display-poster.jpg",
            BackdropUrl = "https://example.com/display-backdrop.jpg",
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.AvailableOnPlex,
            UpdatedAt = updatedAt,
            TmdbId = 1297842,
            TmdbTitle = "TMDB Title",
            OriginalTitle = "Original Title",
            ReleaseDate = "2026-06-04",
            Genres = ["Drama"],
            RuntimeMinutes = 93,
            OriginalLanguage = "en",
            TmdbVoteAverage = 7.7,
            TmdbVoteCount = 1200,
            PosterPath = "/tmdb-poster.jpg",
            BackdropPath = "/tmdb-backdrop.jpg",
            ReleasedOnVod = true,
            VodRegions = ["PL", "US"],
            OwnedServiceAvailability = ["Amazon Prime Video"],
            TmdbMetadataStatus = "enriched"
        };

        WatchlistItem item = document.ToDomain();

        item.Overview.Should().Be("Display overview");
        item.PosterUrl.Should().Be("https://example.com/display-poster.jpg");
        item.BackdropUrl.Should().Be("https://example.com/display-backdrop.jpg");
        item.VodReleaseKnown.Should().BeTrue();
        item.ReleasedOnVod.Should().BeTrue();
        item.VodRegions.Should().Equal("PL", "US");
        item.OwnedServiceAvailability.Should().Equal("Amazon Prime Video");
        item.Genres.Should().Equal("Drama");
        item.RuntimeMinutes.Should().Be(93);
        item.OriginalLanguage.Should().Be("en");
        item.TmdbVoteAverage.Should().Be(7.7);
        item.TmdbVoteCount.Should().Be(1200);
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

    [Fact]
    public void FromDomain_WhenDocumentHasSourceTraceFields_PreservesThemOnDocument()
    {
        WatchlistItem item = new(
            "movie-letterboxd-1418998",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            "1418998",
            "Karma",
            2026,
            null,
            null,
            null,
            ReleaseStatus.Unreleased,
            AvailabilityStatus.Unreleased,
            DateTimeOffset.Parse("2026-06-03T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-03T12:00:00Z"));

        MongoWatchlistItemDocument document = MongoWatchlistItemDocument.FromDomain(
            item,
            "tt35450621",
            "/film/karma-2026/");

        document.ImdbId.Should().Be("tt35450621");
        document.LetterboxdPath.Should().Be("/film/karma-2026/");
    }
}
