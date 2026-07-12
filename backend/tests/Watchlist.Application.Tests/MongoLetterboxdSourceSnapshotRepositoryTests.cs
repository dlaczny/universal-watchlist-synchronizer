using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoLetterboxdSourceSnapshotRepositoryTests : IAsyncLifetime
{
    private readonly string databaseName = $"watchlist_test_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly MongoDbOptions options;
    private readonly IMongoDatabase database;

    public MongoLetterboxdSourceSnapshotRepositoryTests()
    {
        options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName,
            LetterboxdSourceSnapshotsCollectionName = "letterboxd_source_snapshots"
        };
        database = client.GetDatabase(databaseName);
    }

    [Fact]
    public async Task GetLatestAsync_WhenSnapshotsExist_ReturnsNewestCompleteState()
    {
        IMongoCollection<MongoLetterboxdSourceSnapshotDocument> snapshots =
            database.GetCollection<MongoLetterboxdSourceSnapshotDocument>(
                options.LetterboxdSourceSnapshotsCollectionName);
        await snapshots.InsertManyAsync([
            new MongoLetterboxdSourceSnapshotDocument
            {
                Id = "snapshot-1",
                PublishedAt = DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
                SourceIds = ["101"],
                ItemCount = 1
            },
            new MongoLetterboxdSourceSnapshotDocument
            {
                Id = "snapshot-2",
                PublishedAt = DateTimeOffset.Parse("2026-07-12T11:00:00Z"),
                SourceIds = ["202"],
                WatchedMovies =
                [
                    new MongoPublishedWatchedMovieDocument
                    {
                        SourceId = "101",
                        LifecycleEventId = "movie-101:watched:1",
                        WatchedAt = DateTimeOffset.Parse("2026-07-12T11:00:00Z"),
                        LifecycleVersion = 1
                    }
                ],
                ItemCount = 1
            }
        ]);
        MongoLetterboxdSourceSnapshotRepository repository = new(
            database,
            Options.Create(options));

        LetterboxdSourceSnapshot? result = await repository.GetLatestAsync(
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.SnapshotId.Should().Be("snapshot-2");
        result.SourceIds.Should().BeEquivalentTo(["202"]);
        result.WatchedMovies.Should().ContainSingle().Which.Should().Be(
            new PublishedWatchedMovie(
                "101",
                "movie-101:watched:1",
                DateTimeOffset.Parse("2026-07-12T11:00:00Z"),
                1));
    }

    [Fact]
    public async Task GetLatestAsync_WhenNoSnapshotExists_ReturnsNull()
    {
        MongoLetterboxdSourceSnapshotRepository repository = new(
            database,
            Options.Create(options));

        LetterboxdSourceSnapshot? result = await repository.GetLatestAsync(
            CancellationToken.None);

        result.Should().BeNull();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }
}
