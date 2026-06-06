using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class WatchlistQueryServiceTests
{
    private static readonly DateTimeOffset AddedAt = new(2026, 5, 20, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 5, 25, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetItemsAsync_WhenCollectionAll_ReturnsMoviesAndTv()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("old-movie", MediaType.Movie, "Dune: Part Two", DateTimeOffset.Parse("2026-05-20T10:00:00+02:00")),
            CreateItem("new-tv", MediaType.TvShow, "Andor", DateTimeOffset.Parse("2026-05-22T10:00:00+02:00"))
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
        WatchlistQuery query = new(
            WatchlistCollection.All,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex, AvailabilityStatus.NotOnPlex },
            WatchlistSort.AddedDescending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Select(item => item.Id).Should().Equal("new-tv", "old-movie");
    }

    [Fact]
    public async Task GetItemsAsync_WhenCollectionMovie_ReturnsOnlyMovies()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("movie-1", MediaType.Movie, "Alien", AvailabilityStatus.AvailableOnPlex),
            CreateItem("tv-1", MediaType.TvShow, "Severance", AvailabilityStatus.AvailableOnPlex)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
        WatchlistQuery query = new(
            WatchlistCollection.Movie,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex },
            WatchlistSort.AddedDescending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("movie-1");
        result[0].MediaType.Should().Be("movie");
    }

    [Fact]
    public async Task GetItemsAsync_WhenCollectionTv_ReturnsOnlyTvShows()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("movie-1", MediaType.Movie, "Alien", AvailabilityStatus.AvailableOnPlex),
            CreateItem("tv-1", MediaType.TvShow, "Severance", AvailabilityStatus.AvailableOnPlex)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
        WatchlistQuery query = new(
            WatchlistCollection.Tv,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex },
            WatchlistSort.AddedDescending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Id.Should().Be("tv-1");
        result[0].MediaType.Should().Be("tv");
    }

    [Fact]
    public async Task GetItemsAsync_WhenAvailabilityHasSplitReasons_ReturnsOnlyRequestedStates()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("available", MediaType.Movie, "Available", DateTimeOffset.Parse("2026-05-20T10:00:00+02:00"), AvailabilityStatus.AvailableOnPlex),
            CreateItem("missing", MediaType.Movie, "Missing", DateTimeOffset.Parse("2026-05-21T10:00:00+02:00"), AvailabilityStatus.NotOnPlex),
            CreateItem("uncertain", MediaType.TvShow, "Uncertain", DateTimeOffset.Parse("2026-05-22T10:00:00+02:00"), AvailabilityStatus.UnknownMatch)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
        WatchlistQuery query = new(
            WatchlistCollection.All,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex, AvailabilityStatus.UnknownMatch },
            WatchlistSort.AddedDescending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Select(item => item.AvailabilityStatus)
            .Should()
            .OnlyContain(status => status == "available_on_plex" || status == "unknown_match");
    }

    [Fact]
    public async Task GetItemsAsync_WhenSortAddedDescending_PreservesRepositoryOrderForEqualAddedAt()
    {
        DateTimeOffset latestAddedAt = DateTimeOffset.Parse("2026-05-22T10:00:00+02:00");
        DateTimeOffset olderAddedAt = DateTimeOffset.Parse("2026-05-20T10:00:00+02:00");
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("tie-first", MediaType.Movie, "First", latestAddedAt, AvailabilityStatus.AvailableOnPlex),
            CreateItem("older", MediaType.Movie, "Older", olderAddedAt, AvailabilityStatus.AvailableOnPlex),
            CreateItem("tie-second", MediaType.TvShow, "Second", latestAddedAt, AvailabilityStatus.AvailableOnPlex)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
        WatchlistQuery query = new(
            WatchlistCollection.All,
            new HashSet<AvailabilityStatus> { AvailabilityStatus.AvailableOnPlex },
            WatchlistSort.AddedDescending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Select(item => item.Id).Should().Equal("tie-first", "tie-second", "older");
    }

    [Fact]
    public async Task GetItemsAsync_WhenSortTitleAscending_SortsCaseInsensitively()
    {
        IReadOnlyList<WatchlistItem> items =
        [
            CreateItem("future", MediaType.Movie, "future Movie", DateTimeOffset.Parse("2026-05-21T10:00:00+02:00"), AvailabilityStatus.Unreleased),
            CreateItem("dune", MediaType.Movie, "Dune: Part Two", DateTimeOffset.Parse("2026-05-20T10:00:00+02:00"), AvailabilityStatus.AvailableOnPlex),
            CreateItem("andor", MediaType.TvShow, "Andor", DateTimeOffset.Parse("2026-05-22T10:00:00+02:00"), AvailabilityStatus.NotOnPlex)
        ];
        WatchlistQueryService service = new(new StubWatchlistReadRepository(items));
        WatchlistQuery query = new(
            WatchlistCollection.All,
            new HashSet<AvailabilityStatus>
            {
                AvailabilityStatus.AvailableOnPlex,
                AvailabilityStatus.NotOnPlex,
                AvailabilityStatus.Unreleased,
                AvailabilityStatus.UnknownMatch
            },
            WatchlistSort.TitleAscending);

        IReadOnlyList<WatchlistItemDto> result = await service.GetItemsAsync(query, CancellationToken.None);

        result.Select(item => item.Title).Should().Equal("Andor", "Dune: Part Two", "future Movie");
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
        result.VodReleaseKnown.Should().BeTrue();
        result.ReleasedOnVod.Should().BeTrue();
        result.VodRegions.Should().Equal("PL", "US");
        result.OwnedServiceAvailability.Should().Equal("Amazon Prime Video", "Max");
        result.AddedAt.Should().Be(AddedAt);
        result.UpdatedAt.Should().Be(UpdatedAt);
    }

    private static WatchlistItem CreateItem(
        string id,
        MediaType mediaType,
        string title,
        AvailabilityStatus availabilityStatus = AvailabilityStatus.NotOnPlex)
    {
        return CreateItem(id, mediaType, title, AddedAt, availabilityStatus);
    }

    private static WatchlistItem CreateItem(
        string id,
        MediaType mediaType,
        string title,
        DateTimeOffset addedAt,
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
            addedAt,
            UpdatedAt)
        {
            VodReleaseKnown = true,
            ReleasedOnVod = true,
            VodRegions = ["PL", "US"],
            OwnedServiceAvailability = ["Amazon Prime Video", "Max"]
        };
    }

    private sealed class StubWatchlistReadRepository(IReadOnlyList<WatchlistItem> items) : IWatchlistReadRepository
    {
        public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(items);
        }
    }
}
