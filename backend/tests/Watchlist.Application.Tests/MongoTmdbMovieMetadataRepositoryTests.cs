using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoTmdbMovieMetadataRepositoryTests : IAsyncLifetime
{
    private readonly string databaseName = $"watchlist_test_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly MongoDbOptions options;
    private readonly IMongoDatabase database;

    public MongoTmdbMovieMetadataRepositoryTests()
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
    public async Task GetAndApplyTmdbMetadataAsync_FiltersLetterboxdMoviesAndUpdatesOnlyTmdbMetadataFields()
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        MongoWatchlistItemDocument letterboxdMovie = CreateLetterboxdMovie();
        MongoWatchlistItemDocument tmdbMovie = CreateTmdbMovie();
        MongoWatchlistItemDocument tvShow = CreateTvShow();
        await items.InsertManyAsync([letterboxdMovie, tmdbMovie, tvShow]);
        MongoTmdbMovieMetadataRepository repository = new(database, Options.Create(options));
        DateTimeOffset updatedAt = DateTimeOffset.Parse("2026-06-04T12:00:00Z");
        TmdbMovieMetadataUpdate update = new(
            1297842,
            "tt27613895",
            "GOAT",
            "GOAT Original",
            "A promising athlete story.",
            "2026-02-13",
            ["Drama", "Thriller"],
            "/poster.jpg",
            "/backdrop.jpg",
            "https://image.tmdb.org/t/p/w500/poster.jpg",
            "https://image.tmdb.org/t/p/w1280/backdrop.jpg",
            new TmdbMovieProviderDataDto(new Dictionary<string, TmdbRegionWatchProvidersDto>
            {
                ["PL"] = new(
                    [new TmdbWatchProviderDto(119, "Amazon Prime Video", "/prime.jpg", 1)],
                    [new TmdbWatchProviderDto(10, "Apple TV", "/apple.jpg", 2)],
                    []),
                ["US"] = new(
                    [],
                    [],
                    [new TmdbWatchProviderDto(2, "Amazon Video", "/amazon.jpg", 3)])
            }),
            ["Amazon Prime Video"],
            true,
            ["PL", "US"],
            updatedAt,
            "enriched",
            null);

        IReadOnlyList<WatchlistItemWriteModel> letterboxdMovies =
            await repository.GetLetterboxdMoviesAsync(CancellationToken.None);
        WatchlistItemWriteModel? fetchedLetterboxdMovie =
            await repository.GetLetterboxdMovieAsync("movie-letterboxd-1297842", CancellationToken.None);
        WatchlistItemWriteModel? fetchedTmdbMovie =
            await repository.GetLetterboxdMovieAsync("movie-tmdb-1297842", CancellationToken.None);

        await repository.ApplyTmdbMetadataAsync(
            "movie-letterboxd-1297842",
            update,
            CancellationToken.None);

        letterboxdMovies.Should().ContainSingle().Which.Should().Match<WatchlistItemWriteModel>(item =>
            item.Item.Id == "movie-letterboxd-1297842"
            && item.ImdbId == "tt-old"
            && item.LetterboxdPath == "/film/goat/");
        fetchedLetterboxdMovie.Should().NotBeNull();
        fetchedTmdbMovie.Should().BeNull();

        MongoWatchlistItemDocument storedLetterboxdMovie = await items
            .Find(item => item.Id == "movie-letterboxd-1297842")
            .SingleAsync();
        storedLetterboxdMovie.TmdbId.Should().Be(1297842);
        storedLetterboxdMovie.ImdbId.Should().Be("tt27613895");
        storedLetterboxdMovie.TmdbTitle.Should().Be("GOAT");
        storedLetterboxdMovie.OriginalTitle.Should().Be("GOAT Original");
        storedLetterboxdMovie.Overview.Should().Be("A promising athlete story.");
        storedLetterboxdMovie.ReleaseDate.Should().Be("2026-02-13");
        storedLetterboxdMovie.Genres.Should().Equal("Drama", "Thriller");
        storedLetterboxdMovie.PosterPath.Should().Be("/poster.jpg");
        storedLetterboxdMovie.BackdropPath.Should().Be("/backdrop.jpg");
        storedLetterboxdMovie.PosterUrl.Should().Be("https://image.tmdb.org/t/p/w500/poster.jpg");
        storedLetterboxdMovie.BackdropUrl.Should().Be("https://image.tmdb.org/t/p/w1280/backdrop.jpg");
        storedLetterboxdMovie.WatchProviders.Should().ContainKeys("PL", "US");
        storedLetterboxdMovie.WatchProviders["PL"].Flatrate.Should().ContainSingle(provider =>
            provider.ProviderId == 119
            && provider.ProviderName == "Amazon Prime Video"
            && provider.LogoPath == "/prime.jpg"
            && provider.DisplayPriority == 1);
        storedLetterboxdMovie.WatchProviders["PL"].Rent.Should().ContainSingle(provider =>
            provider.ProviderName == "Apple TV");
        storedLetterboxdMovie.WatchProviders["US"].Buy.Should().ContainSingle(provider =>
            provider.ProviderName == "Amazon Video");
        storedLetterboxdMovie.OwnedServiceAvailability.Should().Equal("Amazon Prime Video");
        storedLetterboxdMovie.ReleasedOnVod.Should().BeTrue();
        storedLetterboxdMovie.VodRegions.Should().Equal("PL", "US");
        storedLetterboxdMovie.TmdbMetadataUpdatedAt.Should().Be(updatedAt);
        storedLetterboxdMovie.TmdbMetadataStatus.Should().Be("enriched");
        storedLetterboxdMovie.TmdbMetadataError.Should().BeNull();

        storedLetterboxdMovie.AvailabilityStatus.Should().Be(letterboxdMovie.AvailabilityStatus);
        storedLetterboxdMovie.AddedAt.Should().Be(letterboxdMovie.AddedAt);
        storedLetterboxdMovie.Source.Should().Be(letterboxdMovie.Source);
        storedLetterboxdMovie.SourceId.Should().Be(letterboxdMovie.SourceId);

        MongoWatchlistItemDocument storedTmdbMovie = await items
            .Find(item => item.Id == "movie-tmdb-1297842")
            .SingleAsync();
        MongoWatchlistItemDocument storedTvShow = await items
            .Find(item => item.Id == "tv-tmdb-1")
            .SingleAsync();
        storedTmdbMovie.Should().BeEquivalentTo(tmdbMovie);
        storedTvShow.Should().BeEquivalentTo(tvShow);
    }

    [Theory]
    [InlineData("failed", "TMDB returned HTTP 503.")]
    [InlineData("not_found", "TMDB movie 1297842 was not found.")]
    public async Task ApplyTmdbMetadataAsync_WhenStatusIsFailureOrNotFound_PreservesExistingMetadata(
        string metadataStatus,
        string metadataError)
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        MongoWatchlistItemDocument letterboxdMovie = CreateEnrichedLetterboxdMovie();
        await items.InsertOneAsync(letterboxdMovie);
        MongoTmdbMovieMetadataRepository repository = new(database, Options.Create(options));
        DateTimeOffset updatedAt = DateTimeOffset.Parse("2026-06-04T13:00:00Z");
        TmdbMovieMetadataUpdate update = new(
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            null,
            null,
            null,
            new TmdbMovieProviderDataDto(new Dictionary<string, TmdbRegionWatchProvidersDto>()),
            [],
            false,
            [],
            updatedAt,
            metadataStatus,
            metadataError);

        await repository.ApplyTmdbMetadataAsync(
            "movie-letterboxd-1297842",
            update,
            CancellationToken.None);

        MongoWatchlistItemDocument storedLetterboxdMovie = await items
            .Find(item => item.Id == "movie-letterboxd-1297842")
            .SingleAsync();
        storedLetterboxdMovie.TmdbId.Should().Be(letterboxdMovie.TmdbId);
        storedLetterboxdMovie.ImdbId.Should().Be(letterboxdMovie.ImdbId);
        storedLetterboxdMovie.TmdbTitle.Should().Be(letterboxdMovie.TmdbTitle);
        storedLetterboxdMovie.OriginalTitle.Should().Be(letterboxdMovie.OriginalTitle);
        storedLetterboxdMovie.Overview.Should().Be(letterboxdMovie.Overview);
        storedLetterboxdMovie.ReleaseDate.Should().Be(letterboxdMovie.ReleaseDate);
        storedLetterboxdMovie.Genres.Should().Equal(letterboxdMovie.Genres);
        storedLetterboxdMovie.PosterPath.Should().Be(letterboxdMovie.PosterPath);
        storedLetterboxdMovie.BackdropPath.Should().Be(letterboxdMovie.BackdropPath);
        storedLetterboxdMovie.PosterUrl.Should().Be(letterboxdMovie.PosterUrl);
        storedLetterboxdMovie.BackdropUrl.Should().Be(letterboxdMovie.BackdropUrl);
        storedLetterboxdMovie.WatchProviders.Should().BeEquivalentTo(letterboxdMovie.WatchProviders);
        storedLetterboxdMovie.OwnedServiceAvailability.Should().Equal(letterboxdMovie.OwnedServiceAvailability);
        storedLetterboxdMovie.ReleasedOnVod.Should().Be(letterboxdMovie.ReleasedOnVod);
        storedLetterboxdMovie.VodRegions.Should().Equal(letterboxdMovie.VodRegions);
        storedLetterboxdMovie.TmdbMetadataUpdatedAt.Should().Be(updatedAt);
        storedLetterboxdMovie.TmdbMetadataStatus.Should().Be(metadataStatus);
        storedLetterboxdMovie.TmdbMetadataError.Should().Be(metadataError);
    }

    [Fact]
    public async Task GetLetterboxdMoviesAsync_IncludesTmdbIdForPlexMatching()
    {
        IMongoCollection<MongoWatchlistItemDocument> items =
            database.GetCollection<MongoWatchlistItemDocument>(options.WatchlistItemsCollectionName);
        MongoWatchlistItemDocument document = CreateLetterboxdMovie();
        document = new MongoWatchlistItemDocument
        {
            Id = document.Id,
            MediaType = document.MediaType,
            Source = document.Source,
            SourceId = document.SourceId,
            Title = document.Title,
            Year = document.Year,
            ImdbId = "tt27613895",
            LetterboxdPath = document.LetterboxdPath,
            Overview = document.Overview,
            PosterUrl = document.PosterUrl,
            BackdropUrl = document.BackdropUrl,
            TmdbId = 1297842,
            ReleaseStatus = document.ReleaseStatus,
            AvailabilityStatus = document.AvailabilityStatus,
            AddedAt = document.AddedAt,
            UpdatedAt = document.UpdatedAt
        };
        await items.InsertOneAsync(document);
        MongoTmdbMovieMetadataRepository repository = new(database, Options.Create(options));

        IReadOnlyList<WatchlistItemWriteModel> movies = await repository.GetLetterboxdMoviesAsync(CancellationToken.None);

        movies.Should().ContainSingle(movie =>
            movie.Item.Id == "movie-letterboxd-1297842"
            && movie.ImdbId == "tt27613895"
            && movie.TmdbId == 1297842);
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }

    private static MongoWatchlistItemDocument CreateLetterboxdMovie()
    {
        return new MongoWatchlistItemDocument
        {
            Id = "movie-letterboxd-1297842",
            MediaType = MediaType.Movie,
            Source = WatchlistSource.Letterboxd,
            SourceId = "1297842",
            Title = "GOAT",
            Year = 2026,
            ImdbId = "tt-old",
            LetterboxdPath = "/film/goat/",
            Overview = "Old overview",
            PosterUrl = "https://example.test/old-poster.jpg",
            BackdropUrl = "https://example.test/old-backdrop.jpg",
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.AvailableOnPlex,
            AddedAt = DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-02T12:00:00Z")
        };
    }

    private static MongoWatchlistItemDocument CreateEnrichedLetterboxdMovie()
    {
        return new MongoWatchlistItemDocument
        {
            Id = "movie-letterboxd-1297842",
            MediaType = MediaType.Movie,
            Source = WatchlistSource.Letterboxd,
            SourceId = "1297842",
            Title = "GOAT",
            Year = 2026,
            ImdbId = "tt27613895",
            LetterboxdPath = "/film/goat/",
            Overview = "A promising athlete story.",
            PosterUrl = "https://image.tmdb.org/t/p/w500/poster.jpg",
            BackdropUrl = "https://image.tmdb.org/t/p/w1280/backdrop.jpg",
            TmdbId = 1297842,
            TmdbTitle = "GOAT",
            OriginalTitle = "GOAT Original",
            ReleaseDate = "2026-02-13",
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
                            ProviderId = 10,
                            ProviderName = "Apple TV",
                            LogoPath = "/apple.jpg",
                            DisplayPriority = 2
                        }
                    ],
                    Buy = []
                }
            },
            OwnedServiceAvailability = ["Amazon Prime Video"],
            ReleasedOnVod = true,
            VodRegions = ["PL"],
            TmdbMetadataUpdatedAt = DateTimeOffset.Parse("2026-06-04T12:00:00Z"),
            TmdbMetadataStatus = "enriched",
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.AvailableOnPlex,
            AddedAt = DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-02T12:00:00Z")
        };
    }

    private static MongoWatchlistItemDocument CreateTmdbMovie()
    {
        return new MongoWatchlistItemDocument
        {
            Id = "movie-tmdb-1297842",
            MediaType = MediaType.Movie,
            Source = WatchlistSource.Tmdb,
            SourceId = "1297842",
            Title = "TMDB Movie",
            Year = 2026,
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.NotOnPlex,
            AddedAt = DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-02T12:00:00Z")
        };
    }

    private static MongoWatchlistItemDocument CreateTvShow()
    {
        return new MongoWatchlistItemDocument
        {
            Id = "tv-tmdb-1",
            MediaType = MediaType.TvShow,
            Source = WatchlistSource.Tmdb,
            SourceId = "1",
            Title = "TMDB Show",
            Year = 2026,
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.NotOnPlex,
            AddedAt = DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-02T12:00:00Z")
        };
    }
}
