using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Application.Tests;

public sealed class WatchlistExportServiceTests
{
    [Fact]
    public async Task GetRadarrMoviesAsync_WhenMovieHasNoOwnedVodAvailability_IncludesMovie()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository(
        [
            new WatchlistExportMovieModel(
                "1297842",
                "tt27613895",
                "GOAT",
                2026,
                "/film/goat-2026/",
                [])
        ]));

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = 1297842,
            ImdbId = "tt27613895",
            Title = "GOAT",
            ReleaseYear = "2026",
            CleanTitle = "/film/goat-2026/",
            Adult = false
        });
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenMovieHasOwnedVodAvailability_ExcludesMovie()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository(
        [
            new WatchlistExportMovieModel(
                "4951",
                "tt0147800",
                "10 Things I Hate About You",
                1999,
                "/film/10-things-i-hate-about-you/",
                ["Amazon Prime Video"])
        ]));

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenTmdbEnrichmentMissing_IncludesMovie()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository(
        [
            new WatchlistExportMovieModel(
                "1418998",
                "tt35450621",
                "Karma",
                2026,
                "/film/karma-2026/",
                [])
        ]));

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Select(item => item.Title).Should().Equal("Karma");
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenSourceIdCannotBeParsed_SkipsMovie()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository(
        [
            new WatchlistExportMovieModel(
                "bad-source",
                "tt0000001",
                "Malformed",
                2026,
                "/film/malformed/",
                [])
        ]));

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRadarrMoviesAsync_WhenOptionalFieldsMissing_UsesEmptyStrings()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository(
        [
            new WatchlistExportMovieModel(
                "1635594",
                null,
                "Ti Amo!",
                null,
                null,
                [])
        ]));

        IReadOnlyList<RadarrMovieExportItemDto> result = await service.GetRadarrMoviesAsync(CancellationToken.None);

        RadarrMovieExportItemDto item = result.Should().ContainSingle().Subject;
        item.ImdbId.Should().BeEmpty();
        item.ReleaseYear.Should().BeEmpty();
        item.CleanTitle.Should().BeEmpty();
    }

    [Fact]
    public Task GetSonarrTvAsync_ForV1_ReturnsEmptyList()
    {
        WatchlistExportService service = new(new StubWatchlistExportRepository([]));

        return service.GetSonarrTvAsync(CancellationToken.None)
            .ContinueWith(task => task.Result.Should().BeEmpty(), CancellationToken.None);
    }

    private sealed class StubWatchlistExportRepository(
        IReadOnlyList<WatchlistExportMovieModel> movies) : IWatchlistExportRepository
    {
        public Task<IReadOnlyList<WatchlistExportMovieModel>> GetLetterboxdMoviesAsync(
            CancellationToken cancellationToken)
        {
            return Task.FromResult(movies);
        }
    }
}
