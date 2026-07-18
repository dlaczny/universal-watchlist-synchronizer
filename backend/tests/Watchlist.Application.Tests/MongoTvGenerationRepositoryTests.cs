using FluentAssertions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using Watchlist.Application;
using Watchlist.Domain;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoTvGenerationRepositoryTests : IAsyncLifetime
{
    private const string ShowsCollection = "tv_shows";
    private const string ManifestsCollection = "tv_sync_manifests";
    private const string EventsCollection = "tv_lifecycle_events";

    private readonly string databaseName = $"watchlist_tv_generation_{Guid.NewGuid():N}";
    private readonly MongoClient client = new("mongodb://localhost:27017");
    private readonly IMongoDatabase database;
    private readonly MongoDbOptions options;

    public MongoTvGenerationRepositoryTests()
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
    public async Task StageAndPublishAsync_TwoGenerations_PublishesLastAndRoundTripsEveryField()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft first = TvMongoGenerationTestData.CreateDraft(
            "generation-1",
            101,
            "First Show");
        TvGenerationManifest firstManifest = TvMongoGenerationTestData.CreateManifest(first);
        TvGenerationDraft second = TvMongoGenerationTestData.CreateDraft(
            "generation-2",
            202,
            "Second Show",
            TvMongoGenerationTestData.BaseTime.AddHours(1));
        TvGenerationManifest secondManifest = TvMongoGenerationTestData.CreateManifest(
            second,
            first.GenerationId);

        await repository.StageAsync(first, CancellationToken.None);
        (await repository.GetPublishedAsync(CancellationToken.None)).Should().BeNull();
        await repository.PublishAsync(firstManifest, CancellationToken.None);

        PublishedTvGeneration firstPublished = (await repository.GetPublishedAsync(
            CancellationToken.None))!;
        firstPublished.Manifest.Should().BeEquivalentTo(
            firstManifest,
            assertion => assertion.WithStrictOrdering());
        firstPublished.Shows.Should().BeEquivalentTo(
            first.Shows,
            assertion => assertion.WithStrictOrdering());

        await repository.StageAsync(second, CancellationToken.None);
        PublishedTvGeneration stillFirst = (await repository.GetPublishedAsync(
            CancellationToken.None))!;
        stillFirst.Manifest.GenerationId.Should().Be(first.GenerationId);
        stillFirst.Shows.Should().ContainSingle().Which.TraktId.Should().Be(101);

        await repository.PublishAsync(secondManifest, CancellationToken.None);

        PublishedTvGeneration published = (await repository.GetPublishedAsync(
            CancellationToken.None))!;
        published.Manifest.Should().BeEquivalentTo(
            secondManifest,
            assertion => assertion.WithStrictOrdering());
        published.Shows.Should().BeEquivalentTo(
            second.Shows,
            assertion => assertion.WithStrictOrdering());
        TvShow show = published.Shows.Should().ContainSingle().Subject;
        show.SpecialEpisodeIdentities.Should().ContainSingle().Which.Should().Be(
            second.Shows[0].SpecialEpisodeIdentities[0]);
        show.Seasons.Should().OnlyContain(season => season.SeasonNumber > 0);
        show.Seasons.SelectMany(season => season.Episodes)
            .Should().NotContain(episode => episode.SeasonNumber == 0);
        show.Availability.Offers.Should().Equal(second.Shows[0].Availability.Offers);
        show.Availability.Offers.Select(offer => offer.Category).Should()
            .Equal(Enum.GetValues<TvProviderCategory>());

        BsonDocument pointer = await ReadPointerAsync();
        pointer["generationId"].AsString.Should().Be("generation-2");
        pointer["manifestId"].AsString.Should().Be("generation:generation-2");
        pointer["showCount"].AsInt32.Should().Be(1);
        BsonDocument storedShow = await database.GetCollection<BsonDocument>(ShowsCollection)
            .Find(new BsonDocument("_id", "generation:generation-2:202"))
            .SingleAsync();
        storedShow["documentKind"].AsString.Should().Be("generation");
        storedShow["legacySourceId"].AsString.Should().Be("legacy-source-202");
        storedShow["specialEpisodeIdentities"].AsBsonArray.Should().ContainSingle();
    }

    [Fact]
    public async Task PublishAsync_WithoutStagedRows_PersistsNeitherManifestNorPointer()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-missing",
            303,
            "Missing");
        TvGenerationManifest manifest = TvMongoGenerationTestData.CreateManifest(draft);

        Func<Task> action = () => repository.PublishAsync(manifest, CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_staged_rows_invalid");
        long documentCount = await database.GetCollection<BsonDocument>(ManifestsCollection)
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        documentCount.Should().Be(0);
    }

    [Fact]
    public async Task StageAsync_WhenGenerationAndTraktIdentityAlreadyExists_RejectsDuplicateRow()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-duplicate",
            404,
            "Duplicate");

        await repository.StageAsync(draft, CancellationToken.None);
        Func<Task> action = () => repository.StageAsync(draft, CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_row_conflict");
        long rowCount = await database.GetCollection<BsonDocument>(ShowsCollection)
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        rowCount.Should().Be(1);
    }

    [Fact]
    public async Task StageAsync_WhenSamePhysicalRowHasDifferentPayload_RejectsWithoutOverwrite()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft original = TvMongoGenerationTestData.CreateDraft(
            "generation-row-immutable",
            454,
            "Original Title");
        TvGenerationDraft changed = original with
        {
            Shows = [original.Shows[0] with { Title = "Changed Title" }]
        };
        TvSnapshotValidator validator = new();
        changed = changed with
        {
            MembershipHash = validator.ComputeMembershipHash(changed.Shows),
            ProgressHash = validator.ComputeProgressHash(changed.Shows)
        };
        await repository.StageAsync(original, CancellationToken.None);

        Func<Task> action = () => repository.StageAsync(changed, CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_row_conflict");
        MongoTvShowDocument stored = await database.GetCollection<MongoTvShowDocument>(
                ShowsCollection)
            .Find(document => document.Id == "generation:generation-row-immutable:454")
            .SingleAsync();
        stored.Title.Should().Be("Original Title");
    }

    [Fact]
    public async Task StageAsync_WhenIdenticalStableLifecycleEventAlreadyExists_IsIdempotent()
    {
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-event-retry",
            505,
            "Event Retry");
        MongoTvLifecycleEventDocument existing = MongoTvLifecycleEventDocument.FromDomain(
            draft.LifecycleEvents[0]);
        await database.GetCollection<MongoTvLifecycleEventDocument>(EventsCollection)
            .InsertOneAsync(existing);
        MongoTvGenerationRepository repository = CreateRepository();

        await repository.StageAsync(draft, CancellationToken.None);

        long eventCount = await database.GetCollection<BsonDocument>(EventsCollection)
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        eventCount.Should().Be(1);
        await repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);
        (await repository.GetPublishedAsync(CancellationToken.None))!
            .Manifest.LifecycleEventIds.Should().Equal(existing.EventId);
    }

    [Fact]
    public async Task StageAndPublishAsync_WhenPriorGenerationWasAbandoned_PersistsDistinctImmutableEventCandidate()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft abandoned = TvMongoGenerationTestData.CreateDraft(
            "generation-event-abandoned",
            555,
            "Retry Event");
        TvGenerationDraft retry = TvMongoGenerationTestData.CreateDraft(
            "generation-event-retry",
            555,
            "Retry Event",
            TvMongoGenerationTestData.BaseTime.AddHours(1));
        abandoned.LifecycleEvents[0].Id.Should().Be(retry.LifecycleEvents[0].Id);
        abandoned.LifecycleEvents[0].OccurredAt.Should()
            .NotBe(retry.LifecycleEvents[0].OccurredAt);
        await repository.StageAsync(abandoned, CancellationToken.None);
        BsonDocument abandonedEventBefore = await database
            .GetCollection<BsonDocument>(EventsCollection)
            .Find(new BsonDocument("generationId", abandoned.GenerationId))
            .SingleAsync();

        await repository.StageAsync(retry, CancellationToken.None);
        await repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(retry),
            CancellationToken.None);

        PublishedTvGeneration published = (await repository.GetPublishedAsync(
            CancellationToken.None))!;
        published.Manifest.GenerationId.Should().Be(retry.GenerationId);
        published.Manifest.LifecycleEventIds.Should().Equal(retry.LifecycleEvents[0].Id);
        published.Shows.Should().ContainSingle().Which.GenerationId.Should()
            .Be(retry.GenerationId);
        List<BsonDocument> eventDocuments = await database
            .GetCollection<BsonDocument>(EventsCollection)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(new BsonDocument("generationId", 1))
            .ToListAsync();
        eventDocuments.Should().HaveCount(2);
        eventDocuments.Select(document => document["eventId"].AsString)
            .Should().OnlyContain(eventId => eventId == retry.LifecycleEvents[0].Id);
        eventDocuments.Select(document => document["_id"].AsString).Should().BeEquivalentTo(
            $"generation:{abandoned.GenerationId}:{abandoned.LifecycleEvents[0].Id}",
            $"generation:{retry.GenerationId}:{retry.LifecycleEvents[0].Id}");
        eventDocuments.Single(document =>
                document["generationId"].AsString == abandoned.GenerationId)
            .Should().BeEquivalentTo(abandonedEventBefore);
    }

    [Fact]
    public async Task StageAsync_WhenStableLifecycleEventPayloadConflicts_RejectsWithoutOverwrite()
    {
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-event-conflict",
            606,
            "Event Conflict");
        MongoTvLifecycleEventDocument conflicting = MongoTvLifecycleEventDocument.FromDomain(
            draft.LifecycleEvents[0]) with
        {
            Reason = "different_stable_reason"
        };
        await database.GetCollection<MongoTvLifecycleEventDocument>(EventsCollection)
            .InsertOneAsync(conflicting);
        MongoTvGenerationRepository repository = CreateRepository();

        Func<Task> action = () => repository.StageAsync(draft, CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_lifecycle_event_conflict");
        MongoTvLifecycleEventDocument stored = await database
            .GetCollection<MongoTvLifecycleEventDocument>(EventsCollection)
            .Find(document => document.Id == conflicting.Id)
            .SingleAsync();
        stored.Should().BeEquivalentTo(conflicting, assertion => assertion.WithStrictOrdering());
        (await repository.GetPublishedAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task StageAsync_WhenExistingLifecycleEventCannotDeserialize_RejectsStableRedactedConflict()
    {
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-malformed-existing-event",
            656,
            "Malformed Existing Event");
        BsonDocument malformed = new()
        {
            ["_id"] = MongoTvLifecycleEventDocument.FromDomain(draft.LifecycleEvents[0]).Id,
            ["eventId"] = draft.LifecycleEvents[0].Id,
            ["traktId"] = "raw-sensitive-upstream-value",
            ["lifecycleVersion"] = 1,
            ["generationId"] = draft.GenerationId,
            ["eventType"] = "added"
        };
        await database.GetCollection<BsonDocument>(EventsCollection).InsertOneAsync(malformed);
        MongoTvGenerationRepository repository = CreateRepository();

        Func<Task> action = () => repository.StageAsync(draft, CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_lifecycle_event_conflict");
        exception.Message.Should().NotContain("raw-sensitive-upstream-value");
        BsonDocument stored = await database.GetCollection<BsonDocument>(EventsCollection)
            .Find(new BsonDocument("_id", malformed["_id"]))
            .SingleAsync();
        stored.Equals(malformed).Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_WhenManifestGenerationAlreadyExistsWithDifferentPayload_RejectsImmutably()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-immutable",
            707,
            "Immutable");
        TvGenerationManifest manifest = TvMongoGenerationTestData.CreateManifest(draft);
        await repository.StageAsync(draft, CancellationToken.None);
        await repository.PublishAsync(manifest, CancellationToken.None);
        BsonDocument before = await ReadManifestAsync(manifest.GenerationId);
        TvGenerationManifest conflicting = manifest with
        {
            EnrichmentErrors = ["different_enrichment_error"]
        };

        Func<Task> action = () => repository.PublishAsync(conflicting, CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_manifest_conflict");
        BsonDocument after = await ReadManifestAsync(manifest.GenerationId);
        after.Equals(before).Should().BeTrue();
        BsonDocument pointer = await ReadPointerAsync();
        pointer["generationId"].AsString.Should().Be(manifest.GenerationId);
    }

    [Fact]
    public async Task PublishAsync_ExactSameManifestRetry_IsIdempotent()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-idempotent",
            808,
            "Idempotent");
        TvGenerationManifest manifest = TvMongoGenerationTestData.CreateManifest(draft);
        await repository.StageAsync(draft, CancellationToken.None);
        await repository.PublishAsync(manifest, CancellationToken.None);

        await repository.PublishAsync(manifest, CancellationToken.None);

        long manifestCount = await database.GetCollection<BsonDocument>(ManifestsCollection)
            .CountDocumentsAsync(new BsonDocument("documentKind", "manifest"));
        manifestCount.Should().Be(1);
        (await repository.GetPublishedAsync(CancellationToken.None))!
            .Manifest.Should().BeEquivalentTo(manifest, assertion => assertion.WithStrictOrdering());
    }

    [Fact]
    public async Task PublishAsync_WhenGenerationContainsUnmanifestedEvent_RejectsBeforePointer()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-extra-event",
            858,
            "Extra Event");
        await repository.StageAsync(draft, CancellationToken.None);
        MongoTvLifecycleEventDocument extra = MongoTvLifecycleEventDocument.FromDomain(
            draft.LifecycleEvents[0] with
            {
                Id = "tv:858:2:reactivated",
                Version = 2,
                EventType = "reactivated"
            });
        await database.GetCollection<MongoTvLifecycleEventDocument>(EventsCollection)
            .InsertOneAsync(extra);

        Func<Task> action = () => repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_staged_events_invalid");
        (await repository.GetPublishedAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_WhenGenerationContainsUnmanifestedRow_RejectsBeforePointer()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-extra-row",
            868,
            "Expected Row");
        await repository.StageAsync(draft, CancellationToken.None);
        TvGenerationDraft extraDraft = TvMongoGenerationTestData.CreateDraft(
            draft.GenerationId,
            869,
            "Unexpected Row");
        await database.GetCollection<MongoTvShowDocument>(ShowsCollection).InsertOneAsync(
            MongoTvShowDocument.FromDomain(extraDraft.Shows[0]));

        Func<Task> action = () => repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_staged_rows_invalid");
        (await repository.GetPublishedAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_WhenStagedRowCannotDeserialize_RejectsWithStableRedactedFailure()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-malformed-row",
            878,
            "Malformed Row");
        await repository.StageAsync(draft, CancellationToken.None);
        await database.GetCollection<BsonDocument>(ShowsCollection).UpdateOneAsync(
            new BsonDocument("_id", "generation:generation-malformed-row:878"),
            new BsonDocument("$set", new BsonDocument(
                "airedEpisodes",
                "raw-sensitive-upstream-value")));

        Func<Task> action = () => repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_staged_rows_invalid");
        exception.Message.Should().NotContain("raw-sensitive-upstream-value");
        (await repository.GetPublishedAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_WhenStagedLifecycleEventCannotDeserialize_RejectsStableRedactedFailure()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-malformed-event",
            888,
            "Malformed Event");
        await repository.StageAsync(draft, CancellationToken.None);
        await database.GetCollection<BsonDocument>(EventsCollection).UpdateOneAsync(
            new BsonDocument("eventId", draft.LifecycleEvents[0].Id),
            new BsonDocument("$set", new BsonDocument(
                "lifecycleVersion",
                "raw-sensitive-upstream-value")));

        Func<Task> action = () => repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_staged_events_invalid");
        exception.Message.Should().NotContain("raw-sensitive-upstream-value");
        (await repository.GetPublishedAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_WhenStagedLifecycleEventPhysicalIdIsInvalid_RejectsStableFailure()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-invalid-event-physical-id",
            887,
            "Invalid Event Identity");
        await repository.StageAsync(draft, CancellationToken.None);
        IMongoCollection<BsonDocument> events = database.GetCollection<BsonDocument>(
            EventsCollection);
        BsonDocument malformed = await events
            .Find(new BsonDocument("eventId", draft.LifecycleEvents[0].Id))
            .SingleAsync();
        await events.DeleteOneAsync(new BsonDocument("_id", malformed["_id"]));
        malformed["_id"] = "noncanonical-sensitive-physical-id";
        await events.InsertOneAsync(malformed);

        Func<Task> action = () => repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_staged_events_invalid");
        exception.Message.Should().NotContain("noncanonical-sensitive-physical-id");
        (await repository.GetPublishedAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_WhenCurrentPointerCannotDeserialize_RejectsTypedFailureWithoutOverwrite()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-malformed-current-pointer",
            889,
            "Malformed Current Pointer");
        await repository.StageAsync(draft, CancellationToken.None);
        BsonDocument malformedPointer = new()
        {
            ["_id"] = "published-tv",
            ["documentKind"] = "pointer",
            ["generationId"] = "generation-existing",
            ["manifestId"] = "generation:generation-existing",
            ["showCount"] = "raw-sensitive-upstream-value",
            ["lifecycleEventCount"] = 0,
            ["membershipHash"] = new string('0', 64),
            ["progressHash"] = new string('0', 64)
        };
        await database.GetCollection<BsonDocument>(ManifestsCollection)
            .InsertOneAsync(malformedPointer);

        Func<Task> action = () => repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_document_invalid");
        exception.Message.Should().Be("The published TV generation is invalid.");
        exception.Message.Should().NotContain("raw-sensitive-upstream-value");
        BsonDocument stored = await ReadPointerAsync();
        stored.Equals(malformedPointer).Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_WhenCurrentPointerShapeIsInvalid_RejectsTypedFailureWithoutOverwrite()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-invalid-current-pointer",
            890,
            "Invalid Current Pointer");
        await repository.StageAsync(draft, CancellationToken.None);
        BsonDocument invalidPointer = new()
        {
            ["_id"] = "published-tv",
            ["documentKind"] = "not-a-pointer",
            ["generationId"] = BsonNull.Value,
            ["manifestId"] = BsonNull.Value,
            ["showCount"] = 0,
            ["lifecycleEventCount"] = 0,
            ["membershipHash"] = new string('0', 64),
            ["progressHash"] = new string('0', 64)
        };
        await database.GetCollection<BsonDocument>(ManifestsCollection)
            .InsertOneAsync(invalidPointer);

        Func<Task> action = () => repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_pointer_invalid");
        BsonDocument stored = await ReadPointerAsync();
        stored.Equals(invalidPointer).Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_WhenExistingManifestCannotDeserialize_RejectsStableRedactedConflict()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-malformed-manifest",
            891,
            "Malformed Manifest");
        await repository.StageAsync(draft, CancellationToken.None);
        BsonDocument malformedManifest = new()
        {
            ["_id"] = $"generation:{draft.GenerationId}",
            ["documentKind"] = "manifest",
            ["generationId"] = draft.GenerationId,
            ["showCount"] = "raw-sensitive-upstream-value"
        };
        await database.GetCollection<BsonDocument>(ManifestsCollection)
            .InsertOneAsync(malformedManifest);

        Func<Task> action = () => repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(draft),
            CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_manifest_conflict");
        exception.Message.Should().NotContain("raw-sensitive-upstream-value");
        (await repository.GetPublishedAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task PublishAsync_WithStalePreviousGeneration_RejectsAndPreservesPointer()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft first = TvMongoGenerationTestData.CreateDraft(
            "generation-cas-1",
            901,
            "First");
        await repository.StageAsync(first, CancellationToken.None);
        await repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(first),
            CancellationToken.None);
        TvGenerationDraft second = TvMongoGenerationTestData.CreateDraft(
            "generation-cas-2",
            902,
            "Second",
            TvMongoGenerationTestData.BaseTime.AddHours(1));
        await repository.StageAsync(second, CancellationToken.None);
        TvGenerationManifest stale = TvMongoGenerationTestData.CreateManifest(
            second,
            "another-generation");

        Func<Task> action = () => repository.PublishAsync(stale, CancellationToken.None);

        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_previous_pointer_conflict");
        BsonDocument pointer = await ReadPointerAsync();
        pointer["generationId"].AsString.Should().Be(first.GenerationId);
    }

    [Fact]
    public async Task PublishAsync_WhenCurrentPointerDisagreesWithItsManifest_RejectsBeforeTargetManifest()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft first = TvMongoGenerationTestData.CreateDraft(
            "generation-incoherent-current-1",
            951,
            "Published");
        await repository.StageAsync(first, CancellationToken.None);
        await repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(first),
            CancellationToken.None);
        string corruptedHash = new('f', 64);
        await database.GetCollection<BsonDocument>(ManifestsCollection).UpdateOneAsync(
            new BsonDocument("_id", "published-tv"),
            new BsonDocument("$set", new BsonDocument("membershipHash", corruptedHash)));
        TvGenerationDraft next = TvMongoGenerationTestData.CreateDraft(
            "generation-incoherent-current-2",
            952,
            "Next",
            TvMongoGenerationTestData.BaseTime.AddHours(1));
        await repository.StageAsync(next, CancellationToken.None);

        Func<Task> action = () => repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(next, first.GenerationId),
            CancellationToken.None);

        TvPublishedGenerationInvalidException exception = (await action.Should()
            .ThrowAsync<TvPublishedGenerationInvalidException>()).Which;
        exception.Code.Should().Be("tv_published_pointer_invalid");
        BsonDocument pointer = await ReadPointerAsync();
        pointer["generationId"].AsString.Should().Be(first.GenerationId);
        pointer["membershipHash"].AsString.Should().Be(corruptedHash);
        long targetManifestCount = await database.GetCollection<BsonDocument>(
                ManifestsCollection)
            .CountDocumentsAsync(new BsonDocument(
                "_id",
                $"generation:{next.GenerationId}"));
        targetManifestCount.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_WhenObservedPointerWitnessChangesBeforeReplace_RejectsAndPreservesChange()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft first = TvMongoGenerationTestData.CreateDraft(
            "generation-witness-race-1",
            961,
            "Published");
        await repository.StageAsync(first, CancellationToken.None);
        await repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(first),
            CancellationToken.None);
        TvGenerationDraft next = TvMongoGenerationTestData.CreateDraft(
            "generation-witness-race-2",
            962,
            "Next",
            TvMongoGenerationTestData.BaseTime.AddHours(1));
        await repository.StageAsync(next, CancellationToken.None);
        using ManualResetEventSlim allowPointerReplace = new(initialState: false);
        TaskCompletionSource pointerReplaceStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        MongoTvGenerationRepository racingRepository = CreatePointerReplaceBlockedRepository(
            pointerReplaceStarted,
            allowPointerReplace);
        Task publishTask = racingRepository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(next, first.GenerationId),
            CancellationToken.None);
        await pointerReplaceStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        string concurrentHash = new('e', 64);
        try
        {
            await database.GetCollection<BsonDocument>(ManifestsCollection).UpdateOneAsync(
                new BsonDocument("_id", "published-tv"),
                new BsonDocument("$set", new BsonDocument("membershipHash", concurrentHash)));
        }
        finally
        {
            allowPointerReplace.Set();
        }

        Func<Task> action = async () => await publishTask;
        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_previous_pointer_conflict");
        BsonDocument pointer = await ReadPointerAsync();
        pointer["generationId"].AsString.Should().Be(first.GenerationId);
        pointer["membershipHash"].AsString.Should().Be(concurrentHash);
    }

    [Fact]
    public async Task PublishAsync_WhenObservedPointerIsDeletedBeforeReplace_RejectsWithoutUpsert()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft first = TvMongoGenerationTestData.CreateDraft(
            "generation-delete-race-1",
            971,
            "Published");
        await repository.StageAsync(first, CancellationToken.None);
        await repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(first),
            CancellationToken.None);
        TvGenerationDraft next = TvMongoGenerationTestData.CreateDraft(
            "generation-delete-race-2",
            972,
            "Next",
            TvMongoGenerationTestData.BaseTime.AddHours(1));
        await repository.StageAsync(next, CancellationToken.None);
        using ManualResetEventSlim allowPointerReplace = new(initialState: false);
        TaskCompletionSource pointerReplaceStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        MongoTvGenerationRepository racingRepository = CreatePointerReplaceBlockedRepository(
            pointerReplaceStarted,
            allowPointerReplace);
        Task publishTask = racingRepository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(next, first.GenerationId),
            CancellationToken.None);
        await pointerReplaceStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await database.GetCollection<BsonDocument>(ManifestsCollection).DeleteOneAsync(
                new BsonDocument("_id", "published-tv"));
        }
        finally
        {
            allowPointerReplace.Set();
        }

        Func<Task> action = async () => await publishTask;
        InvalidOperationException exception = (await action.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be("tv_generation_previous_pointer_conflict");
        long pointerCount = await database.GetCollection<BsonDocument>(ManifestsCollection)
            .CountDocumentsAsync(new BsonDocument("_id", "published-tv"));
        pointerCount.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_ConcurrentForks_AllowsOneAtomicPointerWinner()
    {
        MongoTvGenerationRepository repository = CreateRepository();
        TvGenerationDraft first = TvMongoGenerationTestData.CreateDraft(
            "generation-fork-1",
            1001,
            "Root");
        await repository.StageAsync(first, CancellationToken.None);
        await repository.PublishAsync(
            TvMongoGenerationTestData.CreateManifest(first),
            CancellationToken.None);
        TvGenerationDraft left = TvMongoGenerationTestData.CreateDraft(
            "generation-fork-left",
            1002,
            "Left",
            TvMongoGenerationTestData.BaseTime.AddHours(1));
        TvGenerationDraft right = TvMongoGenerationTestData.CreateDraft(
            "generation-fork-right",
            1003,
            "Right",
            TvMongoGenerationTestData.BaseTime.AddHours(1));
        await repository.StageAsync(left, CancellationToken.None);
        await repository.StageAsync(right, CancellationToken.None);
        TvGenerationManifest leftManifest = TvMongoGenerationTestData.CreateManifest(
            left,
            first.GenerationId);
        TvGenerationManifest rightManifest = TvMongoGenerationTestData.CreateManifest(
            right,
            first.GenerationId);

        Task leftPublish = repository.PublishAsync(leftManifest, CancellationToken.None);
        Task rightPublish = repository.PublishAsync(rightManifest, CancellationToken.None);
        Exception?[] outcomes = await Task.WhenAll(
            CaptureExceptionAsync(leftPublish),
            CaptureExceptionAsync(rightPublish));

        outcomes.Count(exception => exception is null).Should().Be(1);
        outcomes.Count(exception => exception is InvalidOperationException).Should().Be(1);
        string winner = (await repository.GetPublishedAsync(CancellationToken.None))!
            .Manifest.GenerationId;
        winner.Should().BeOneOf(left.GenerationId, right.GenerationId);
    }

    [Theory]
    [InlineData("legacyWatchlistItemId")]
    [InlineData("legacyMigratedAt")]
    [InlineData("legacyMigrationStatus")]
    [InlineData("legacyMigrationReason")]
    [InlineData("genres")]
    [InlineData("originalLanguage")]
    [InlineData("tmdbVoteAverage")]
    [InlineData("tmdbVoteCount")]
    public void ToDomain_WhenGenerationRowContainsLegacyOnlyAuthority_Rejects(string field)
    {
        TvGenerationDraft draft = TvMongoGenerationTestData.CreateDraft(
            "generation-legacy-inert",
            1701,
            "Legacy Inert");
        BsonDocument raw = MongoTvShowDocument.FromDomain(draft.Shows[0]).ToBsonDocument();
        raw[field] = field switch
        {
            "legacyMigratedAt" => raw["addedAt"],
            "genres" => new BsonArray(["Drama"]),
            "tmdbVoteAverage" => 8.5,
            "tmdbVoteCount" => 42,
            _ => "legacy-value"
        };
        MongoTvShowDocument document = BsonSerializer.Deserialize<MongoTvShowDocument>(raw);

        Action action = () => document.ToDomain();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("tv_generation_row_invalid");
    }

    [Fact]
    public void LegacyDocumentSerialization_OmitsGenerationAuthorityAndPreservesLegacyData()
    {
        MongoTvShowDocument legacy = new()
        {
            Id = "legacy:legacy-item-1",
            DocumentKind = MongoTvShowDocument.LegacyDocumentKind,
            TvdbId = 1234,
            TmdbId = 5678,
            ImdbId = "tt1234567",
            IdentityStatus = TvIdentityStatus.LegacyUnresolved,
            Title = "Legacy Show",
            Year = 2020,
            Overview = "Legacy overview",
            PosterUrl = "https://image.tmdb.org/t/p/w500/legacy-poster.jpg",
            BackdropUrl = "https://image.tmdb.org/t/p/w1280/legacy-backdrop.jpg",
            AddedAt = TvMongoGenerationTestData.BaseTime.AddYears(-1),
            UpdatedAt = TvMongoGenerationTestData.BaseTime.AddMonths(-1),
            LegacySourceId = "legacy-source-1",
            LegacyWatchlistItemId = "legacy-item-1",
            LegacyMigratedAt = TvMongoGenerationTestData.BaseTime,
            LegacyMigrationStatus = "migrated",
            LegacyMigrationReason = "exact_tvdb_identity",
            Genres = ["Drama", "Mystery"],
            OriginalLanguage = "pl",
            TmdbVoteAverage = 8.25,
            TmdbVoteCount = 321
        };

        BsonDocument raw = legacy.ToBsonDocument();

        string[] generationAuthorityKeys =
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
        foreach (string key in generationAuthorityKeys)
        {
            raw.Contains(key).Should().BeFalse($"legacy rows must not carry {key}");
        }

        raw["legacySourceId"].AsString.Should().Be("legacy-source-1");
        raw["legacyWatchlistItemId"].AsString.Should().Be("legacy-item-1");
        raw["legacyMigrationStatus"].AsString.Should().Be("migrated");
        raw["legacyMigrationReason"].AsString.Should().Be("exact_tvdb_identity");
        raw["genres"].AsBsonArray.Select(value => value.AsString)
            .Should().Equal("Drama", "Mystery");
        raw["originalLanguage"].AsString.Should().Be("pl");
        raw["tmdbVoteAverage"].AsDouble.Should().Be(8.25);
        raw["tmdbVoteCount"].AsInt32.Should().Be(321);
    }

    [Fact]
    public void PublishedTvGeneration_SnapshotsSourceAndWithCollections()
    {
        TvGenerationDraft first = TvMongoGenerationTestData.CreateDraft(
            "generation-snapshot-1",
            1801,
            "First");
        TvGenerationDraft second = TvMongoGenerationTestData.CreateDraft(
            "generation-snapshot-2",
            1802,
            "Second");
        List<TvShow> source = [first.Shows[0]];
        PublishedTvGeneration published = new(
            TvMongoGenerationTestData.CreateManifest(first),
            source);
        source.Add(second.Shows[0]);
        List<TvShow> replacement = [second.Shows[0]];

        PublishedTvGeneration copied = published with { Shows = replacement };
        replacement.Add(first.Shows[0]);

        published.Shows.Should().ContainSingle().Which.TraktId.Should().Be(1801);
        copied.Shows.Should().ContainSingle().Which.TraktId.Should().Be(1802);
        Action mutate = () => ((IList<TvShow>)copied.Shows).Add(first.Shows[0]);
        mutate.Should().Throw<NotSupportedException>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await client.DropDatabaseAsync(databaseName);
    }

    private MongoTvGenerationRepository CreateRepository()
    {
        return new MongoTvGenerationRepository(database, Options.Create(options));
    }

    private MongoTvGenerationRepository CreatePointerReplaceBlockedRepository(
        TaskCompletionSource pointerReplaceStarted,
        ManualResetEventSlim allowPointerReplace)
    {
        MongoClientSettings settings = MongoClientSettings.FromConnectionString(
            options.ConnectionString);
        settings.ClusterConfigurator = cluster =>
            cluster.Subscribe<CommandStartedEvent>(command =>
            {
                if (!string.Equals(command.CommandName, "update", StringComparison.Ordinal)
                    || !command.Command.TryGetValue("update", out BsonValue? collection)
                    || !string.Equals(
                        collection.AsString,
                        ManifestsCollection,
                        StringComparison.Ordinal))
                {
                    return;
                }

                pointerReplaceStarted.TrySetResult();
                allowPointerReplace.Wait(TimeSpan.FromSeconds(10));
            });
        MongoClient monitoredClient = new(settings);
        return new MongoTvGenerationRepository(
            monitoredClient.GetDatabase(databaseName),
            Options.Create(options));
    }

    private async Task<BsonDocument> ReadPointerAsync()
    {
        return await database.GetCollection<BsonDocument>(ManifestsCollection)
            .Find(new BsonDocument("_id", "published-tv"))
            .SingleAsync();
    }

    private async Task<BsonDocument> ReadManifestAsync(string generationId)
    {
        return await database.GetCollection<BsonDocument>(ManifestsCollection)
            .Find(new BsonDocument("_id", $"generation:{generationId}"))
            .SingleAsync();
    }

    private static async Task<Exception?> CaptureExceptionAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
