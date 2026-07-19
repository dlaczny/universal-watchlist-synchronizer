using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTvGenerationRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : ITvGenerationRepository
{
    private readonly MongoDbOptions mongoOptions = options.Value;

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

    public async Task StageAsync(
        TvGenerationDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        new TvSnapshotValidator().Validate(draft);
        MongoTvShowDocument[] rowDocuments = draft.Shows
            .Select(MongoTvShowDocument.FromDomain)
            .ToArray();
        if (rowDocuments.Length > 0)
        {
            try
            {
                await shows.InsertManyAsync(
                    rowDocuments,
                    new InsertManyOptions { IsOrdered = true },
                    cancellationToken);
            }
            catch (MongoBulkWriteException<MongoTvShowDocument> exception)
                when (exception.WriteErrors.Any(error => error.Category == ServerErrorCategory.DuplicateKey))
            {
                throw Conflict("tv_generation_row_conflict");
            }
            catch (MongoWriteException exception)
                when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                throw Conflict("tv_generation_row_conflict");
            }
        }

        foreach (TvLifecycleEvent lifecycleEvent in draft.LifecycleEvents)
        {
            await StageLifecycleEventAsync(lifecycleEvent, cancellationToken);
        }
    }

    public async Task PublishAsync(
        TvGenerationManifest manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        TvSnapshotValidator validator = new();
        validator.Validate(manifest);
        StagedGeneration staged = await LoadAndValidateStagedAsync(
            manifest,
            validator,
            cancellationToken);
        MongoTvPublishedPointerDocument? currentPointer = await ReadCurrentPointerAsync(
            cancellationToken);
        if (currentPointer is not null)
        {
            MongoTvPublishedGenerationLoader currentLoader = new(database, mongoOptions);
            await currentLoader.LoadCapturedAsync(currentPointer, cancellationToken);
        }

        ValidatePreviousPointer(manifest, currentPointer);

        MongoTvSyncManifestDocument requestedDocument =
            MongoTvSyncManifestDocument.FromDomain(manifest, staged.Shows.Count);
        await InsertOrConfirmManifestAsync(requestedDocument, cancellationToken);
        MongoTvSyncManifestDocument? persistedDocument = await ReadManifestForComparisonAsync(
            requestedDocument.Id,
            cancellationToken);
        if (persistedDocument is null || !ManifestDocumentsEqual(persistedDocument, requestedDocument))
        {
            throw Conflict("tv_generation_manifest_conflict");
        }

        MongoTvPublishedPointerDocument nextPointer =
            MongoTvPublishedPointerDocument.FromManifest(persistedDocument);
        await AdvancePointerAsync(currentPointer, nextPointer, cancellationToken);
    }

    public Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken)
    {
        MongoTvPublishedGenerationLoader loader = new(database, mongoOptions);
        return loader.LoadAsync(cancellationToken);
    }

    private async Task StageLifecycleEventAsync(
        TvLifecycleEvent lifecycleEvent,
        CancellationToken cancellationToken)
    {
        MongoTvLifecycleEventDocument requested =
            MongoTvLifecycleEventDocument.FromDomain(lifecycleEvent);
        try
        {
            await lifecycleEvents.InsertOneAsync(
                requested,
                cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            MongoTvLifecycleEventDocument? existing =
                await ReadLifecycleEventForComparisonAsync(
                    requested.Id,
                    cancellationToken);
            if (existing != requested)
            {
                throw Conflict("tv_lifecycle_event_conflict");
            }
        }
    }

    private async Task<MongoTvLifecycleEventDocument?> ReadLifecycleEventForComparisonAsync(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            return await lifecycleEvents
                .Find(document => document.Id == id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is BsonSerializationException
            or FormatException)
        {
            throw Conflict("tv_lifecycle_event_conflict");
        }
    }

    private async Task<StagedGeneration> LoadAndValidateStagedAsync(
        TvGenerationManifest manifest,
        TvSnapshotValidator validator,
        CancellationToken cancellationToken)
    {
        List<MongoTvShowDocument> rowDocuments;
        try
        {
            rowDocuments = await shows
                .Find(document =>
                    document.DocumentKind == MongoTvShowDocument.GenerationDocumentKind
                    && document.GenerationId == manifest.GenerationId)
                .SortBy(document => document.TraktId)
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is BsonSerializationException
            or FormatException)
        {
            throw Conflict("tv_generation_staged_rows_invalid");
        }
        List<TvShow> generationShows = [];
        try
        {
            foreach (MongoTvShowDocument row in rowDocuments)
            {
                TvShow show = row.ToDomain();
                if (!string.Equals(
                        row.Id,
                        FormattableString.Invariant(
                            $"generation:{manifest.GenerationId}:{show.TraktId}"),
                        StringComparison.Ordinal)
                    || !string.Equals(show.GenerationId, manifest.GenerationId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("tv_generation_row_invalid");
                }

                generationShows.Add(show);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or FormatException)
        {
            throw Conflict("tv_generation_staged_rows_invalid");
        }

        string membershipHash = validator.ComputeMembershipHash(generationShows);
        string progressHash = validator.ComputeProgressHash(generationShows);
        if (!string.Equals(membershipHash, manifest.MembershipHash, StringComparison.Ordinal)
            || !string.Equals(progressHash, manifest.ProgressHash, StringComparison.Ordinal))
        {
            throw Conflict("tv_generation_staged_rows_invalid");
        }

        List<MongoTvLifecycleEventDocument> eventDocuments;
        try
        {
            eventDocuments = await lifecycleEvents
                .Find(document => document.GenerationId == manifest.GenerationId)
                .SortBy(document => document.TraktId)
                .ThenBy(document => document.LifecycleVersion)
                .ThenBy(document => document.EventId)
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is BsonSerializationException
            or FormatException)
        {
            throw Conflict("tv_generation_staged_events_invalid");
        }
        if (!eventDocuments.Select(document => document.EventId)
            .SequenceEqual(manifest.LifecycleEventIds, StringComparer.Ordinal))
        {
            throw Conflict("tv_generation_staged_events_invalid");
        }

        IReadOnlyList<TvLifecycleEvent> events;
        try
        {
            events = eventDocuments
                .Select(document => document.ToDomain())
                .ToArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or FormatException)
        {
            throw Conflict("tv_generation_staged_events_invalid");
        }

        TvGenerationDraft reconstructed = new(
            manifest.GenerationId,
            manifest.Kind,
            manifest.StartedAt,
            manifest.CompletedAt,
            manifest.ActivityCursor,
            manifest.ActivityCursor,
            manifest.WatchlistPageCount,
            manifest.WatchlistItemCount,
            manifest.ProgressPageCount,
            manifest.ProgressItemCount,
            manifest.RequestContractVersion,
            manifest.RequestFilters,
            manifest.MembershipHash,
            manifest.ProgressHash,
            generationShows,
            events,
            manifest.EnrichmentErrors);
        try
        {
            validator.Validate(reconstructed);
        }
        catch (TvSourceSnapshotRejectedException)
        {
            throw Conflict("tv_generation_staged_rows_invalid");
        }

        return new StagedGeneration(generationShows);
    }

    private async Task InsertOrConfirmManifestAsync(
        MongoTvSyncManifestDocument requested,
        CancellationToken cancellationToken)
    {
        try
        {
            await manifests.InsertOneAsync(
                requested,
                cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            MongoTvSyncManifestDocument? existing = await ReadManifestForComparisonAsync(
                requested.Id,
                cancellationToken);
            if (existing is null || !ManifestDocumentsEqual(existing, requested))
            {
                throw Conflict("tv_generation_manifest_conflict");
            }
        }
    }

    private async Task<MongoTvPublishedPointerDocument?> ReadCurrentPointerAsync(
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
            throw new TvPublishedGenerationInvalidException("tv_published_document_invalid");
        }

        if (pointer is not null && !pointer.HasValidShape())
        {
            throw new TvPublishedGenerationInvalidException("tv_published_pointer_invalid");
        }

        return pointer;
    }

    private async Task AdvancePointerAsync(
        MongoTvPublishedPointerDocument? currentPointer,
        MongoTvPublishedPointerDocument nextPointer,
        CancellationToken cancellationToken)
    {
        if (currentPointer is null)
        {
            try
            {
                await pointers.InsertOneAsync(
                    nextPointer,
                    cancellationToken: cancellationToken);
                return;
            }
            catch (MongoWriteException exception)
                when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                throw Conflict("tv_generation_previous_pointer_conflict");
            }
        }

        FilterDefinitionBuilder<MongoTvPublishedPointerDocument> filter =
            Builders<MongoTvPublishedPointerDocument>.Filter;
        FilterDefinition<MongoTvPublishedPointerDocument> expectedPointer =
            filter.Eq(document => document.Id, currentPointer.Id)
            & filter.Eq(document => document.DocumentKind, currentPointer.DocumentKind)
            & filter.Eq(document => document.GenerationId, currentPointer.GenerationId)
            & filter.Eq(document => document.ManifestId, currentPointer.ManifestId)
            & filter.Eq(document => document.ShowCount, currentPointer.ShowCount)
            & filter.Eq(
                document => document.LifecycleEventCount,
                currentPointer.LifecycleEventCount)
            & filter.Eq(document => document.MembershipHash, currentPointer.MembershipHash)
            & filter.Eq(document => document.ProgressHash, currentPointer.ProgressHash)
            & filter.Eq(document => document.PublishedAt, currentPointer.PublishedAt);
        ReplaceOneResult result = await pointers.ReplaceOneAsync(
            expectedPointer,
            nextPointer,
            new ReplaceOptions { IsUpsert = false },
            cancellationToken);
        if (!result.IsAcknowledged || result.MatchedCount != 1)
        {
            throw Conflict("tv_generation_previous_pointer_conflict");
        }
    }

    private async Task<MongoTvSyncManifestDocument?> ReadManifestForComparisonAsync(
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            return await manifests
                .Find(document => document.Id == id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is BsonSerializationException
            or FormatException)
        {
            throw Conflict("tv_generation_manifest_conflict");
        }
    }

    private static void ValidatePreviousPointer(
        TvGenerationManifest manifest,
        MongoTvPublishedPointerDocument? currentPointer)
    {
        if (currentPointer is null)
        {
            if (manifest.PreviousGenerationId is not null)
            {
                throw Conflict("tv_generation_previous_pointer_conflict");
            }

            return;
        }

        if (string.Equals(
            currentPointer.GenerationId,
            manifest.GenerationId,
            StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(
            currentPointer.GenerationId,
            manifest.PreviousGenerationId,
            StringComparison.Ordinal))
        {
            throw Conflict("tv_generation_previous_pointer_conflict");
        }
    }

    private static bool ManifestDocumentsEqual(
        MongoTvSyncManifestDocument left,
        MongoTvSyncManifestDocument right)
    {
        TvGenerationManifest leftManifest;
        TvGenerationManifest rightManifest;
        try
        {
            leftManifest = left.ToDomain();
            rightManifest = right.ToDomain();
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or FormatException)
        {
            return false;
        }

        return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && left.ShowCount == right.ShowCount
            && left.LifecycleEventCount == right.LifecycleEventCount
            && ManifestEquals(leftManifest, rightManifest);
    }

    private static bool ManifestEquals(TvGenerationManifest left, TvGenerationManifest right)
    {
        return string.Equals(left.GenerationId, right.GenerationId, StringComparison.Ordinal)
            && string.Equals(
                left.PreviousGenerationId,
                right.PreviousGenerationId,
                StringComparison.Ordinal)
            && left.Kind == right.Kind
            && left.StartedAt == right.StartedAt
            && left.CompletedAt == right.CompletedAt
            && left.PublishedAt == right.PublishedAt
            && left.LastScheduledFullAt == right.LastScheduledFullAt
            && left.ActivityCursor == right.ActivityCursor
            && left.WatchlistPageCount == right.WatchlistPageCount
            && left.WatchlistItemCount == right.WatchlistItemCount
            && left.ProgressPageCount == right.ProgressPageCount
            && left.ProgressItemCount == right.ProgressItemCount
            && string.Equals(
                left.RequestContractVersion,
                right.RequestContractVersion,
                StringComparison.Ordinal)
            && DictionariesEqual(left.RequestFilters, right.RequestFilters)
            && string.Equals(left.MembershipHash, right.MembershipHash, StringComparison.Ordinal)
            && string.Equals(left.ProgressHash, right.ProgressHash, StringComparison.Ordinal)
            && left.PlexHistoryCollectedAt == right.PlexHistoryCollectedAt
            && left.PlexHistoryWatermark == right.PlexHistoryWatermark
            && left.ProviderEnrichmentCompletedAt == right.ProviderEnrichmentCompletedAt
            && string.Equals(left.ValidationStatus, right.ValidationStatus, StringComparison.Ordinal)
            && left.ValidationFailureReasons.SequenceEqual(
                right.ValidationFailureReasons,
                StringComparer.Ordinal)
            && left.LifecycleEventIds.SequenceEqual(
                right.LifecycleEventIds,
                StringComparer.Ordinal)
            && left.CleanupEventIds.SequenceEqual(right.CleanupEventIds, StringComparer.Ordinal)
            && left.MutationCapable == right.MutationCapable
            && left.HealthReasons.SequenceEqual(right.HealthReasons, StringComparer.Ordinal)
            && left.EnrichmentErrors.SequenceEqual(right.EnrichmentErrors, StringComparer.Ordinal);
    }

    private static bool DictionariesEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        return left.Count == right.Count
            && left.All(item => right.TryGetValue(item.Key, out string? value)
                && string.Equals(item.Value, value, StringComparison.Ordinal));
    }

    private static InvalidOperationException Conflict(string code)
    {
        return new InvalidOperationException(code);
    }

    private sealed record StagedGeneration(IReadOnlyList<TvShow> Shows);
}
