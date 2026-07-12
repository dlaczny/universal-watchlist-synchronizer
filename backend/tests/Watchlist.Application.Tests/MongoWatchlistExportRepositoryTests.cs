using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
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
            TmdbId = 1297842,
            TmdbMetadataStatus = "enriched",
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
        result.TmdbId.Should().Be(1297842);
        result.MetadataStatus.Should().Be("enriched");
        result.AvailabilityStatus.Should().Be(AvailabilityStatus.NotOnPlex);
        result.ImdbId.Should().Be("tt27613895");
        result.Title.Should().Be("GOAT");
        result.Year.Should().Be(2026);
        result.LetterboxdPath.Should().Be("/film/goat-2026/");
        result.OwnedServiceAvailability.Should().Equal("Amazon Prime Video");
    }
}

public sealed class MongoWatchlistExportRepositoryLifecycleTests : IAsyncLifetime
{
    private readonly string databaseName = $"watchlist_test_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly MongoDbOptions options;
    private readonly IMongoDatabase database;

    public MongoWatchlistExportRepositoryLifecycleTests()
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
    public async Task GetLifecycleExportsAsync_WhenManifestExists_SeparatesActiveAndWatchedMovies()
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        await items.InsertManyAsync([
            CreateMovie("101", "Active", 101),
            CreateMovie("202", "Watched", 202)
        ]);
        IMongoCollection<MongoLetterboxdSourceSnapshotDocument> snapshots =
            database.GetCollection<MongoLetterboxdSourceSnapshotDocument>(
                options.LetterboxdSourceSnapshotsCollectionName);
        await snapshots.InsertOneAsync(new MongoLetterboxdSourceSnapshotDocument
        {
            Id = "snapshot-1",
            PublishedAt = DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
            SourceIds = ["101"],
            WatchedMovies =
            [
                new MongoPublishedWatchedMovieDocument
                {
                    SourceId = "202",
                    LifecycleEventId = "movie-202:watched:1",
                    WatchedAt = DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
                    LifecycleVersion = 1
                }
            ],
            ItemCount = 1
        });
        MongoWatchlistExportRepository repository = new(database, Options.Create(options));

        WatchlistMovieLifecycleExport lifecycle =
            await repository.GetMovieLifecycleAsync(CancellationToken.None);

        lifecycle.ActiveMovies.Should().ContainSingle(item => item.SourceId == "101");
        lifecycle.ActiveMovies.Should().NotContain(item => item.SourceId == "202");
        lifecycle.WatchedMovies.Should().ContainSingle().Which.Should().Be(new WatchlistWatchedMovieModel(
            202,
            "tt0000202",
            "Watched",
            2024,
            "202",
            DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
            1,
            "movie-202:watched:1"));
        lifecycle.SourceSnapshot!.SnapshotId.Should().Be("snapshot-1");
    }

    private static MongoWatchlistItemDocument CreateMovie(
        string sourceId,
        string title,
        int tmdbId)
    {
        return new MongoWatchlistItemDocument
        {
            Id = $"movie-letterboxd-{sourceId}",
            MediaType = MediaType.Movie,
            Source = WatchlistSource.Letterboxd,
            SourceId = sourceId,
            Title = title,
            Year = 2024,
            TmdbId = tmdbId,
            ImdbId = $"tt{tmdbId:D7}",
            TmdbMetadataStatus = "enriched",
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.NotOnPlex,
            AddedAt = DateTimeOffset.Parse("2026-07-12T09:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-07-12T09:00:00Z")
        };
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }
}
