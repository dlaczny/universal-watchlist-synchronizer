using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class LetterboxdMovieSyncServiceTests
{
    private static readonly DateTimeOffset SyncTime = DateTimeOffset.Parse("2026-06-03T12:00:00Z");

    [Fact]
    public async Task SyncAsync_WhenMoviesFetched_UpsertsMappedMoviesAndDeletesRemovedLetterboxdMovies()
    {
        FakeLetterboxdWatchlistClient client = new([
            new LetterboxdMovieDto("1418998", "tt35450621", "Karma", 2026, "/film/karma-2026/"),
            new LetterboxdMovieDto("4951", "tt0147800", "10 Things I Hate About You", 1999, "/film/10-things-i-hate-about-you/")
        ]);
        FakeWatchlistWriteRepository repository = new([
            CreateExistingMovie("4951"),
            CreateRemovedLetterboxdMovie("old"),
            CreateTvShow()
        ]);
        LetterboxdMovieSyncService service = CreateService(client, repository);

        LetterboxdSyncResultDto result = await service.SyncAsync(CancellationToken.None);

        result.Status.Should().Be("completed");
        result.StartedAt.Should().Be(SyncTime);
        result.FinishedAt.Should().Be(SyncTime);
        result.ItemsFetched.Should().Be(2);
        result.ItemsUpserted.Should().Be(2);
        result.ItemsDeleted.Should().Be(1);
        repository.AppliedItems.Select(item => item.Item.Id).Should().Equal(
            "movie-letterboxd-1418998",
            "movie-letterboxd-4951");
        repository.AppliedItems.Select(item => item.ImdbId).Should().Equal("tt35450621", "tt0147800");
        repository.AppliedItems.Select(item => item.LetterboxdPath).Should().Equal(
            "/film/karma-2026/",
            "/film/10-things-i-hate-about-you/");
        repository.AppliedSourceIds.Should().Equal("1418998", "4951");
        repository.CompletedStatuses.Should().Equal("letterboxd_completed");
        repository.CompletedAtValues.Should().Equal(SyncTime);
        repository.Items.Should().Contain(item => item.MediaType == MediaType.TvShow);
    }

    [Fact]
    public async Task SyncAsync_WhenExistingRecordUpdated_PreservesAvailabilityAddedAtAndMetadata()
    {
        WatchlistItem existing = CreateExistingMovie("4951");
        FakeLetterboxdWatchlistClient client = new([
            new LetterboxdMovieDto("4951", "tt0147800", "10 Things I Hate About You", 1999, "/film/10-things-i-hate-about-you/")
        ]);
        FakeWatchlistWriteRepository repository = new([existing]);
        LetterboxdMovieSyncService service = CreateService(client, repository);

        await service.SyncAsync(CancellationToken.None);

        WatchlistItem updated = repository.AppliedItems.Single().Item;
        updated.AvailabilityStatus.Should().Be(AvailabilityStatus.AvailableOnPlex);
        updated.AddedAt.Should().Be(existing.AddedAt);
        updated.Overview.Should().Be(existing.Overview);
        updated.PosterUrl.Should().Be(existing.PosterUrl);
        updated.BackdropUrl.Should().Be(existing.BackdropUrl);
        updated.UpdatedAt.Should().Be(SyncTime);
    }

    [Theory]
    [InlineData(2027, ReleaseStatus.Unreleased, AvailabilityStatus.Unreleased)]
    [InlineData(1999, ReleaseStatus.Released, AvailabilityStatus.NotOnPlex)]
    public async Task SyncAsync_WhenNewRecordImported_MapsReleaseAndAvailability(
        int releaseYear,
        ReleaseStatus releaseStatus,
        AvailabilityStatus availabilityStatus)
    {
        FakeLetterboxdWatchlistClient client = new([
            new LetterboxdMovieDto("source", "tt0000001", "Movie", releaseYear, "/film/movie/")
        ]);
        FakeWatchlistWriteRepository repository = new([]);
        LetterboxdMovieSyncService service = CreateService(client, repository);

        await service.SyncAsync(CancellationToken.None);

        WatchlistItem item = repository.AppliedItems.Single().Item;
        item.ReleaseStatus.Should().Be(releaseStatus);
        item.AvailabilityStatus.Should().Be(availabilityStatus);
        item.AddedAt.Should().Be(SyncTime);
        item.UpdatedAt.Should().Be(SyncTime);
    }

    [Fact]
    public async Task SyncAsync_WhenReleaseYearUnknown_MapsUnknownReleaseAndUnknownMatch()
    {
        FakeLetterboxdWatchlistClient client = new([
            new LetterboxdMovieDto("source", null, "Movie", null, "/film/movie/")
        ]);
        FakeWatchlistWriteRepository repository = new([]);
        LetterboxdMovieSyncService service = CreateService(client, repository);

        await service.SyncAsync(CancellationToken.None);

        WatchlistItem item = repository.AppliedItems.Single().Item;
        item.ReleaseStatus.Should().Be(ReleaseStatus.Unknown);
        item.AvailabilityStatus.Should().Be(AvailabilityStatus.UnknownMatch);
    }

    [Fact]
    public async Task SyncAsync_WhenFetchFails_DoesNotModifyRepository()
    {
        FakeLetterboxdWatchlistClient client = new(new LetterboxdUnavailableException("unavailable"));
        FakeWatchlistWriteRepository repository = new([CreateExistingMovie("4951")]);
        LetterboxdMovieSyncService service = CreateService(client, repository);

        Func<Task> action = () => service.SyncAsync(CancellationToken.None);

        await action.Should().ThrowAsync<LetterboxdUnavailableException>();
        repository.AppliedItems.Should().BeEmpty();
        repository.AppliedSourceIds.Should().BeEmpty();
        repository.CompletedStatuses.Should().BeEmpty();
    }

    private static LetterboxdMovieSyncService CreateService(
        ILetterboxdWatchlistClient client,
        FakeWatchlistWriteRepository repository)
    {
        return new LetterboxdMovieSyncService(
            client,
            repository,
            new FakeTimeProvider(SyncTime));
    }

    private static WatchlistItem CreateExistingMovie(string sourceId)
    {
        return new WatchlistItem(
            $"movie-letterboxd-{sourceId}",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            sourceId,
            "Old Title",
            1999,
            "Existing overview",
            "https://example.test/poster.jpg",
            "https://example.test/backdrop.jpg",
            ReleaseStatus.Released,
            AvailabilityStatus.AvailableOnPlex,
            DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T12:00:00Z"));
    }

    private static WatchlistItem CreateRemovedLetterboxdMovie(string sourceId)
    {
        return new WatchlistItem(
            $"movie-letterboxd-{sourceId}",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            sourceId,
            "Removed",
            2001,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T12:00:00Z"));
    }

    private static WatchlistItem CreateTvShow()
    {
        return new WatchlistItem(
            "tv-andor",
            MediaType.TvShow,
            WatchlistSource.Tmdb,
            "tmdb-andor",
            "Andor",
            2022,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-02T12:00:00Z"));
    }

    private sealed class FakeLetterboxdWatchlistClient : ILetterboxdWatchlistClient
    {
        private readonly IReadOnlyList<LetterboxdMovieDto>? movies;
        private readonly Exception? exception;

        public FakeLetterboxdWatchlistClient(IReadOnlyList<LetterboxdMovieDto> movies)
        {
            this.movies = movies;
        }

        public FakeLetterboxdWatchlistClient(Exception exception)
        {
            this.exception = exception;
        }

        public Task<IReadOnlyList<LetterboxdMovieDto>> GetMoviesAsync(CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(movies ?? []);
        }
    }

    private sealed class FakeWatchlistWriteRepository(IReadOnlyList<WatchlistItem> items) : IWatchlistWriteRepository
    {
        public List<WatchlistItem> Items { get; } = items.ToList();

        public List<WatchlistItemWriteModel> AppliedItems { get; } = [];

        public List<string> AppliedSourceIds { get; } = [];

        public List<string> CompletedStatuses { get; } = [];

        public List<DateTimeOffset> CompletedAtValues { get; } = [];

        public Task<IReadOnlyList<WatchlistItem>> GetItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<WatchlistItem>>(Items);
        }

        public Task<int> ApplyLetterboxdMovieSyncAsync(
            IReadOnlyList<WatchlistItemWriteModel> itemsToUpsert,
            IReadOnlySet<string> sourceIds,
            string completedStatus,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            AppliedItems.AddRange(itemsToUpsert);
            AppliedSourceIds.AddRange(sourceIds);
            CompletedStatuses.Add(completedStatus);
            CompletedAtValues.Add(completedAt);

            List<WatchlistItem> deletedItems = Items
                .Where(item => item.MediaType == MediaType.Movie
                    && item.Source == WatchlistSource.Letterboxd
                    && !sourceIds.Contains(item.SourceId))
                .ToList();

            Items.RemoveAll(deletedItems.Contains);

            return Task.FromResult(deletedItems.Count);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
