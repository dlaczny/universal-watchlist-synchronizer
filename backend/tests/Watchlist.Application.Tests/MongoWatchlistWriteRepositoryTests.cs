using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
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
            SyncRunsCollectionName = "sync_runs",
            LetterboxdSourceSnapshotsCollectionName = "letterboxd_source_snapshots"
        };
        database = client.GetDatabase(databaseName);
    }

    [Fact]
    public async Task ApplyLetterboxdMovieSyncAsync_UpsertsTraceFieldsRetainsWatchedMoviesAndPublishesManifest()
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        IMongoCollection<MongoSyncRunDocument> syncRuns =
            database.GetCollection<MongoSyncRunDocument>(options.SyncRunsCollectionName);
        IMongoCollection<MongoLetterboxdSourceSnapshotDocument> snapshots =
            database.GetCollection<MongoLetterboxdSourceSnapshotDocument>(
                options.LetterboxdSourceSnapshotsCollectionName);
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

        LetterboxdMovieSyncApplyResult result = await repository.ApplyLetterboxdMovieSyncAsync(
            [writeModel],
            new HashSet<string>(["1418998"], StringComparer.Ordinal),
            "letterboxd_completed",
            completedAt,
            CancellationToken.None);

        result.ItemsMarkedWatched.Should().Be(1);
        result.SourceSnapshotId.Should().StartWith("letterboxd-");
        List<MongoWatchlistItemDocument> storedItems = await items
            .Find(FilterDefinition<MongoWatchlistItemDocument>.Empty)
            .ToListAsync();
        storedItems.Should().ContainSingle(item => item.Id == "movie-letterboxd-1418998")
            .Which.Should().Match<MongoWatchlistItemDocument>(item =>
                item.ImdbId == "tt35450621"
                && item.LetterboxdPath == "/film/karma-2026/"
                && item.TmdbMetadataStatus == "not_synced");
        MongoWatchlistItemDocument watched = storedItems
            .Single(item => item.Id == "movie-letterboxd-old");
        watched.LastWatchedAt.Should().Be(completedAt);
        watched.LifecycleVersion.Should().Be(1);
        watched.LifecycleEvents.Should().ContainSingle().Which.Should().Match<MongoMovieLifecycleEventDocument>(
            item => item.EventType == "watched"
                && item.SourceSnapshotId == result.SourceSnapshotId
                && item.LifecycleVersion == 1
                && item.OccurredAt == completedAt);

        MongoWatchlistItemDocument added = storedItems
            .Single(item => item.Id == "movie-letterboxd-1418998");
        added.LastSeenInSourceAt.Should().Be(completedAt);
        added.LifecycleVersion.Should().Be(1);
        added.LifecycleEvents.Should().ContainSingle(item => item.EventType == "added");
        storedItems.Should().Contain(item => item.Id == "movie-tmdb-existing");
        storedItems.Should().Contain(item => item.Id == "tv-tmdb-existing");

        List<MongoLetterboxdSourceSnapshotDocument> published = await snapshots
            .Find(FilterDefinition<MongoLetterboxdSourceSnapshotDocument>.Empty)
            .SortBy(item => item.PublishedAt)
            .ToListAsync();
        published.Should().HaveCount(2);
        MongoLetterboxdSourceSnapshotDocument bootstrap = published[0];
        bootstrap.Id.Should().StartWith("letterboxd-bootstrap-");
        bootstrap.SourceIds.Should().Equal("old");
        bootstrap.ItemCount.Should().Be(1);
        bootstrap.WatchedMovies.Should().BeEmpty();

        MongoLetterboxdSourceSnapshotDocument snapshot = published[1];
        snapshot.Id.Should().Be(result.SourceSnapshotId);
        snapshot.PublishedAt.Should().Be(completedAt);
        snapshot.SourceIds.Should().Equal("1418998");
        snapshot.ItemCount.Should().Be(1);
        snapshot.WatchedMovies.Should().ContainSingle().Which.Should().Match<MongoPublishedWatchedMovieDocument>(
            item => item.SourceId == "old"
                && item.WatchedAt == completedAt
                && item.LifecycleVersion == 1
                && item.LifecycleEventId == watched.LifecycleEvents.Single().EventId);

        MongoSyncRunDocument syncRun = await syncRuns
            .Find(FilterDefinition<MongoSyncRunDocument>.Empty)
            .SingleAsync();
        syncRun.Status.Should().Be("letterboxd_completed");
        syncRun.LastSuccessfulSyncAt.Should().Be(completedAt);
    }

    [Fact]
    public async Task ApplyLetterboxdMovieSyncAsync_WhenLetterboxdMovieAlreadyHasTmdbMetadata_PreservesMetadata()
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        DateTimeOffset tmdbMetadataUpdatedAt = DateTimeOffset.Parse("2026-06-04T09:00:00Z");
        MongoWatchlistItemDocument existingDocument = new()
        {
            Id = "movie-letterboxd-1418998",
            MediaType = MediaType.Movie,
            Source = WatchlistSource.Letterboxd,
            SourceId = "1418998",
            Title = "Karma",
            Year = 2026,
            ReleaseStatus = ReleaseStatus.Unreleased,
            AvailabilityStatus = AvailabilityStatus.Unreleased,
            AddedAt = DateTimeOffset.Parse("2026-06-03T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-03T12:00:00Z"),
            TmdbId = 1297842,
            TmdbTitle = "Karma TMDB",
            OriginalTitle = "Karma Original",
            ReleaseDate = "2026-05-29",
            Genres = ["Drama", "Thriller"],
            PosterPath = "/poster.jpg",
            BackdropPath = "/backdrop.jpg",
            WatchProviders = new Dictionary<string, MongoRegionWatchProvidersDocument>
            {
                ["PL"] = new()
                {
                    Flatrate =
                    [
                        new MongoWatchProviderDocument
                        {
                            ProviderId = 119,
                            ProviderName = "Amazon Prime Video",
                            LogoPath = "/prime.jpg",
                            DisplayPriority = 1
                        }
                    ],
                    Rent =
                    [
                        new MongoWatchProviderDocument
                        {
                            ProviderId = 2,
                            ProviderName = "Apple TV",
                            LogoPath = "/apple.jpg",
                            DisplayPriority = 2
                        }
                    ],
                    Buy =
                    [
                        new MongoWatchProviderDocument
                        {
                            ProviderId = 3,
                            ProviderName = "Google Play Movies",
                            LogoPath = "/google.jpg",
                            DisplayPriority = 3
                        }
                    ]
                }
            },
            OwnedServiceAvailability = ["Amazon Prime Video"],
            ReleasedOnVod = true,
            VodRegions = ["PL", "US"],
            TmdbMetadataUpdatedAt = tmdbMetadataUpdatedAt,
            TmdbMetadataStatus = "failed",
            TmdbMetadataError = "Rate limited",
            PlexRatingKey = "8058",
            PlexMatchedAt = DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
            PlexMatchReason = "imdb",
            PlexMatchConfidence = "exact"
        };
        await items.InsertOneAsync(existingDocument);
        MongoWatchlistWriteRepository repository = new(database, Options.Create(options));
        WatchlistItemWriteModel writeModel = new(
            CreateLetterboxdMovie("1418998", "Karma Updated"),
            "tt35450621",
            "/film/karma-2026/");

        await repository.ApplyLetterboxdMovieSyncAsync(
            [writeModel],
            new HashSet<string>(["1418998"], StringComparer.Ordinal),
            "letterboxd_completed",
            DateTimeOffset.Parse("2026-06-04T10:00:00Z"),
            CancellationToken.None);

        MongoWatchlistItemDocument storedDocument = await items
            .Find(item => item.Id == "movie-letterboxd-1418998")
            .SingleAsync();
        storedDocument.Title.Should().Be("Karma Updated");
        storedDocument.ImdbId.Should().Be("tt35450621");
        storedDocument.LetterboxdPath.Should().Be("/film/karma-2026/");
        storedDocument.TmdbId.Should().Be(1297842);
        storedDocument.TmdbTitle.Should().Be("Karma TMDB");
        storedDocument.OriginalTitle.Should().Be("Karma Original");
        storedDocument.ReleaseDate.Should().Be("2026-05-29");
        storedDocument.Genres.Should().Equal("Drama", "Thriller");
        storedDocument.PosterPath.Should().Be("/poster.jpg");
        storedDocument.BackdropPath.Should().Be("/backdrop.jpg");
        storedDocument.WatchProviders.Should().ContainKey("PL");
        storedDocument.WatchProviders["PL"].Flatrate.Should().ContainSingle(provider =>
            provider.ProviderId == 119
            && provider.ProviderName == "Amazon Prime Video"
            && provider.LogoPath == "/prime.jpg"
            && provider.DisplayPriority == 1);
        storedDocument.OwnedServiceAvailability.Should().Equal("Amazon Prime Video");
        storedDocument.ReleasedOnVod.Should().BeTrue();
        storedDocument.VodRegions.Should().Equal("PL", "US");
        storedDocument.TmdbMetadataUpdatedAt.Should().Be(tmdbMetadataUpdatedAt);
        storedDocument.TmdbMetadataStatus.Should().Be("failed");
        storedDocument.TmdbMetadataError.Should().Be("Rate limited");
        storedDocument.PlexRatingKey.Should().Be("8058");
        storedDocument.PlexMatchedAt.Should().Be(DateTimeOffset.Parse("2026-06-05T12:00:00Z"));
        storedDocument.PlexMatchReason.Should().Be("imdb");
        storedDocument.PlexMatchConfidence.Should().Be("exact");
    }

    [Fact]
    public async Task ApplyLetterboxdMovieSyncAsync_WhenWatchedMovieReturns_PreservesHistoryAcrossReactivation()
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        IMongoCollection<MongoLetterboxdSourceSnapshotDocument> snapshots =
            database.GetCollection<MongoLetterboxdSourceSnapshotDocument>(
                options.LetterboxdSourceSnapshotsCollectionName);
        await items.InsertOneAsync(
            MongoWatchlistItemDocument.FromDomain(CreateLetterboxdMovie("101", "Lifecycle Movie")));
        MongoWatchlistWriteRepository repository = new(database, Options.Create(options));
        WatchlistItemWriteModel other = new(
            CreateLetterboxdMovie("202", "Other Movie"),
            "tt0000202",
            "/film/other/");
        WatchlistItemWriteModel lifecycle = new(
            CreateLetterboxdMovie("101", "Lifecycle Movie"),
            "tt0000101",
            "/film/lifecycle/");

        LetterboxdMovieSyncApplyResult watched = await repository.ApplyLetterboxdMovieSyncAsync(
            [other],
            new HashSet<string>(["202"], StringComparer.Ordinal),
            "letterboxd_completed",
            DateTimeOffset.Parse("2026-07-12T10:00:00Z"),
            CancellationToken.None);
        LetterboxdMovieSyncApplyResult stableWatched = await repository.ApplyLetterboxdMovieSyncAsync(
            [other],
            new HashSet<string>(["202"], StringComparer.Ordinal),
            "letterboxd_completed",
            DateTimeOffset.Parse("2026-07-12T11:00:00Z"),
            CancellationToken.None);
        LetterboxdMovieSyncApplyResult reactivated = await repository.ApplyLetterboxdMovieSyncAsync(
            [lifecycle, other],
            new HashSet<string>(["101", "202"], StringComparer.Ordinal),
            "letterboxd_completed",
            DateTimeOffset.Parse("2026-07-12T12:00:00Z"),
            CancellationToken.None);
        LetterboxdMovieSyncApplyResult watchedAgain = await repository.ApplyLetterboxdMovieSyncAsync(
            [other],
            new HashSet<string>(["202"], StringComparer.Ordinal),
            "letterboxd_completed",
            DateTimeOffset.Parse("2026-07-12T13:00:00Z"),
            CancellationToken.None);

        watched.ItemsMarkedWatched.Should().Be(1);
        stableWatched.ItemsMarkedWatched.Should().Be(0);
        reactivated.ItemsMarkedWatched.Should().Be(0);
        watchedAgain.ItemsMarkedWatched.Should().Be(1);

        MongoWatchlistItemDocument stored = await items
            .Find(item => item.SourceId == "101")
            .SingleAsync();
        stored.LifecycleVersion.Should().Be(3);
        stored.LifecycleEvents.Select(item => item.EventType)
            .Should().Equal("watched", "reactivated", "watched");
        stored.LifecycleEvents.Select(item => item.SourceSnapshotId)
            .Should().OnlyHaveUniqueItems();
        stored.LastWatchedAt.Should().Be(DateTimeOffset.Parse("2026-07-12T13:00:00Z"));
        stored.LastSeenInSourceAt.Should().Be(DateTimeOffset.Parse("2026-07-12T12:00:00Z"));

        List<MongoLetterboxdSourceSnapshotDocument> published = await snapshots
            .Find(FilterDefinition<MongoLetterboxdSourceSnapshotDocument>.Empty)
            .SortBy(snapshot => snapshot.PublishedAt)
            .ToListAsync();
        published.Should().HaveCount(5);
        published[0].Id.Should().StartWith("letterboxd-bootstrap-");
        published[0].SourceIds.Should().Equal("101");
        published[0].WatchedMovies.Should().BeEmpty();

        List<MongoLetterboxdSourceSnapshotDocument> operational = published[1..];
        operational[0].WatchedMovies.Should().ContainSingle(item => item.SourceId == "101");
        operational[1].WatchedMovies.Should().BeEquivalentTo(operational[0].WatchedMovies);
        operational[2].WatchedMovies.Should().BeEmpty();
        operational[3].WatchedMovies.Should().ContainSingle().Which.LifecycleEventId
            .Should().Be(stored.LifecycleEvents.Last().EventId);
    }

    [Fact]
    public async Task ApplyLetterboxdMovieSyncAsync_WhenFirstDocumentWritesDoNotComplete_PublishesOnlyBootstrapManifest()
    {
        string interruptedDatabaseName = $"watchlist_test_{Guid.NewGuid():N}";
        using CancellationTokenSource cancellation = new();
        int completedUpdates = 0;
        MongoClientSettings settings = MongoClientSettings.FromConnectionString(
            "mongodb://localhost:27017");
        settings.ClusterConfigurator = cluster => cluster.Subscribe<CommandSucceededEvent>(command =>
        {
            if (command.CommandName == "update"
                && Interlocked.Increment(ref completedUpdates) == 1)
            {
                cancellation.Cancel();
            }
        });
        MongoClient interruptedClient = new(settings);
        IMongoDatabase interruptedDatabase = interruptedClient.GetDatabase(interruptedDatabaseName);
        MongoDbOptions interruptedOptions = new()
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = interruptedDatabaseName,
            WatchlistItemsCollectionName = "watchlist_items",
            SyncRunsCollectionName = "sync_runs",
            LetterboxdSourceSnapshotsCollectionName = "letterboxd_source_snapshots"
        };
        MongoWatchlistWriteRepository repository = new(
            interruptedDatabase,
            Options.Create(interruptedOptions));
        WatchlistItemWriteModel first = new(
            CreateLetterboxdMovie("101", "First"),
            "tt0000101",
            "/film/first/");
        WatchlistItemWriteModel second = new(
            CreateLetterboxdMovie("202", "Second"),
            "tt0000202",
            "/film/second/");

        Func<Task> action = () => repository.ApplyLetterboxdMovieSyncAsync(
            [first, second],
            new HashSet<string>(["101", "202"], StringComparer.Ordinal),
            "letterboxd_completed",
            DateTimeOffset.Parse("2026-07-12T14:00:00Z"),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        IMongoCollection<MongoLetterboxdSourceSnapshotDocument> snapshots =
            interruptedDatabase.GetCollection<MongoLetterboxdSourceSnapshotDocument>(
                interruptedOptions.LetterboxdSourceSnapshotsCollectionName);
        MongoLetterboxdSourceSnapshotDocument published = await snapshots
            .Find(FilterDefinition<MongoLetterboxdSourceSnapshotDocument>.Empty)
            .SingleAsync();
        published.Id.Should().StartWith("letterboxd-bootstrap-");
        published.SourceIds.Should().BeEmpty();
        published.WatchedMovies.Should().BeEmpty();

        MongoWatchlistExportRepository exportRepository = new(
            interruptedDatabase,
            Options.Create(interruptedOptions));
        WatchlistMovieLifecycleExport lifecycle = await exportRepository
            .GetMovieLifecycleAsync(CancellationToken.None);
        lifecycle.SourceSnapshot!.SnapshotId.Should().Be(published.Id);
        lifecycle.ActiveMovies.Should().BeEmpty();
        lifecycle.WatchedMovies.Should().BeEmpty();

        await interruptedClient.DropDatabaseAsync(interruptedDatabaseName);
    }

    [Fact]
    public async Task ApplyTmdbTvWatchlistSyncAsync_UpsertsTvDeletesRemovedTvAndPreservesOtherSources()
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        IMongoCollection<MongoSyncRunDocument> syncRuns =
            database.GetCollection<MongoSyncRunDocument>(options.SyncRunsCollectionName);
        await items.InsertManyAsync([
            MongoWatchlistItemDocument.FromDomain(CreateTmdbTv("removed", "Removed")),
            MongoWatchlistItemDocument.FromDomain(CreateLetterboxdMovie("1297842", "GOAT")),
            MongoWatchlistItemDocument.FromDomain(CreateTmdbMovie())
        ]);
        MongoWatchlistWriteRepository repository = new(database, Options.Create(options));
        WatchlistItem syncedItem = new(
            "tv-tmdb-1399",
            MediaType.TvShow,
            WatchlistSource.Tmdb,
            "1399",
            "Game of Thrones",
            2011,
            "Nine noble families fight for control.",
            "https://image.tmdb.org/t/p/w500/poster.jpg",
            "https://image.tmdb.org/t/p/w1280/backdrop.jpg",
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-06-06T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-06T12:00:00Z"))
        {
            Genres = ["Drama"],
            OriginalLanguage = "en",
            TmdbVoteAverage = 8.5,
            TmdbVoteCount = 25000
        };
        WatchlistItemWriteModel writeModel = new(
            syncedItem,
            "tt0944947",
            null,
            1399,
            TvdbId: 121361);

        TmdbTvWatchlistApplyResult result = await repository.ApplyTmdbTvWatchlistSyncAsync(
            [writeModel],
            new HashSet<string>(["1399"], StringComparer.Ordinal),
            "tmdb_tv_completed",
            DateTimeOffset.Parse("2026-06-06T12:00:01Z"),
            CancellationToken.None);

        result.ItemsUpserted.Should().Be(1);
        result.ItemsDeleted.Should().Be(1);
        MongoWatchlistItemDocument stored = await items.Find(item => item.Id == "tv-tmdb-1399").SingleAsync();
        stored.TmdbId.Should().Be(1399);
        stored.ImdbId.Should().Be("tt0944947");
        stored.TvdbId.Should().Be(121361);
        stored.TmdbMetadataStatus.Should().Be("enriched");
        stored.Genres.Should().Equal("Drama");
        stored.PlexRatingKey.Should().BeNull();

        MongoSyncRunDocument syncRun = await syncRuns
            .Find(FilterDefinition<MongoSyncRunDocument>.Empty)
            .SingleAsync();
        syncRun.Status.Should().Be("tmdb_tv_completed");
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }

    private static WatchlistItem CreateTmdbTv(string sourceId, string title)
    {
        return new WatchlistItem(
            $"tv-tmdb-{sourceId}",
            MediaType.TvShow,
            WatchlistSource.Tmdb,
            sourceId,
            title,
            2024,
            null,
            null,
            null,
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-06-06T12:00:00Z"),
            DateTimeOffset.Parse("2026-06-06T12:00:00Z"));
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
