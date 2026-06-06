using FluentAssertions;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoWatchlistExportRepositoryTests
{
    [Fact]
    public void ToExportModel_MapsRadarrExportFieldsFromMongoDocument()
    {
        MongoWatchlistItemDocument document = new()
        {
            Id = "movie-letterboxd-1297842",
            MediaType = MediaType.Movie,
            Source = WatchlistSource.Letterboxd,
            SourceId = "1297842",
            ImdbId = "tt27613895",
            Title = "GOAT",
            Year = 2026,
            LetterboxdPath = "/film/goat-2026/",
            OwnedServiceAvailability = ["Amazon Prime Video"],
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.NotOnPlex,
            UpdatedAt = DateTimeOffset.Parse("2026-06-05T12:00:00Z")
        };

        WatchlistExportMovieModel result = MongoWatchlistExportRepository.ToExportModel(document);

        result.SourceId.Should().Be("1297842");
        result.ImdbId.Should().Be("tt27613895");
        result.Title.Should().Be("GOAT");
        result.Year.Should().Be(2026);
        result.LetterboxdPath.Should().Be("/film/goat-2026/");
        result.OwnedServiceAvailability.Should().Equal("Amazon Prime Video");
    }
}
