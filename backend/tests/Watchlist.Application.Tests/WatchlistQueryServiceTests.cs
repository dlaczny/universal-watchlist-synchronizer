using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class WatchlistQueryServiceTests
{
    private static readonly DateTimeOffset UpdatedAt = new(2026, 5, 25, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetItemsAsync_WhenMediaTypeIsMovie_ReturnsOnlyMovies()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("movie-1", MediaType.Movie, "Alien"),
            CreateItem("tv-1", MediaType.TvShow, "Severance")
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(
            MediaType.Movie,
            WatchlistFilter.All,
            CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("movie-1");
        result[0].MediaType.Should().Be("movie");
    }

    [Fact]
    public async Task GetItemsAsync_WhenFilterIsAvailable_ReturnsOnlyAvailableOnPlex()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("available-1", MediaType.Movie, "Alien", AvailabilityStatus.AvailableOnPlex),
            CreateItem("missing-1", MediaType.Movie, "Blade Runner", AvailabilityStatus.NotOnPlex),
            CreateItem("uncertain-1", MediaType.TvShow, "Twin Peaks", AvailabilityStatus.UnknownMatch)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(
            null,
            WatchlistFilter.Available,
            CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("available-1");
        result[0].AvailabilityStatus.Should().Be("available_on_plex");
    }

    [Fact]
    public async Task GetItemAsync_WhenItemExists_ReturnsItem()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("movie-1", MediaType.Movie, "Alien", AvailabilityStatus.AvailableOnPlex)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));

        WatchlistItemDto? result = await service.GetItemAsync("movie-1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("movie-1");
        result.MediaType.Should().Be("movie");
        result.Source.Should().Be("letterboxd");
        result.SourceId.Should().Be("source-movie-1");
        result.Title.Should().Be("Alien");
        result.Year.Should().Be(1979);
        result.Overview.Should().Be("A test overview.");
        result.PosterUrl.Should().Be("https://example.test/poster.jpg");
        result.BackdropUrl.Should().Be("https://example.test/backdrop.jpg");
        result.ReleaseStatus.Should().Be("released");
        result.AvailabilityStatus.Should().Be("available_on_plex");
        result.UpdatedAt.Should().Be(UpdatedAt);
    }

    private static WatchlistItem CreateItem(
        string id,
        MediaType mediaType,
        string title,
        AvailabilityStatus availabilityStatus = AvailabilityStatus.NotOnPlex)
    {
        return new WatchlistItem(
            id,
            mediaType,
            WatchlistSource.Letterboxd,
            $"source-{id}",
            title,
            1979,
            "A test overview.",
            "https://example.test/poster.jpg",
            "https://example.test/backdrop.jpg",
            ReleaseStatus.Released,
            availabilityStatus,
            UpdatedAt);
    }

    private sealed class StubWatchlistReadRepository(IReadOnlyList<WatchlistItem> items) : IWatchlistReadRepository
    {
        public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(items);
        }
    }
}
