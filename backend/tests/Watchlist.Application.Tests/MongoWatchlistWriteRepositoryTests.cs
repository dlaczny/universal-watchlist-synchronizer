using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoWatchlistWriteRepositoryTests : IAsyncLifetime
{
    private readonly string databaseName = $"watchlist_test_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly MongoDbOptions options;
    private readonly IMongoDatabase database;

    public MongoWatchlistWriteRepositoryTests()
    {
        options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName,
            WatchlistItemsCollectionName = "watchlist_items",
            SyncRunsCollectionName = "sync_runs"
        };
        database = client.GetDatabase(databaseName);
    }

    [Fact]
    public async Task ApplyLetterboxdMovieSyncAsync_UpsertsTraceFieldsDeletesRemovedMoviesAndPreservesOtherSources()
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        IMongoCollection<MongoSyncRunDocument> syncRuns =
            database.GetCollection<MongoSyncRunDocument>(options.SyncRunsCollectionName);
        await items.InsertManyAsync([
            MongoWatchlistItemDocument.FromDomain(CreateLetterboxdMovie("old", "Removed")),
            MongoWatchlistItemDocument.FromDomain(CreateTmdbMovie()),
            MongoWatchlistItemDocument.FromDomain(CreateTvShow())
        ]);
        MongoWatchlistWriteRepository repository = new(database, Options.Create(options));
        WatchlistItem syncedItem = CreateLetterboxdMovie("1418998", "Karma");
        WatchlistItemWriteModel writeModel = new(
            syncedItem,
            "tt35450621",
            "/film/karma-2026/");
        DateTimeOffset completedAt = DateTimeOffset.Parse("2026-06-03T12:00:01Z");

        int deleted = await repository.ApplyLetterboxdMovieSyncAsync(
            [writeModel],
            new HashSet<string>(["1418998"], StringComparer.Ordinal),
            "letterboxd_completed",
            completedAt,
            CancellationToken.None);

        deleted.Should().Be(1);
        List<MongoWatchlistItemDocument> storedItems = await items
            .Find(FilterDefinition<MongoWatchlistItemDocument>.Empty)
            .ToListAsync();
        storedItems.Should().ContainSingle(item => item.Id == "movie-letterboxd-1418998")
            .Which.Should().Match<MongoWatchlistItemDocument>(item =>
                item.ImdbId == "tt35450621"
                && item.LetterboxdPath == "/film/karma-2026/");
        storedItems.Should().NotContain(item => item.Id == "movie-letterboxd-old");
        storedItems.Should().Contain(item => item.Id == "movie-tmdb-existing");
        storedItems.Should().Contain(item => item.Id == "tv-tmdb-existing");

        MongoSyncRunDocument syncRun = await syncRuns
            .Find(FilterDefinition<MongoSyncRunDocument>.Empty)
            .SingleAsync();
        syncRun.Status.Should().Be("letterboxd_completed");
        syncRun.LastSuccessfulSyncAt.Should().Be(completedAt);
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }

    private static WatchlistItem CreateLetterboxdMovie(string sourceId, string title)
    {
        return new WatchlistItem(
            $"movie-letterboxd-{sourceId}",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            sourceId,
            title,
            2026,
            null,
            null,
            null,
            ReleaseStatus.Unreleased,
            AvailabilityStatus.Unreleased,
            DateTimeOffset.Parse("2026-06-03T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-03T12:00:00Z"));
    }

    private static WatchlistItem CreateTmdbMovie()
    {
        return new WatchlistItem(
            "movie-tmdb-existing",
            MediaType.Movie,
            WatchlistSource.Tmdb,
            "tmdb-existing",
            "TMDB Movie",
            2024,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-06-03T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-03T12:00:00Z"));
    }

    private static WatchlistItem CreateTvShow()
    {
        return new WatchlistItem(
            "tv-tmdb-existing",
            MediaType.TvShow,
            WatchlistSource.Tmdb,
            "tmdb-tv-existing",
            "TMDB Show",
            2024,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-06-03T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-03T12:00:00Z"));
    }
}
