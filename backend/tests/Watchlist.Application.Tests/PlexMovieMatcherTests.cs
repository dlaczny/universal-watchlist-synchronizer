using FluentAssertions;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class PlexMovieMatcherTests
{
    [Fact]
    public void Match_WhenImdbIdMatches_ReturnsAvailableByImdb()
    {
        PlexMatchResult result = PlexMovieMatcher.Match(
            CreateWatchlistMovie("GOAT", 2026, "tt27613895", 1297842),
            [CreatePlexMovie("10", "Different", 2020, "tt27613895", null)]);

        result.Should().Be(new PlexMatchResult(
            AvailabilityStatus.AvailableOnPlex,
            "10",
            "imdb",
            "exact"));
    }

    [Fact]
    public void Match_WhenTmdbIdMatches_ReturnsAvailableByTmdb()
    {
        PlexMatchResult result = PlexMovieMatcher.Match(
            CreateWatchlistMovie("GOAT", 2026, null, 1297842),
            [CreatePlexMovie("10", "Different", 2020, null, 1297842)]);

        result.MatchReason.Should().Be("tmdb");
        result.AvailabilityStatus.Should().Be(AvailabilityStatus.AvailableOnPlex);
    }

    [Fact]
    public void Match_WhenTitleAndYearUnique_ReturnsAvailableByTitleYear()
    {
        PlexMatchResult result = PlexMovieMatcher.Match(
            CreateWatchlistMovie("A Scanner Darkly", 2006, null, null),
            [CreatePlexMovie("22", "A Scanner, Darkly", 2006, null, null)]);

        result.Should().Be(new PlexMatchResult(
            AvailabilityStatus.AvailableOnPlex,
            "22",
            "title_year",
            "exact"));
    }

    [Fact]
    public void Match_WhenTitleAndYearAmbiguous_ReturnsUnknownMatch()
    {
        PlexMatchResult result = PlexMovieMatcher.Match(
            CreateWatchlistMovie("Possession", 1981, null, null),
            [
                CreatePlexMovie("1", "Possession", 1981, null, null),
                CreatePlexMovie("2", "Possession", 1981, null, null)
            ]);

        result.Should().Be(new PlexMatchResult(
            AvailabilityStatus.UnknownMatch,
            null,
            "ambiguous",
            "ambiguous"));
    }

    [Fact]
    public void Match_WhenReleasedAndNoMatch_ReturnsNotOnPlex()
    {
        PlexMatchResult result = PlexMovieMatcher.Match(
            CreateWatchlistMovie("Missing", 2020, null, null, ReleaseStatus.Released),
            []);

        result.AvailabilityStatus.Should().Be(AvailabilityStatus.NotOnPlex);
        result.MatchReason.Should().Be("none");
    }

    [Fact]
    public void Match_WhenUnreleasedAndNoMatch_ReturnsUnreleased()
    {
        PlexMatchResult result = PlexMovieMatcher.Match(
            CreateWatchlistMovie("Future", 2027, null, null, ReleaseStatus.Unreleased),
            []);

        result.AvailabilityStatus.Should().Be(AvailabilityStatus.Unreleased);
    }

    [Fact]
    public void Match_WhenUnreleasedButPlexMatchExists_ReturnsAvailable()
    {
        PlexMatchResult result = PlexMovieMatcher.Match(
            CreateWatchlistMovie("Future", 2027, "tt1", null, ReleaseStatus.Unreleased),
            [CreatePlexMovie("99", "Future", 2027, "tt1", null)]);

        result.AvailabilityStatus.Should().Be(AvailabilityStatus.AvailableOnPlex);
    }

    private static WatchlistItemWriteModel CreateWatchlistMovie(
        string title,
        int? year,
        string? imdbId,
        int? tmdbId,
        ReleaseStatus releaseStatus = ReleaseStatus.Released)
    {
        WatchlistItem item = new(
            $"movie-letterboxd-{title}",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            "source",
            title,
            year,
            null,
            null,
            null,
            releaseStatus,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-05T12:00:00Z"));

        return new WatchlistItemWriteModel(item, imdbId, null, tmdbId);
    }

    private static PlexMovieDto CreatePlexMovie(
        string ratingKey,
        string title,
        int? year,
        string? imdbId,
        int? tmdbId)
    {
        return new PlexMovieDto(ratingKey, title, year, "1", "Filmy", "plex://movie/local", imdbId, tmdbId, null);
    }
}
