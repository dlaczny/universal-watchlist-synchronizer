using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoWatchlistReadRepositoryTests : IAsyncLifetime
{
    private readonly string databaseName = $"watchlist_test_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly MongoDbOptions options;
    private readonly IMongoDatabase database;

    public MongoWatchlistReadRepositoryTests()
    {
        options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName,
            WatchlistItemsCollectionName = "watchlist_items",
            LetterboxdSourceSnapshotsCollectionName = "letterboxd_source_snapshots"
        };
        database = client.GetDatabase(databaseName);
    }

    [Fact]
    public async Task GetItemsAsync_WhenManifestExists_ReturnsActiveLetterboxdAndOtherSourcesOnly()
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        await items.InsertManyAsync([
            CreateMovie("101", "Active"),
            CreateMovie("202", "Watched"),
            CreateTvShow()
        ]);
        await PublishSnapshot(["101"], ["202"]);
        MongoWatchlistReadRepository repository = new(database, Options.Create(options));

        IReadOnlyList<WatchlistItem> result = await repository.GetItemsAsync(
            CancellationToken.None);

        result.Select(item => item.Id).Should().BeEquivalentTo(
            "movie-letterboxd-101",
            "tv-tmdb-1");
        result.Should().NotContain(item => item.SourceId == "202");
    }

    [Fact]
    public async Task GetItemsAsync_WhenManifestDoesNotExist_TreatsExistingDocumentsAsActive()
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        await items.InsertManyAsync([CreateMovie("101", "First"), CreateMovie("202", "Second")]);
        MongoWatchlistReadRepository repository = new(database, Options.Create(options));

        IReadOnlyList<WatchlistItem> result = await repository.GetItemsAsync(
            CancellationToken.None);

        result.Select(item => item.SourceId).Should().BeEquivalentTo("101", "202");
    }

    private async Task PublishSnapshot(
        IReadOnlyList<string> activeSourceIds,
        IReadOnlyList<string> watchedSourceIds)
    {
        IMongoCollection<MongoLetterboxdSourceSnapshotDocument> snapshots =
            database.GetCollection<MongoLetterboxdSourceSnapshotDocument>(
                options.LetterboxdSourceSnapshotsCollectionName);
        await snapshots.InsertOneAsync(new MongoLetterboxdSourceSnapshotDocument
        {
            Id = "snapshot-1",
            PublishedAt = DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
            SourceIds = activeSourceIds,
            WatchedMovies = watchedSourceIds.Select(sourceId => new MongoPublishedWatchedMovieDocument
            {
                SourceId = sourceId,
                LifecycleEventId = $"movie-{sourceId}:watched:1",
                WatchedAt = DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
                LifecycleVersion = 1
            }).ToList(),
            ItemCount = activeSourceIds.Count
        });
    }

    private static MongoWatchlistItemDocument CreateMovie(string sourceId, string title)
    {
        return MongoWatchlistItemDocument.FromDomain(new WatchlistItem(
            $"movie-letterboxd-{sourceId}",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            sourceId,
            title,
            2024,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-07-12T09:00:00Z"),
            DateTimeOffset.Parse("2026-07-12T09:00:00Z")));
    }

    private static MongoWatchlistItemDocument CreateTvShow()
    {
        return MongoWatchlistItemDocument.FromDomain(new WatchlistItem(
            "tv-tmdb-1",
            MediaType.TvShow,
            WatchlistSource.Tmdb,
            "1",
            "TV",
            2024,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-07-12T09:00:00Z"),
            DateTimeOffset.Parse("2026-07-12T09:00:00Z")));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }
}
