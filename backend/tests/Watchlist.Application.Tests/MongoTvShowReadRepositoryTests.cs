using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoTvShowReadRepositoryTests : IAsyncLifetime
{
    private const string ShowsCollection = "tv_shows";
    private const string ManifestsCollection = "tv_sync_manifests";
    private const string EventsCollection = "tv_lifecycle_events";

    private readonly string databaseName = $"watchlist_tv_reads_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly IMongoDatabase database;
    private readonly MongoDbOptions options;

    public MongoTvShowReadRepositoryTests()
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
    public async Task GetPublishedAsync_WhenNoPointerExists_ReturnsNullWithoutFallingBackToRows()
    {
        TvGenerationDraft staged = TvMongoGenerationTestData.CreateDraft(
            "generation-staged-only",
            1101,
            "Staged");
        MongoTvGenerationRepository writer = CreateWriter();
        await writer.StageAsync(staged, CancellationToken.None);
        MongoTvShowReadRepository reader = CreateReader();

        PublishedTvGeneration? result = await reader.GetPublishedAsync(CancellationToken.None);
        Watchlist.Domain.TvShow? show = await reader.GetPublishedShowAsync(
            staged.Shows[0].Id,
            CancellationToken.None);

        result.Should().BeNull();
        show.Should().BeNull();
    }

    [Fact]
    public async Task GetPublishedShowAsync_ReturnsOnlyExactShowFromPublishedGeneration()
    {
        TvGenerationDraft publishedDraft = TvMongoGenerationTestData.CreateDraft(
            "generation-visible",
            1201,
            "Visible");
        TvGenerationDraft stagedDraft = TvMongoGenerationTestData.CreateDraft(
            "generation-hidden",
            1202,
            "Hidden",
            TvMongoGenerationTestData.BaseTime.AddHours(1));
        MongoTvGenerationRepository writer = CreateWriter();
        await writer.StageAsync(publishedDraft, CancellationToken.None);
        await writer.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(publishedDraft),
            CancellationToken.None);
        await writer.StageAsync(stagedDraft, CancellationToken.None);
        MongoTvShowReadRepository reader = CreateReader();

        Watchlist.Domain.TvShow? visible = await reader.GetPublishedShowAsync(
            publishedDraft.Shows[0].Id,
            CancellationToken.None);
        Watchlist.Domain.TvShow? hidden = await reader.GetPublishedShowAsync(
            stagedDraft.Shows[0].Id,
            CancellationToken.None);

        visible.Should().BeEquivalentTo(
            publishedDraft.Shows[0],
            assertion => assertion.WithStrictOrdering());
        hidden.Should().BeNull();
    }

    [Fact]
    public async Task GetPublishedAsync_WhenPointerTargetsMissingManifest_ThrowsTypedFailure()
    {
        await database.GetCollection<BsonDocument>(ManifestsCollection).InsertOneAsync(
            new BsonDocument
            {
                ["_id"] = "published-tv",
                ["documentKind"] = "pointer",
                ["generationId"] = "missing-generation",
                ["manifestId"] = "generation:missing-generation",
                ["showCount"] = 0,
                ["lifecycleEventCount"] = 0,
                ["membershipHash"] = new string('0', 64),
                ["progressHash"] = new string('0', 64)
            });
        MongoTvShowReadRepository reader = CreateReader();

        Func<Task> action = () => reader.GetPublishedAsync(CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_manifest_missing");
    }

    [Fact]
    public async Task GetPublishedAsync_WhenPointerContainsNullWitness_ThrowsTypedFailure()
    {
        await database.GetCollection<BsonDocument>(ManifestsCollection).InsertOneAsync(
            new BsonDocument
            {
                ["_id"] = "published-tv",
                ["documentKind"] = "pointer",
                ["generationId"] = "generation-null-witness",
                ["manifestId"] = "generation:generation-null-witness",
                ["showCount"] = 0,
                ["lifecycleEventCount"] = 0,
                ["membershipHash"] = BsonNull.Value,
                ["progressHash"] = new string('0', 64)
            });
        MongoTvShowReadRepository reader = CreateReader();

        Func<Task> action = () => reader.GetPublishedAsync(CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_pointer_invalid");
        exception.Message.Should().Be("The published TV generation is invalid.");
    }

    [Fact]
    public async Task GetPublishedAsync_WhenPointerBsonTypeCannotDeserialize_ThrowsTypedFailure()
    {
        await database.GetCollection<BsonDocument>(ManifestsCollection).InsertOneAsync(
            new BsonDocument
            {
                ["_id"] = "published-tv",
                ["documentKind"] = "pointer",
                ["generationId"] = "generation-malformed-pointer",
                ["manifestId"] = "generation:generation-malformed-pointer",
                ["showCount"] = "not-an-integer",
                ["lifecycleEventCount"] = 0,
                ["membershipHash"] = new string('0', 64),
                ["progressHash"] = new string('0', 64)
            });
        MongoTvShowReadRepository reader = CreateReader();

        Func<Task> action = () => reader.GetPublishedAsync(CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_document_invalid");
    }

    [Fact]
    public async Task GetPublishedAsync_WhenPublishedRowCountDisagrees_ThrowsTypedFailure()
    {
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-row-count",
            1301,
            "Row Count");
        MongoTvGenerationRepository writer = CreateWriter();
        await writer.StageAsync(draft, CancellationToken.None);
        await writer.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);
        await database.GetCollection<BsonDocument>(ShowsCollection)
            .DeleteOneAsync(new BsonDocument("_id", $"generation:{draft.GenerationId}:1301"));
        MongoTvShowReadRepository reader = CreateReader();

        Func<Task> action = () => reader.GetPublishedAsync(CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_row_count_invalid");
    }

    [Fact]
    public async Task GetPublishedAsync_WhenPointerWitnessDisagreesWithManifest_ThrowsTypedFailure()
    {
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-pointer-witness",
            1401,
            "Witness");
        MongoTvGenerationRepository writer = CreateWriter();
        await writer.StageAsync(draft, CancellationToken.None);
        await writer.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);
        await database.GetCollection<BsonDocument>(ManifestsCollection).UpdateOneAsync(
            new BsonDocument("_id", "published-tv"),
            new BsonDocument("$set", new BsonDocument("showCount", 2)));
        MongoTvShowReadRepository reader = CreateReader();

        Func<Task> action = () => reader.GetPublishedAsync(CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_pointer_invalid");
    }

    [Fact]
    public async Task GetPublishedAsync_WhenStoredShowChangesAfterPublication_RejectsHashMismatch()
    {
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-hash-mismatch",
            1501,
            "Original");
        MongoTvGenerationRepository writer = CreateWriter();
        await writer.StageAsync(draft, CancellationToken.None);
        await writer.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);
        await database.GetCollection<BsonDocument>(ShowsCollection).UpdateOneAsync(
            new BsonDocument("_id", $"generation:{draft.GenerationId}:1501"),
            new BsonDocument("$set", new BsonDocument("inWatchlist", false)));
        MongoTvShowReadRepository reader = CreateReader();

        Func<Task> action = () => reader.GetPublishedAsync(CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_hash_invalid");
    }

    [Fact]
    public async Task GetPublishedAsync_WhenNestedEpisodeIsNull_ThrowsTypedRowFailure()
    {
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-null-episode",
            1551,
            "Null Episode");
        MongoTvGenerationRepository writer = CreateWriter();
        await writer.StageAsync(draft, CancellationToken.None);
        await writer.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);
        await database.GetCollection<BsonDocument>(ShowsCollection).UpdateOneAsync(
            new BsonDocument("_id", $"generation:{draft.GenerationId}:1551"),
            new BsonDocument("$set", new BsonDocument("seasons.0.episodes.0", BsonNull.Value)));
        MongoTvShowReadRepository reader = CreateReader();

        Func<Task> action = () => reader.GetPublishedAsync(CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_row_invalid");
    }

    [Fact]
    public async Task GetPublishedAsync_WhenLifecycleEventPhysicalIdIsInvalid_ThrowsTypedEventFailure()
    {
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-invalid-event-physical-id",
            1571,
            "Invalid Event Identity");
        MongoTvGenerationRepository writer = CreateWriter();
        await writer.StageAsync(draft, CancellationToken.None);
        await writer.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);
        IMongoCollection<BsonDocument> events = database.GetCollection<BsonDocument>(
            EventsCollection);
        BsonDocument malformed = await events
            .Find(new BsonDocument("eventId", draft.LifecycleEvents[0].Id))
            .SingleAsync();
        await events.DeleteOneAsync(new BsonDocument("_id", malformed["_id"]));
        malformed["_id"] = "noncanonical-sensitive-physical-id";
        await events.InsertOneAsync(malformed);
        MongoTvShowReadRepository reader = CreateReader();

        Func<Task> action = () => reader.GetPublishedAsync(CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_lifecycle_events_invalid");
        exception.Message.Should().Be("The published TV generation is invalid.");
        exception.Message.Should().NotContain("noncanonical-sensitive-physical-id");
    }

    [Fact]
    public async Task GetPublishedAsync_WhenPointerChangesDuringRead_UsesCapturedGenerationOnly()
    {
        TvGenerationDraft first = TvMongoGenerationTestData.CreateDraft(
            "generation-coherent-1",
            1601,
            "First");
        TvGenerationDraft second = TvMongoGenerationTestData.CreateDraft(
            "generation-coherent-2",
            1602,
            "Second",
            TvMongoGenerationTestData.BaseTime.AddHours(1));
        MongoTvGenerationRepository writer = CreateWriter();
        await writer.StageAsync(first, CancellationToken.None);
        await writer.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(first),
            CancellationToken.None);
        await writer.StageAsync(second, CancellationToken.None);

        TaskCompletionSource manifestReadStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ManualResetEventSlim allowManifestRead = new(initialState: false);
        int manifestCollectionFinds = 0;
        MongoClientSettings monitoredSettings = MongoClientSettings.FromConnectionString(
            "mongodb://localhost:27017");
        monitoredSettings.ClusterConfigurator = cluster =>
            cluster.Subscribe<CommandStartedEvent>(command =>
            {
                if (!string.Equals(command.CommandName, "find", StringComparison.Ordinal)
                    || !command.Command.TryGetValue("find", out BsonValue? collection)
                    || !string.Equals(collection.AsString, ManifestsCollection, StringComparison.Ordinal))
                {
                    return;
                }

                int currentFind = Interlocked.Increment(ref manifestCollectionFinds);
                if (currentFind == 2)
                {
                    manifestReadStarted.TrySetResult();
                    allowManifestRead.Wait(TimeSpan.FromSeconds(10));
                }
            });
        MongoClient monitoredClient = new(monitoredSettings);
        MongoTvShowReadRepository reader = new(
            monitoredClient.GetDatabase(databaseName),
            Options.Create(options));

        Task<PublishedTvGeneration?> readTask = reader.GetPublishedAsync(CancellationToken.None);
        await manifestReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await writer.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(second, first.GenerationId),
            CancellationToken.None);
        allowManifestRead.Set();

        PublishedTvGeneration result = (await readTask)!;
        result.Manifest.GenerationId.Should().Be(first.GenerationId);
        result.Shows.Should().ContainSingle().Which.TraktId.Should().Be(1601);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }

    private MongoTvGenerationRepository CreateWriter()
    {
        return new MongoTvGenerationRepository(database, Options.Create(options));
    }

    private MongoTvShowReadRepository CreateReader()
    {
        return new MongoTvShowReadRepository(database, Options.Create(options));
    }
}
