using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Watchlist.Application;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class SeededWatchlistReadRepositoryTests
{
    [Fact]
    public async Task GetItemsAsync_ReturnsPlannedSeedItems()
    {
        SeededWatchlistReadRepository repository = new();

        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(CancellationToken.None);

        items.Should().ContainSingle(item =>
            item.Id == "movie-dune-part-two"
            && item.MediaType == MediaType.Movie
            && item.Source == WatchlistSource.Letterboxd
            && item.SourceId == "letterboxd-dune-part-two"
            && item.Title == "Dune: Part Two"
            && item.Year == 2024
            && item.ReleaseStatus == ReleaseStatus.Released
            && item.AvailabilityStatus == AvailabilityStatus.AvailableOnPlex
            && item.PosterUrl == "https://image.tmdb.org/t/p/w500/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg"
            && item.BackdropUrl == "https://image.tmdb.org/t/p/w1280/xOMo8BRK7PfcJv9JCnx7s5hj0PX.jpg");
        items.Should().ContainSingle(item =>
            item.Id == "movie-unreleased-example"
            && item.MediaType == MediaType.Movie
            && item.Source == WatchlistSource.Letterboxd
            && item.SourceId == "letterboxd-unreleased-example"
            && item.Title == "Future Movie"
            && item.Year == 2027
            && item.ReleaseStatus == ReleaseStatus.Unreleased
            && item.AvailabilityStatus == AvailabilityStatus.Unreleased
            && item.PosterUrl == null
            && item.BackdropUrl == null);
        items.Should().ContainSingle(item =>
            item.Id == "tv-andor"
            && item.MediaType == MediaType.TvShow
            && item.Source == WatchlistSource.Tmdb
            && item.SourceId == "tmdb-tv-83867"
            && item.Title == "Andor"
            && item.Year == 2022
            && item.ReleaseStatus == ReleaseStatus.Released
            && item.AvailabilityStatus == AvailabilityStatus.NotOnPlex
            && item.PosterUrl == "https://image.tmdb.org/t/p/w500/59SVNwLfoMnZPPB6ukW6dlPxAdI.jpg"
            && item.BackdropUrl == "https://image.tmdb.org/t/p/w1280/5NbdcZdsu7Rr0RthcYk4qqv7W7J.jpg");
    }

    [Fact]
    public async Task GetItemsAsync_DoesNotReturnUnspecifiedEnumValues()
    {
        SeededWatchlistReadRepository repository = new();

        IReadOnlyList<WatchlistItem> items = await repository.GetItemsAsync(CancellationToken.None);

        items.Should().NotContain(item => item.MediaType == MediaType.Unspecified);
        items.Should().NotContain(item => item.Source == WatchlistSource.Unspecified);
        items.Should().NotContain(item => item.AvailabilityStatus == AvailabilityStatus.Unspecified);
    }

    [Fact]
    public void AddWatchlistInfrastructure_RegistersMongoRepositoriesAsSingletons()
    {
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoDb:DatabaseName"] = "watchlist-tests",
                ["MongoDb:WatchlistItemsCollectionName"] = "watchlist_items",
                ["MongoDb:SyncRunsCollectionName"] = "sync_runs"
            })
            .Build();

        services.AddWatchlistInfrastructure(configuration);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        IWatchlistReadRepository firstRepository = serviceProvider.GetRequiredService<IWatchlistReadRepository>();
        IWatchlistReadRepository secondRepository = serviceProvider.GetRequiredService<IWatchlistReadRepository>();
        ISyncStatusReadRepository syncStatusRepository =
            serviceProvider.GetRequiredService<ISyncStatusReadRepository>();

        firstRepository.Should().BeOfType<MongoWatchlistReadRepository>();
        secondRepository.Should().BeSameAs(firstRepository);
        syncStatusRepository.Should().BeOfType<MongoSyncStatusReadRepository>();
    }
}
