using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTvGenerationRetentionRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : ITvGenerationRetentionRepository
{
    private readonly IMongoCollection<MongoTvShowDocument> shows =
        database.GetCollection<MongoTvShowDocument>(options.Value.TvShowsCollectionName);

    private readonly IMongoCollection<MongoTvLifecycleEventDocument> lifecycleEvents =
        database.GetCollection<MongoTvLifecycleEventDocument>(
            options.Value.TvLifecycleEventsCollectionName);

    private readonly IMongoCollection<MongoTvSyncManifestDocument> manifests =
        database.GetCollection<MongoTvSyncManifestDocument>(
            options.Value.TvSyncManifestsCollectionName);

    private readonly IMongoCollection<MongoTvPublishedPointerDocument> pointers =
        database.GetCollection<MongoTvPublishedPointerDocument>(
            options.Value.TvSyncManifestsCollectionName);

    private readonly IMongoCollection<BsonDocument> rawShows =
        database.GetCollection<BsonDocument>(options.Value.TvShowsCollectionName);

    private readonly IMongoCollection<BsonDocument> rawLifecycleEvents =
        database.GetCollection<BsonDocument>(
            options.Value.TvLifecycleEventsCollectionName);

    private readonly IMongoCollection<BsonDocument> rawManifests =
        database.GetCollection<BsonDocument>(
            options.Value.TvSyncManifestsCollectionName);

    public async Task<TvGenerationRetentionSnapshot> ReadSnapshotAsync(
        CancellationToken cancellationToken)
    {
        MongoTvPublishedPointerDocument? pointer =
            await ReadPointerAsync(cancellationToken);
        TvStoredGenerationSummary[] summaries =
            await ReadManifestSummariesAsync(cancellationToken);
        HashSet<string> manifestGenerationIds = summaries
            .Select(summary => summary.GenerationId)
            .ToHashSet(StringComparer.Ordinal);
        PhysicalGenerationIdentities physicalIdentities =
            await ReadPhysicalGenerationIdentitiesAsync(cancellationToken);
        HashSet<string> physicalGenerationIds =
            new(physicalIdentities.ValidGenerationIds, StringComparer.Ordinal);
        physicalGenerationIds.ExceptWith(manifestGenerationIds);
        AddOpaqueMalformedIdentityTokens(
            physicalGenerationIds,
            manifestGenerationIds,
            physicalIdentities.MalformedIdentityCount);

        return new TvGenerationRetentionSnapshot(
            pointer?.GenerationId,
            summaries,
            physicalGenerationIds.Order(StringComparer.Ordinal).ToArray());
    }

    public async Task<TvGenerationRetentionDeleteResult> ApplyAsync(
        TvGenerationRetentionPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidatePlan(plan);

        MongoTvPublishedPointerDocument? pointer =
            await ReadPointerAsync(cancellationToken);
        if (!string.Equals(
                pointer?.GenerationId,
                plan.ExpectedCurrentGenerationId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("tv_generation_retention_pointer_changed");
        }

        if (pointer is null)
        {
            return new TvGenerationRetentionDeleteResult(0, 0, 0);
        }

        string currentGenerationId = pointer.GenerationId;
        string[] expiredManifestGenerationIds = plan.ExpiredManifestGenerationIds
            .Distinct(StringComparer.Ordinal)
            .Where(generationId => !string.Equals(
                generationId,
                currentGenerationId,
                StringComparison.Ordinal))
            .ToArray();
        string[] childGenerationIds = expiredManifestGenerationIds
            .Concat(plan.ExpiredOrphanGenerationIds)
            .Distinct(StringComparer.Ordinal)
            .Where(generationId => !string.Equals(
                generationId,
                currentGenerationId,
                StringComparison.Ordinal))
            .ToArray();

        long showDocumentsDeleted = 0;
        if (childGenerationIds.Length > 0)
        {
            FilterDefinitionBuilder<MongoTvShowDocument> filter =
                Builders<MongoTvShowDocument>.Filter;
            DeleteResult result = await shows.DeleteManyAsync(
                filter.Eq(
                    document => document.DocumentKind,
                    MongoTvShowDocument.GenerationDocumentKind)
                & HasStringDocumentKind<MongoTvShowDocument>()
                & filter.In(document => document.GenerationId, childGenerationIds)
                & filter.Ne(document => document.GenerationId, currentGenerationId)
                & HasStringGenerationId<MongoTvShowDocument>(),
                cancellationToken);
            EnsureAcknowledged(result);
            showDocumentsDeleted = result.DeletedCount;
        }

        long lifecycleEventsDeleted = 0;
        if (childGenerationIds.Length > 0)
        {
            FilterDefinitionBuilder<MongoTvLifecycleEventDocument> filter =
                Builders<MongoTvLifecycleEventDocument>.Filter;
            DeleteResult result = await lifecycleEvents.DeleteManyAsync(
                filter.In(document => document.GenerationId, childGenerationIds)
                & filter.Ne(document => document.GenerationId, currentGenerationId)
                & HasStringGenerationId<MongoTvLifecycleEventDocument>(),
                cancellationToken);
            EnsureAcknowledged(result);
            lifecycleEventsDeleted = result.DeletedCount;
        }

        long manifestsDeleted = 0;
        if (expiredManifestGenerationIds.Length > 0)
        {
            FilterDefinitionBuilder<MongoTvSyncManifestDocument> filter =
                Builders<MongoTvSyncManifestDocument>.Filter;
            DeleteResult result = await manifests.DeleteManyAsync(
                filter.Eq(
                    document => document.DocumentKind,
                    MongoTvSyncManifestDocument.ManifestDocumentKind)
                & HasStringDocumentKind<MongoTvSyncManifestDocument>()
                & filter.In(
                    document => document.GenerationId,
                    expiredManifestGenerationIds)
                & filter.Ne(document => document.GenerationId, currentGenerationId)
                & HasStringGenerationId<MongoTvSyncManifestDocument>(),
                cancellationToken);
            EnsureAcknowledged(result);
            manifestsDeleted = result.DeletedCount;
        }

        return new TvGenerationRetentionDeleteResult(
            showDocumentsDeleted,
            lifecycleEventsDeleted,
            manifestsDeleted);
    }

    private async Task<MongoTvPublishedPointerDocument?> ReadPointerAsync(
        CancellationToken cancellationToken)
    {
        MongoTvPublishedPointerDocument? pointer;
        try
        {
            pointer = await pointers
                .Find(document =>
                    document.Id == MongoTvPublishedPointerDocument.PublishedPointerId)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is BsonSerializationException
            or FormatException)
        {
            throw new InvalidOperationException(
                "tv_generation_retention_pointer_invalid");
        }

        if (pointer is not null && !pointer.HasValidShape())
        {
            throw new InvalidOperationException(
                "tv_generation_retention_pointer_invalid");
        }

        return pointer;
    }

    private static void ValidatePlan(TvGenerationRetentionPlan plan)
    {
        IReadOnlyList<string>[] buckets =
        [
            plan.RetainedGenerationIds,
            plan.ExpiredManifestGenerationIds,
            plan.ExpiredOrphanGenerationIds,
            plan.DeferredOrphanGenerationIds,
            plan.UncertainOrphanGenerationIds
        ];
        bool invalid = plan.ExpectedCurrentGenerationId is not null
            && string.IsNullOrWhiteSpace(plan.ExpectedCurrentGenerationId);
        invalid = invalid || buckets.Any(
            bucket => bucket.Any(string.IsNullOrWhiteSpace));
        HashSet<string>[] bucketSets = buckets
            .Select(bucket => new HashSet<string>(bucket, StringComparer.Ordinal))
            .ToArray();
        for (int left = 0; left < bucketSets.Length && !invalid; left++)
        {
            for (int right = left + 1; right < bucketSets.Length; right++)
            {
                if (bucketSets[left].Overlaps(bucketSets[right]))
                {
                    invalid = true;
                    break;
                }
            }
        }

        HashSet<string> retainedGenerationIds = bucketSets[0];
        HashSet<string> expiredManifestGenerationIds = bucketSets[1];
        HashSet<string> expiredOrphanGenerationIds = bucketSets[2];
        invalid = invalid || (plan.ExpectedCurrentGenerationId is null
            ? expiredManifestGenerationIds.Count > 0
                || expiredOrphanGenerationIds.Count > 0
            : !retainedGenerationIds.Contains(plan.ExpectedCurrentGenerationId));
        if (invalid)
        {
            throw new InvalidOperationException("tv_generation_retention_plan_invalid");
        }
    }

    private async Task<TvStoredGenerationSummary[]> ReadManifestSummariesAsync(
        CancellationToken cancellationToken)
    {
        List<TvStoredGenerationSummary> summaries;
        try
        {
            FilterDefinitionBuilder<BsonDocument> rawFilter =
                Builders<BsonDocument>.Filter;
            FilterDefinition<BsonDocument> validManifestDocumentKind =
                rawFilter.Eq(
                    "documentKind",
                    MongoTvSyncManifestDocument.ManifestDocumentKind)
                & HasStringDocumentKind<BsonDocument>();
            FilterDefinition<BsonDocument> validManifestGenerationKind =
                HasInt32Field<BsonDocument>("kind")
                & rawFilter.Or(
                    rawFilter.Eq(
                        "kind",
                        (int)TvGenerationKind.ScheduledFull),
                    rawFilter.Eq(
                        "kind",
                        (int)TvGenerationKind.ActivityFull));
            FilterDefinition<BsonDocument> validManifestShape =
                validManifestDocumentKind
                & HasStringGenerationId<BsonDocument>()
                & HasMatchingManifestPhysicalId()
                & validManifestGenerationKind;
            bool hasInvalidManifest = await rawManifests
                .Find(
                    rawFilter.Ne(
                        "_id",
                        MongoTvPublishedPointerDocument.PublishedPointerId)
                    & rawFilter.Not(validManifestShape))
                .Project(new BsonDocument("_id", 1))
                .Limit(1)
                .AnyAsync(cancellationToken);
            if (hasInvalidManifest)
            {
                throw new InvalidOperationException(
                    "tv_generation_retention_manifest_invalid");
            }

            FilterDefinitionBuilder<MongoTvSyncManifestDocument> filter =
                Builders<MongoTvSyncManifestDocument>.Filter;
            summaries = await manifests
                .Find(
                    filter.Eq(
                        document => document.DocumentKind,
                        MongoTvSyncManifestDocument.ManifestDocumentKind)
                    & HasStringDocumentKind<MongoTvSyncManifestDocument>())
                .Project(document => new TvStoredGenerationSummary(
                    document.GenerationId,
                    document.PublishedAt))
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is BsonSerializationException
            or FormatException)
        {
            throw new InvalidOperationException(
                "tv_generation_retention_manifest_invalid");
        }

        if (summaries.Any(summary =>
                string.IsNullOrWhiteSpace(summary.GenerationId)
                || summary.PublishedAt == default))
        {
            throw new InvalidOperationException(
                "tv_generation_retention_manifest_invalid");
        }

        return summaries.ToArray();
    }

    private async Task<PhysicalGenerationIdentities>
        ReadPhysicalGenerationIdentitiesAsync(
            CancellationToken cancellationToken)
    {
        FilterDefinitionBuilder<BsonDocument> filter =
            Builders<BsonDocument>.Filter;
        FilterDefinition<BsonDocument> generationShows =
            filter.Eq(
                "documentKind",
                MongoTvShowDocument.GenerationDocumentKind)
            & HasStringDocumentKind<BsonDocument>();
        FilterDefinition<BsonDocument> stringGenerationId =
            HasStringGenerationId<BsonDocument>();
        FilterDefinition<BsonDocument> nonStringGenerationId =
            new BsonDocument(
                "$expr",
                new BsonDocument(
                    "$ne",
                    new BsonArray(
                        [new BsonDocument("$type", "$generationId"), "string"])));
        FilterDefinition<BsonDocument> generationIdExists =
            filter.Exists("generationId");
        FilterDefinition<BsonDocument> emptyArrayGenerationId =
            new BsonDocument(
                "generationId",
                new BsonDocument
                {
                    { "$type", "array" },
                    { "$size", 0 }
                });
        List<BsonValue> validShowGenerationIds =
            await ReadDistinctGenerationIdsAsync(
            rawShows,
            generationShows & stringGenerationId,
            cancellationToken);
        List<BsonValue> validEventGenerationIds =
            await ReadDistinctGenerationIdsAsync(
            rawLifecycleEvents,
            stringGenerationId,
            cancellationToken);
        List<BsonValue> malformedShowGenerationIds =
            await ReadDistinctGenerationIdsAsync(
            rawShows,
            generationShows & generationIdExists & nonStringGenerationId,
            cancellationToken);
        List<BsonValue> malformedEventGenerationIds =
            await ReadDistinctGenerationIdsAsync(
            rawLifecycleEvents,
            generationIdExists & nonStringGenerationId,
            cancellationToken);
        bool hasMissingGenerationId = await rawShows
            .Find(generationShows & filter.Exists("generationId", false))
            .Limit(1)
            .AnyAsync(cancellationToken);
        hasMissingGenerationId = hasMissingGenerationId
            || await rawLifecycleEvents
                .Find(filter.Exists("generationId", false))
                .Limit(1)
                .AnyAsync(cancellationToken);
        bool hasEmptyArrayGenerationId = await rawShows
            .Find(generationShows & emptyArrayGenerationId)
            .Limit(1)
            .AnyAsync(cancellationToken);
        hasEmptyArrayGenerationId = hasEmptyArrayGenerationId
            || await rawLifecycleEvents
                .Find(emptyArrayGenerationId)
                .Limit(1)
                .AnyAsync(cancellationToken);

        HashSet<string> validGenerationIds = new(StringComparer.Ordinal);
        HashSet<BsonValue> malformedGenerationIds = new(
            malformedShowGenerationIds.Concat(malformedEventGenerationIds));
        foreach (BsonValue generationId in
            validShowGenerationIds.Concat(validEventGenerationIds))
        {
            if (generationId is BsonString bsonString
                && !string.IsNullOrWhiteSpace(bsonString.Value))
            {
                validGenerationIds.Add(bsonString.Value);
            }
            else
            {
                malformedGenerationIds.Add(generationId);
            }
        }

        int malformedIdentityCount = malformedGenerationIds.Count
            + (hasMissingGenerationId ? 1 : 0)
            + (hasEmptyArrayGenerationId ? 1 : 0);
        return new PhysicalGenerationIdentities(
            validGenerationIds,
            malformedIdentityCount);
    }

    private static async Task<List<BsonValue>> ReadDistinctGenerationIdsAsync(
        IMongoCollection<BsonDocument> collection,
        FilterDefinition<BsonDocument> filter,
        CancellationToken cancellationToken)
    {
        FieldDefinition<BsonDocument, BsonValue> generationIdField =
            new StringFieldDefinition<BsonDocument, BsonValue>("generationId");
        using IAsyncCursor<BsonValue> cursor =
            await collection.DistinctAsync(
                generationIdField,
                filter,
                new DistinctOptions(),
                cancellationToken);
        return await cursor.ToListAsync(cancellationToken);
    }

    private static FilterDefinition<TDocument>
        HasStringGenerationId<TDocument>()
    {
        return HasStringField<TDocument>("generationId");
    }

    private static FilterDefinition<TDocument>
        HasStringDocumentKind<TDocument>()
    {
        return HasStringField<TDocument>("documentKind");
    }

    private static FilterDefinition<TDocument> HasStringField<TDocument>(
        string fieldName)
    {
        return HasFieldType<TDocument>(fieldName, "string");
    }

    private static FilterDefinition<TDocument> HasInt32Field<TDocument>(
        string fieldName)
    {
        return HasFieldType<TDocument>(fieldName, "int");
    }

    private static FilterDefinition<TDocument> HasFieldType<TDocument>(
        string fieldName,
        string bsonType)
    {
        BsonDocument expression = new(
            "$expr",
            new BsonDocument(
                "$eq",
                new BsonArray(
                    [new BsonDocument("$type", $"${fieldName}"), bsonType])));
        return new BsonDocumentFilterDefinition<TDocument>(expression);
    }

    private static FilterDefinition<BsonDocument> HasMatchingManifestPhysicalId()
    {
        BsonDocument hasStringIdentityFields = new(
            "$and",
            new BsonArray
            {
                new BsonDocument(
                    "$eq",
                    new BsonArray
                    {
                        new BsonDocument("$type", "$_id"),
                        "string"
                    }),
                new BsonDocument(
                    "$eq",
                    new BsonArray
                    {
                        new BsonDocument("$type", "$generationId"),
                        "string"
                    })
            });
        BsonDocument physicalIdMatchesGeneration = new(
            "$eq",
            new BsonArray
            {
                "$_id",
                new BsonDocument(
                    "$concat",
                    new BsonArray { "generation:", "$generationId" })
            });
        BsonDocument expression = new(
            "$expr",
            new BsonDocument(
                "$cond",
                new BsonArray
                {
                    hasStringIdentityFields,
                    physicalIdMatchesGeneration,
                    false
                }));
        return new BsonDocumentFilterDefinition<BsonDocument>(expression);
    }

    private static void AddOpaqueMalformedIdentityTokens(
        HashSet<string> physicalGenerationIds,
        HashSet<string> manifestGenerationIds,
        int malformedIdentityCount)
    {
        int candidate = 1;
        int added = 0;
        while (added < malformedIdentityCount)
        {
            string token = FormattableString.Invariant(
                $"invalid-physical-identity-{candidate:0000}");
            candidate++;
            if (manifestGenerationIds.Contains(token)
                || !physicalGenerationIds.Add(token))
            {
                continue;
            }

            added++;
        }
    }

    private static void EnsureAcknowledged(DeleteResult result)
    {
        if (!result.IsAcknowledged)
        {
            throw new InvalidOperationException(
                "tv_generation_retention_write_unacknowledged");
        }
    }

    private sealed record PhysicalGenerationIdentities(
        IReadOnlySet<string> ValidGenerationIds,
        int MalformedIdentityCount);
}
