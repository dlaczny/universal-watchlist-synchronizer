using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoTvIndexHostedServiceTests : IAsyncLifetime
{
    private const string ShowsCollection = "tv_shows";
    private const string ManifestsCollection = "tv_sync_manifests";
    private const string EventsCollection = "tv_lifecycle_events";

    private readonly string databaseName = $"watchlist_tv_indexes_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly IMongoDatabase database;
    private readonly MongoDbOptions options;

    public MongoTvIndexHostedServiceTests()
    {
        database = client.GetDatabase(databaseName);
        options = new MongoDbOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = databaseName,
            TvShowsCollectionName = ShowsCollection,
            TvSyncManifestsCollectionName = ManifestsCollection,
            TvLifecycleEventsCollectionName = EventsCollection
        };
    }

    [Fact]
    public async Task StartAsync_CreatesOnlyRequiredDedicatedTvIndexesAndNoPublishedPointer()
    {
        MongoTvIndexHostedService service = CreateService();

        await service.StartAsync(CancellationToken.None);

        IReadOnlyDictionary<string, BsonDocument> showIndexes = await ReadIndexesAsync(
            ShowsCollection);
        showIndexes.Keys.Should().BeEquivalentTo(
            "_id_",
            "ux_tv_shows_generation_identity",
            "ix_tv_shows_document_kind_tvdb_id",
            "ix_tv_shows_document_kind_tmdb_id");
        BsonDocument identityIndex = showIndexes["ux_tv_shows_generation_identity"];
        identityIndex["unique"].AsBoolean.Should().BeTrue();
        identityIndex["key"].AsBsonDocument.Should().Equal(new BsonDocument
        {
            ["documentKind"] = 1,
            ["generationId"] = 1,
            ["traktId"] = 1
        });
        identityIndex["partialFilterExpression"]["documentKind"].AsString
            .Should().Be("generation");

        IReadOnlyDictionary<string, BsonDocument> manifestIndexes = await ReadIndexesAsync(
            ManifestsCollection);
        manifestIndexes.Keys.Should().BeEquivalentTo(
            "_id_",
            "ux_tv_sync_manifests_generation_id");
        manifestIndexes["ux_tv_sync_manifests_generation_id"]["unique"].AsBoolean
            .Should().BeTrue();
        manifestIndexes["ux_tv_sync_manifests_generation_id"]
            ["partialFilterExpression"]["documentKind"].AsString
            .Should().Be("manifest");

        IReadOnlyDictionary<string, BsonDocument> eventIndexes = await ReadIndexesAsync(
            EventsCollection);
        eventIndexes.Keys.Should().BeEquivalentTo(
            "_id_",
            "ix_tv_lifecycle_events_generation_trakt_version");
        eventIndexes["_id_"]["key"].AsBsonDocument.Should().Equal(
            new BsonDocument("_id", 1));
        eventIndexes["ix_tv_lifecycle_events_generation_trakt_version"]["key"]
            .AsBsonDocument.Should().Equal(new BsonDocument
            {
                ["generationId"] = 1,
                ["traktId"] = 1,
                ["lifecycleVersion"] = 1
            });
        long pointerCount = await database.GetCollection<BsonDocument>(ManifestsCollection)
            .CountDocumentsAsync(new BsonDocument("_id", "published-tv"));
        pointerCount.Should().Be(0);
        IMongoCollection<BsonDocument> events = database.GetCollection<BsonDocument>(
            EventsCollection);
        await events.InsertOneAsync(new BsonDocument("_id", "stable-event"));
        Func<Task> duplicateEvent = () => events.InsertOneAsync(
            new BsonDocument("_id", "stable-event"));
        await duplicateEvent.Should().ThrowAsync<MongoWriteException>();
    }

    [Fact]
    public async Task StartAsync_WhenRepeated_IsIdempotent()
    {
        MongoTvIndexHostedService first = CreateService();
        MongoTvIndexHostedService second = CreateService();

        await first.StartAsync(CancellationToken.None);
        await second.StartAsync(CancellationToken.None);

        (await ReadIndexesAsync(ShowsCollection)).Should().HaveCount(4);
        (await ReadIndexesAsync(ManifestsCollection)).Should().HaveCount(2);
        (await ReadIndexesAsync(EventsCollection)).Should().HaveCount(2);
    }

    [Fact]
    public async Task GenerationIdentityIndex_RejectsDuplicateGenerationRowsButAllowsMultipleLegacyRows()
    {
        await CreateService().StartAsync(CancellationToken.None);
        IMongoCollection<BsonDocument> shows = database.GetCollection<BsonDocument>(ShowsCollection);
        await shows.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "generation:generation-1:1",
            ["documentKind"] = "generation",
            ["generationId"] = "generation-1",
            ["traktId"] = 1
        });

        Func<Task> duplicate = () => shows.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "generation:generation-1:1-copy",
            ["documentKind"] = "generation",
            ["generationId"] = "generation-1",
            ["traktId"] = 1
        });

        await duplicate.Should().ThrowAsync<MongoWriteException>();
        await shows.InsertManyAsync([
            new BsonDocument
            {
                ["_id"] = "legacy:first",
                ["documentKind"] = "legacy",
                ["generationId"] = BsonNull.Value,
                ["traktId"] = BsonNull.Value
            },
            new BsonDocument
            {
                ["_id"] = "legacy:second",
                ["documentKind"] = "legacy",
                ["generationId"] = BsonNull.Value,
                ["traktId"] = BsonNull.Value
            }
        ]);
        long legacyCount = await shows.CountDocumentsAsync(
            new BsonDocument("documentKind", "legacy"));
        legacyCount.Should().Be(2);
    }

    [Fact]
    public async Task PartialAndNonUniqueIndexes_PreservePointerLegacyAndAuditShapes()
    {
        await CreateService().StartAsync(CancellationToken.None);
        IMongoCollection<BsonDocument> manifests = database.GetCollection<BsonDocument>(
            ManifestsCollection);
        await manifests.InsertManyAsync([
            new BsonDocument
            {
                ["_id"] = "generation:generation-shared",
                ["documentKind"] = "manifest",
                ["generationId"] = "generation-shared"
            },
            new BsonDocument
            {
                ["_id"] = "published-tv",
                ["documentKind"] = "pointer",
                ["generationId"] = "generation-shared"
            }
        ]);

        IMongoCollection<BsonDocument> shows = database.GetCollection<BsonDocument>(ShowsCollection);
        await shows.InsertManyAsync([
            new BsonDocument
            {
                ["_id"] = "generation:generation-a:11",
                ["documentKind"] = "generation",
                ["generationId"] = "generation-a",
                ["traktId"] = 11,
                ["tvdbId"] = 900,
                ["tmdbId"] = 901
            },
            new BsonDocument
            {
                ["_id"] = "generation:generation-b:12",
                ["documentKind"] = "generation",
                ["generationId"] = "generation-b",
                ["traktId"] = 12,
                ["tvdbId"] = 900,
                ["tmdbId"] = 901
            }
        ]);

        IMongoCollection<BsonDocument> events = database.GetCollection<BsonDocument>(EventsCollection);
        await events.InsertManyAsync([
            new BsonDocument
            {
                ["_id"] = "event-a",
                ["generationId"] = "generation-shared",
                ["traktId"] = 11,
                ["lifecycleVersion"] = 2
            },
            new BsonDocument
            {
                ["_id"] = "event-b",
                ["generationId"] = "generation-shared",
                ["traktId"] = 11,
                ["lifecycleVersion"] = 2
            }
        ]);

        (await manifests.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty))
            .Should().Be(2);
        (await shows.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty))
            .Should().Be(2);
        (await events.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty))
            .Should().Be(2);
    }

    [Fact]
    public void AddWatchlistInfrastructure_RegistersTvRepositoriesAndStartsIndexBootstrapBeforeMongoBootstrap()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{MongoDbOptions.SectionName}:ConnectionString"] =
                    "mongodb://localhost:27017",
                [$"{MongoDbOptions.SectionName}:DatabaseName"] = databaseName,
                [$"{MongoDbOptions.SectionName}:WatchlistItemsCollectionName"] =
                    "watchlist_items",
                [$"{MongoDbOptions.SectionName}:SyncRunsCollectionName"] = "sync_runs"
            })
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddWatchlistInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        ITvGenerationRepository generationRepository =
            provider.GetRequiredService<ITvGenerationRepository>();
        ITvShowReadRepository readRepository =
            provider.GetRequiredService<ITvShowReadRepository>();
        generationRepository.Should().BeOfType<MongoTvGenerationRepository>();
        generationRepository.Should().BeSameAs(
            provider.GetRequiredService<ITvGenerationRepository>());
        readRepository.Should().BeOfType<MongoTvShowReadRepository>();
        readRepository.Should().BeSameAs(provider.GetRequiredService<ITvShowReadRepository>());
        List<IHostedService> hostedServices = provider.GetServices<IHostedService>().ToList();
        int indexPosition = hostedServices.FindIndex(service => service is MongoTvIndexHostedService);
        int mongoBootstrapPosition = hostedServices.FindIndex(
            service => service is MongoBootstrapHostedService);
        indexPosition.Should().BeGreaterThanOrEqualTo(0);
        mongoBootstrapPosition.Should().BeGreaterThan(indexPosition);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }

    private MongoTvIndexHostedService CreateService()
    {
        return new MongoTvIndexHostedService(database, Options.Create(options));
    }

    private async Task<IReadOnlyDictionary<string, BsonDocument>> ReadIndexesAsync(
        string collectionName)
    {
        using IAsyncCursor<BsonDocument> cursor = await database
            .GetCollection<BsonDocument>(collectionName)
            .Indexes.ListAsync();
        List<BsonDocument> indexes = await cursor.ToListAsync();
        return indexes.ToDictionary(index => index["name"].AsString, StringComparer.Ordinal);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Watchlist.Application.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
