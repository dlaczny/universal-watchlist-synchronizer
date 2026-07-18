using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoLegacyTvMigrationServiceTests : IAsyncLifetime
{
    private const string WatchlistItemsCollection = "watchlist_items";
    private const string TvShowsCollection = "tv_shows";
    private const string TvManifestsCollection = "tv_sync_manifests";
    private const string TvEventsCollection = "tv_lifecycle_events";

    private static readonly DateTimeOffset MigrationTime =
        DateTimeOffset.Parse("2026-07-18T10:00:00Z");

    private readonly string databaseName = $"watchlist_legacy_tv_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly IMongoDatabase database;
    private readonly MongoDbOptions options;

    public MongoLegacyTvMigrationServiceTests()
    {
        database = client.GetDatabase(databaseName);
        options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName,
            WatchlistItemsCollectionName = WatchlistItemsCollection,
            TvShowsCollectionName = TvShowsCollection,
            TvSyncManifestsCollectionName = TvManifestsCollection,
            TvLifecycleEventsCollectionName = TvEventsCollection
        };
    }

    [Fact]
    public async Task MigrateAsync_ExactLegacyRows_CopyPresentationProvenanceAndIdentitiesOnly()
    {
        await InsertLegacyRowsAsync();
        MongoLegacyTvMigrationService service = CreateService();

        LegacyTvMigrationResult result = await service.MigrateAsync(CancellationToken.None);

        result.MigratedCount.Should().Be(2);
        result.QuarantinedCount.Should().Be(4);
        IMongoCollection<MongoTvShowDocument> shows =
            database.GetCollection<MongoTvShowDocument>(TvShowsCollection);
        MongoTvShowDocument migrated = await shows.Find(document =>
                document.Id == "legacy:legacy-tv-exact")
            .SingleAsync();
        migrated.DocumentKind.Should().Be(MongoTvShowDocument.LegacyDocumentKind);
        migrated.GenerationId.Should().BeNull();
        migrated.TraktId.Should().BeNull();
        migrated.PublicId.Should().BeNull();
        migrated.TvdbId.Should().Be(1001);
        migrated.TmdbId.Should().Be(101);
        migrated.ImdbId.Should().Be("tt0000101");
        migrated.IdentityStatus.Should().Be(TvIdentityStatus.LegacyUnresolved);
        migrated.Title.Should().Be("Exact Legacy Show");
        migrated.Year.Should().Be(2020);
        migrated.Overview.Should().Be("Exact legacy overview");
        migrated.PosterUrl.Should().Be("https://example.test/exact-poster.jpg");
        migrated.BackdropUrl.Should().Be("https://example.test/exact-backdrop.jpg");
        migrated.AddedAt.Should().Be(DateTimeOffset.Parse("2025-01-02T03:04:05Z"));
        migrated.UpdatedAt.Should().Be(DateTimeOffset.Parse("2026-01-02T03:04:05Z"));
        migrated.LegacySourceId.Should().Be("101");
        migrated.LegacyWatchlistItemId.Should().Be("legacy-tv-exact");
        migrated.LegacyMigratedAt.Should().Be(MigrationTime);
        migrated.LegacyMigrationStatus.Should().Be("migrated");
        migrated.LegacyMigrationReason.Should().Be("exact_tvdb_identity");
        migrated.Genres.Should().Equal("Drama", "Mystery");
        migrated.OriginalLanguage.Should().Be("pl");
        migrated.TmdbVoteAverage.Should().Be(8.25);
        migrated.TmdbVoteCount.Should().Be(321);

        MongoTvShowDocument tmdbOnly = await shows.Find(document =>
                document.Id == "legacy:legacy-tv-tmdb-only")
            .SingleAsync();
        tmdbOnly.TmdbId.Should().Be(202);
        tmdbOnly.TvdbId.Should().BeNull();
        tmdbOnly.IdentityStatus.Should().Be(TvIdentityStatus.LegacyUnresolved);
        tmdbOnly.LegacyMigrationStatus.Should().Be("migrated");
        tmdbOnly.LegacyMigrationReason.Should().Be("exact_tmdb_identity");

        IMongoCollection<MongoWatchlistItemDocument> source =
            database.GetCollection<MongoWatchlistItemDocument>(WatchlistItemsCollection);
        (await source.CountDocumentsAsync(FilterDefinition<MongoWatchlistItemDocument>.Empty))
            .Should().Be(7);
        MongoWatchlistItemDocument movie = await source.Find(document =>
                document.Id == "movie-letterboxd-unchanged")
            .SingleAsync();
        movie.Title.Should().Be("Movie Must Remain");
        (await shows.CountDocumentsAsync(document =>
                document.LegacyWatchlistItemId == "movie-letterboxd-unchanged"))
            .Should().Be(0);
    }

    [Fact]
    public async Task MigrateAsync_AmbiguousOrInvalidRows_UseStableRedactedQuarantineReasons()
    {
        await InsertLegacyRowsAsync();
        MongoLegacyTvMigrationService service = CreateService();

        await service.MigrateAsync(CancellationToken.None);

        IMongoCollection<MongoTvShowDocument> shows =
            database.GetCollection<MongoTvShowDocument>(TvShowsCollection);
        IReadOnlyDictionary<string, MongoTvShowDocument> documents = (await shows
                .Find(document => document.DocumentKind == MongoTvShowDocument.LegacyDocumentKind)
                .ToListAsync())
            .ToDictionary(document => document.LegacyWatchlistItemId!, StringComparer.Ordinal);
        documents["legacy-tv-conflict"].IdentityStatus
            .Should().Be(TvIdentityStatus.LegacyUnresolved);
        documents["legacy-tv-conflict"].TmdbId.Should().Be(304);
        documents["legacy-tv-conflict"].TvdbId.Should().Be(1003);
        documents["legacy-tv-conflict"].LegacyMigrationStatus.Should().Be("quarantined");
        documents["legacy-tv-conflict"].LegacyMigrationReason
            .Should().Be("source_tmdb_conflict");
        documents["legacy-tv-invalid-source"].LegacyMigrationReason
            .Should().Be("source_id_invalid");
        documents["legacy-tv-invalid-source"].TmdbId.Should().Be(777);
        documents["legacy-tv-duplicate-a"].TvdbId.Should().Be(1004);
        documents["legacy-tv-duplicate-b"].TvdbId.Should().Be(1004);
        documents["legacy-tv-duplicate-a"].LegacyMigrationReason
            .Should().Be("duplicate_tvdb_identity");
        documents["legacy-tv-duplicate-b"].LegacyMigrationReason
            .Should().Be("duplicate_tvdb_identity");
        documents.Values.Where(document => document.LegacyMigrationStatus == "quarantined")
            .Should().OnlyContain(document =>
                document.IdentityStatus == TvIdentityStatus.LegacyUnresolved);
        documents.Values.Select(document => document.LegacyMigrationReason)
            .Should().NotContain(reason => reason != null
                && (reason.Contains("not-a-number", StringComparison.Ordinal)
                    || reason.Contains("303", StringComparison.Ordinal)
                    || reason.Contains("304", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MigrateAsync_WhenRepeated_IsInertAndCreatesNoTvAuthority()
    {
        await InsertLegacyRowsAsync();
        MongoLegacyTvMigrationService service = CreateService();
        LegacyTvMigrationResult first = await service.MigrateAsync(CancellationToken.None);
        IMongoCollection<BsonDocument> shows =
            database.GetCollection<BsonDocument>(TvShowsCollection);
        List<BsonDocument> firstDocuments = await shows
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(new BsonDocument("_id", 1))
            .ToListAsync();

        LegacyTvMigrationResult second = await service.MigrateAsync(CancellationToken.None);
        List<BsonDocument> secondDocuments = await shows
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(new BsonDocument("_id", 1))
            .ToListAsync();

        first.MigratedCount.Should().Be(2);
        first.QuarantinedCount.Should().Be(4);
        second.MigratedCount.Should().Be(0);
        second.QuarantinedCount.Should().Be(0);
        secondDocuments.Should().Equal(firstDocuments);
        secondDocuments.Should().HaveCount(6);
        string[] forbiddenAuthorityFields =
        [
            "generationId",
            "traktId",
            "publicId",
            "traktStatus",
            "inWatchlist",
            "airedEpisodes",
            "completedEpisodes",
            "lastWatchedEpisode",
            "nextEpisode",
            "availability",
            "lifecycleState",
            "lastLifecycleEvent",
            "lifecycleVersion",
            "missingScheduledConfirmations",
            "metadataFetchedAt"
        ];
        secondDocuments.Should().OnlyContain(document =>
            forbiddenAuthorityFields.All(field => !document.Contains(field)));
        (await database.GetCollection<BsonDocument>(TvManifestsCollection)
                .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty))
            .Should().Be(0);
        (await database.GetCollection<BsonDocument>(TvEventsCollection)
                .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty))
            .Should().Be(0);
    }

    [Fact]
    public async Task MigrateAsync_WhenMatchingLegacyDocumentAlreadyExists_AcceptsDifferentMigrationTime()
    {
        IMongoCollection<MongoWatchlistItemDocument> source =
            database.GetCollection<MongoWatchlistItemDocument>(WatchlistItemsCollection);
        await source.InsertOneAsync(CreateLegacyTv(
            "legacy-tv-exact",
            "101",
            "Exact Legacy Show",
            101,
            1001,
            "tt0000101",
            genres: ["Drama", "Mystery"],
            originalLanguage: "pl",
            voteAverage: 8.25,
            voteCount: 321));
        DateTimeOffset originalMigrationTime = MigrationTime.AddDays(-1);
        IMongoCollection<MongoTvShowDocument> shows =
            database.GetCollection<MongoTvShowDocument>(TvShowsCollection);
        await shows.InsertOneAsync(new MongoTvShowDocument
        {
            Id = "legacy:legacy-tv-exact",
            DocumentKind = MongoTvShowDocument.LegacyDocumentKind,
            TvdbId = 1001,
            TmdbId = 101,
            ImdbId = "tt0000101",
            IdentityStatus = TvIdentityStatus.LegacyUnresolved,
            Title = "Exact Legacy Show",
            Year = 2020,
            Overview = "Exact legacy overview",
            PosterUrl = "https://example.test/exact-poster.jpg",
            BackdropUrl = "https://example.test/exact-backdrop.jpg",
            AddedAt = DateTimeOffset.Parse("2025-01-02T03:04:05Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
            LegacySourceId = "101",
            LegacyWatchlistItemId = "legacy-tv-exact",
            LegacyMigratedAt = originalMigrationTime,
            LegacyMigrationStatus = "migrated",
            LegacyMigrationReason = "exact_tvdb_identity",
            Genres = ["Drama", "Mystery"],
            OriginalLanguage = "pl",
            TmdbVoteAverage = 8.25,
            TmdbVoteCount = 321
        });

        LegacyTvMigrationResult result = await CreateService().MigrateAsync(
            CancellationToken.None);

        result.MigratedCount.Should().Be(0);
        result.QuarantinedCount.Should().Be(0);
        MongoTvShowDocument stored = await shows.Find(document =>
                document.Id == "legacy:legacy-tv-exact")
            .SingleAsync();
        stored.LegacyMigratedAt.Should().Be(originalMigrationTime);
    }

    [Fact]
    public async Task MigrateAsync_WhenLegacyIdCollidesWithDifferentPayload_FailsClosedAndRedacted()
    {
        IMongoCollection<MongoWatchlistItemDocument> source =
            database.GetCollection<MongoWatchlistItemDocument>(WatchlistItemsCollection);
        await source.InsertOneAsync(CreateLegacyTv(
            "legacy-tv-exact",
            "101",
            "Exact Legacy Show",
            101,
            1001,
            "tt0000101"));
        IMongoCollection<MongoTvShowDocument> shows =
            database.GetCollection<MongoTvShowDocument>(TvShowsCollection);
        await shows.InsertOneAsync(new MongoTvShowDocument
        {
            Id = "legacy:legacy-tv-exact",
            DocumentKind = MongoTvShowDocument.LegacyDocumentKind,
            LegacyWatchlistItemId = "different-sensitive-provenance",
            LegacySourceId = "different-sensitive-source",
            IdentityStatus = TvIdentityStatus.LegacyUnresolved,
            LegacyMigrationStatus = "migrated",
            LegacyMigrationReason = "exact_tvdb_identity"
        });

        Func<Task> action = () => CreateService().MigrateAsync(CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("legacy_tv_migration_collision");
        exception.Message.Should().NotContain("different-sensitive");
    }

    [Fact]
    public async Task MigrateAsync_WhenLegacyCollisionCannotDeserialize_FailsWithStableRedactedCode()
    {
        IMongoCollection<MongoWatchlistItemDocument> source =
            database.GetCollection<MongoWatchlistItemDocument>(WatchlistItemsCollection);
        await source.InsertOneAsync(CreateLegacyTv(
            "legacy-tv-exact",
            "101",
            "Exact Legacy Show",
            101,
            1001,
            "tt0000101"));
        await database.GetCollection<BsonDocument>(TvShowsCollection).InsertOneAsync(
            new BsonDocument
            {
                ["_id"] = "legacy:legacy-tv-exact",
                ["documentKind"] = MongoTvShowDocument.LegacyDocumentKind,
                ["tvdbId"] = "raw-sensitive-invalid-value"
            });

        Func<Task> action = () => CreateService().MigrateAsync(CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("legacy_tv_migration_collision");
        exception.Message.Should().NotContain("raw-sensitive-invalid-value");
    }

    [Fact]
    public async Task HostedService_StartAsync_BlocksUntilMigrationCompletes()
    {
        TaskCompletionSource<LegacyTvMigrationResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingMigrationService migration = new(completion.Task);
        LegacyTvMigrationHostedService hosted = new(
            migration,
            NullLogger<LegacyTvMigrationHostedService>.Instance);

        Task start = hosted.StartAsync(CancellationToken.None);

        start.IsCompleted.Should().BeFalse();
        migration.CallCount.Should().Be(1);
        completion.SetResult(new LegacyTvMigrationResult(3, 2));
        await start;
    }

    [Fact]
    public void AddWatchlistInfrastructure_RegistersBlockingMigrationAfterTvIndexes()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{MongoDbOptions.SectionName}:ConnectionString"] =
                    "mongodb://localhost:27017",
                [$"{MongoDbOptions.SectionName}:DatabaseName"] = databaseName,
                [$"{MongoDbOptions.SectionName}:WatchlistItemsCollectionName"] =
                    WatchlistItemsCollection,
                [$"{MongoDbOptions.SectionName}:SyncRunsCollectionName"] = "sync_runs"
            })
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddWatchlistInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        ILegacyTvMigrationService migration =
            provider.GetRequiredService<ILegacyTvMigrationService>();
        migration.Should().BeOfType<MongoLegacyTvMigrationService>();
        migration.Should().BeSameAs(provider.GetRequiredService<ILegacyTvMigrationService>());
        List<IHostedService> hostedServices = provider.GetServices<IHostedService>().ToList();
        int indexPosition = hostedServices.FindIndex(service => service is MongoTvIndexHostedService);
        int migrationPosition = hostedServices.FindIndex(service =>
            service is LegacyTvMigrationHostedService);
        int bootstrapPosition = hostedServices.FindIndex(service =>
            service is MongoBootstrapHostedService);
        indexPosition.Should().BeGreaterThanOrEqualTo(0);
        migrationPosition.Should().BeGreaterThan(indexPosition);
        bootstrapPosition.Should().BeGreaterThan(migrationPosition);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }

    private MongoLegacyTvMigrationService CreateService()
    {
        return new MongoLegacyTvMigrationService(
            database,
            Options.Create(options),
            new FixedTimeProvider());
    }

    private async Task InsertLegacyRowsAsync()
    {
        IMongoCollection<MongoWatchlistItemDocument> source =
            database.GetCollection<MongoWatchlistItemDocument>(WatchlistItemsCollection);
        await source.InsertManyAsync(
        [
            CreateLegacyTv(
                "legacy-tv-exact",
                "101",
                "Exact Legacy Show",
                101,
                1001,
                "tt0000101",
                genres: ["Drama", "Mystery"],
                originalLanguage: "pl",
                voteAverage: 8.25,
                voteCount: 321),
            CreateLegacyTv("legacy-tv-tmdb-only", "202", "TMDB Only", null, null, null),
            CreateLegacyTv("legacy-tv-conflict", "303", "Conflict", 304, 1003, null),
            CreateLegacyTv(
                "legacy-tv-invalid-source",
                "not-a-number-sensitive-source",
                "Invalid Source",
                777,
                null,
                null),
            CreateLegacyTv("legacy-tv-duplicate-a", "401", "Duplicate A", 401, 1004, null),
            CreateLegacyTv("legacy-tv-duplicate-b", "402", "Duplicate B", 402, 1004, null),
            new MongoWatchlistItemDocument
            {
                Id = "movie-letterboxd-unchanged",
                MediaType = MediaType.Movie,
                Source = WatchlistSource.Letterboxd,
                SourceId = "movie-source",
                Title = "Movie Must Remain",
                ReleaseStatus = ReleaseStatus.Released,
                AvailabilityStatus = AvailabilityStatus.NotOnPlex,
                AddedAt = DateTimeOffset.Parse("2025-02-03T04:05:06Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-02-03T04:05:06Z")
            }
        ]);
    }

    private static MongoWatchlistItemDocument CreateLegacyTv(
        string id,
        string sourceId,
        string title,
        int? tmdbId,
        int? tvdbId,
        string? imdbId,
        IReadOnlyList<string>? genres = null,
        string? originalLanguage = null,
        double? voteAverage = null,
        int? voteCount = null)
    {
        return new MongoWatchlistItemDocument
        {
            Id = id,
            MediaType = MediaType.TvShow,
            Source = WatchlistSource.Tmdb,
            SourceId = sourceId,
            Title = title,
            Year = 2020,
            TmdbId = tmdbId,
            TvdbId = tvdbId,
            ImdbId = imdbId,
            Overview = title == "Exact Legacy Show" ? "Exact legacy overview" : null,
            PosterUrl = title == "Exact Legacy Show"
                ? "https://example.test/exact-poster.jpg"
                : null,
            BackdropUrl = title == "Exact Legacy Show"
                ? "https://example.test/exact-backdrop.jpg"
                : null,
            Genres = genres ?? [],
            OriginalLanguage = originalLanguage,
            TmdbVoteAverage = voteAverage,
            TmdbVoteCount = voteCount,
            ReleaseStatus = ReleaseStatus.Released,
            AvailabilityStatus = AvailabilityStatus.NotOnPlex,
            AddedAt = DateTimeOffset.Parse("2025-01-02T03:04:05Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-02T03:04:05Z")
        };
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => MigrationTime;
    }

    private sealed class BlockingMigrationService(Task<LegacyTvMigrationResult> result)
        : ILegacyTvMigrationService
    {
        public int CallCount { get; private set; }

        public Task<LegacyTvMigrationResult> MigrateAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Watchlist.Application.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
