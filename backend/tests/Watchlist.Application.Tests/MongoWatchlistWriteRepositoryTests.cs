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
                && item.LetterboxdPath == "/film/karma-2026/"
                && item.TmdbMetadataStatus == "not_synced");
        storedItems.Should().NotContain(item => item.Id == "movie-letterboxd-old");
        storedItems.Should().Contain(item => item.Id == "movie-tmdb-existing");
        storedItems.Should().Contain(item => item.Id == "tv-tmdb-existing");

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
