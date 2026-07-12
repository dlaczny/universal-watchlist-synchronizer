using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoPlexMovieInventoryRepositoryTests : IAsyncLifetime
{
    private readonly string databaseName = $"watchlist_test_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly MongoDbOptions options;
    private readonly IMongoDatabase database;

    public MongoPlexMovieInventoryRepositoryTests()
    {
        options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName,
            WatchlistItemsCollectionName = "watchlist_items",
            SyncRunsCollectionName = "sync_runs",
            PlexLibraryItemsCollectionName = "plex_library_items"
        };
        database = client.GetDatabase(databaseName);
    }

    [Fact]
    public async Task ApplyMovieInventoryAsync_UpsertsCurrentMoviesAndDeletesStaleMoviesForScannedSections()
    {
        IMongoCollection<MongoPlexLibraryItemDocument> collection =
            database.GetCollection<MongoPlexLibraryItemDocument>(options.PlexLibraryItemsCollectionName);
        await collection.InsertManyAsync([
            CreatePlexDocument("old", "1", "Old"),
            CreatePlexDocument("keep-tv-section", "2", "Other Section")
        ]);
        MongoPlexMovieInventoryRepository repository = new(database, Options.Create(options));
        DateTimeOffset syncTime = DateTimeOffset.Parse("2026-06-05T12:00:00Z");

        PlexInventoryApplyResult result = await repository.ApplyMovieInventoryAsync(
            [new PlexMovieDto("8058", "10 Things I Hate About You", 1999, "1", "Filmy", "plex://movie/local", "tt0147800", 4951, 836)],
            new HashSet<string>(["1"], StringComparer.Ordinal),
            syncTime,
            CancellationToken.None);

        result.ItemsUpserted.Should().Be(1);
        result.ItemsDeleted.Should().Be(1);
        List<MongoPlexLibraryItemDocument> stored = await collection.Find(FilterDefinition<MongoPlexLibraryItemDocument>.Empty).ToListAsync();
        stored.Should().ContainSingle(item => item.Id == "plex-movie-8058"
            && item.ImdbId == "tt0147800"
            && item.TmdbId == 4951
            && item.LastSeenAt == syncTime);
        stored.Should().ContainSingle(item => item.Id == "plex-movie-keep-tv-section");
        stored.Should().NotContain(item => item.Id == "plex-movie-old");
    }

    [Fact]
    public async Task ApplyMatchUpdatesAsync_UpdatesOnlyLetterboxdMoviesAndWritesSyncRun()
    {
        IMongoCollection<MongoWatchlistItemDocument> watchlist =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        IMongoCollection<MongoSyncRunDocument> syncRuns =
            database.GetCollection<MongoSyncRunDocument>(options.SyncRunsCollectionName);
        await watchlist.InsertOneAsync(MongoWatchlistItemDocument.FromDomain(CreateWatchlistMovie()));
        MongoPlexMovieInventoryRepository repository = new(database, Options.Create(options));
        DateTimeOffset completedAt = DateTimeOffset.Parse("2026-06-05T12:00:00Z");

        await repository.ApplyMatchUpdatesAsync(
            [new PlexMovieMatchUpdate("movie-letterboxd-4951", AvailabilityStatus.AvailableOnPlex, "8058", completedAt, "imdb", "exact")],
            "plex_movies_completed",
            completedAt,
            CancellationToken.None);

        MongoWatchlistItemDocument stored = await watchlist.Find(item => item.Id == "movie-letterboxd-4951").SingleAsync();
        stored.AvailabilityStatus.Should().Be(AvailabilityStatus.AvailableOnPlex);
        stored.PlexRatingKey.Should().Be("8058");
        stored.PlexMatchedAt.Should().Be(completedAt);
        stored.PlexMatchReason.Should().Be("imdb");
        stored.PlexMatchConfidence.Should().Be("exact");
        MongoSyncRunDocument run = await syncRuns.Find(FilterDefinition<MongoSyncRunDocument>.Empty).SingleAsync();
        run.Status.Should().Be("plex_movies_completed");
    }

    [Fact]
    public async Task GetWatchlistMoviesAsync_WhenManifestExists_ExcludesWatchedFromMatching()
    {
        IMongoCollection<MongoWatchlistItemDocument> watchlist =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        MongoWatchlistItemDocument active = MongoWatchlistItemDocument.FromDomain(
            CreateWatchlistMovie());
        MongoWatchlistItemDocument watched = new()
        {
            Id = "movie-letterboxd-202",
            MediaType = MediaType.Movie,
            Source = WatchlistSource.Letterboxd,
            SourceId = "202",
            Title = "Watched",
            Year = 2024,
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.AvailableOnPlex,
            PlexRatingKey = "watched-key",
            AddedAt = DateTimeOffset.Parse("2026-07-12T09:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-07-12T09:00:00Z")
        };
        await watchlist.InsertManyAsync([active, watched]);
        IMongoCollection<MongoPlexLibraryItemDocument> plexItems =
            database.GetCollection<MongoPlexLibraryItemDocument>(options.PlexLibraryItemsCollectionName);
        await plexItems.InsertOneAsync(CreatePlexDocument("watched-key", "1", "Watched"));
        IMongoCollection<MongoLetterboxdSourceSnapshotDocument> snapshots =
            database.GetCollection<MongoLetterboxdSourceSnapshotDocument>(
                options.LetterboxdSourceSnapshotsCollectionName);
        await snapshots.InsertOneAsync(new MongoLetterboxdSourceSnapshotDocument
        {
            Id = "snapshot-1",
            PublishedAt = DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
            SourceIds = [active.SourceId],
            WatchedMovies =
            [
                new MongoPublishedWatchedMovieDocument
                {
                    SourceId = watched.SourceId,
                    LifecycleEventId = "movie-202:watched:1",
                    WatchedAt = DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
                    LifecycleVersion = 1
                }
            ],
            ItemCount = 1
        });
        MongoPlexMovieInventoryRepository repository = new(database, Options.Create(options));

        IReadOnlyList<WatchlistItemWriteModel> activeMovies =
            await repository.GetWatchlistMoviesAsync(CancellationToken.None);
        IReadOnlyList<PlexMovieDto> unmatched =
            await repository.GetUnmatchedMoviesAsync(CancellationToken.None);

        activeMovies.Should().ContainSingle(item => item.Item.Id == active.Id);
        unmatched.Should().ContainSingle(item => item.RatingKey == "watched-key");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }

    private static MongoPlexLibraryItemDocument CreatePlexDocument(string ratingKey, string sectionKey, string title)
    {
        return new MongoPlexLibraryItemDocument
        {
            Id = $"plex-movie-{ratingKey}",
            RatingKey = ratingKey,
            MediaType = MediaType.Movie,
            Title = title,
            Year = 2020,
            LibrarySectionKey = sectionKey,
            LibrarySectionTitle = "Filmy",
            LastSeenAt = DateTimeOffset.Parse("2026-06-04T12:00:00Z")
        };
    }

    private static WatchlistItem CreateWatchlistMovie()
    {
        return new WatchlistItem(
            "movie-letterboxd-4951",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            "4951",
            "10 Things I Hate About You",
            1999,
            "overview",
            "poster",
            "backdrop",
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-01T12:00:00Z"));
    }
}
