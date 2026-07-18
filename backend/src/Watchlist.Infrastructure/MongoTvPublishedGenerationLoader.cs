using MongoDB.Bson;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

internal sealed class MongoTvPublishedGenerationLoader(
    IMongoDatabase database,
    MongoDbOptions options)
{
    private readonly IMongoCollection<MongoTvShowDocument> shows =
        database.GetCollection<MongoTvShowDocument>(options.TvShowsCollectionName);

    private readonly IMongoCollection<MongoTvSyncManifestDocument> manifests =
        database.GetCollection<MongoTvSyncManifestDocument>(
            options.TvSyncManifestsCollectionName);

    private readonly IMongoCollection<MongoTvPublishedPointerDocument> pointers =
        database.GetCollection<MongoTvPublishedPointerDocument>(
            options.TvSyncManifestsCollectionName);

    private readonly IMongoCollection<MongoTvLifecycleEventDocument> lifecycleEvents =
        database.GetCollection<MongoTvLifecycleEventDocument>(
            options.TvLifecycleEventsCollectionName);

    public async Task<PublishedTvGeneration?> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        catch (TvPublishedGenerationInvalidException)
        {
            throw;
        }
        catch (Exception exception) when (exception is BsonSerializationException
            or FormatException)
        {
            throw Invalid("tv_published_document_invalid");
        }
    }

    internal async Task<PublishedTvGeneration> LoadCapturedAsync(
        MongoTvPublishedPointerDocument pointer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        try
        {
            return await LoadCapturedCoreAsync(pointer, cancellationToken);
        }
        catch (TvPublishedGenerationInvalidException)
        {
            throw;
        }
        catch (Exception exception) when (exception is BsonSerializationException
            or FormatException)
        {
            throw Invalid("tv_published_document_invalid");
        }
    }

    private async Task<PublishedTvGeneration?> LoadCoreAsync(
        CancellationToken cancellationToken)
    {
        MongoTvPublishedPointerDocument? pointer = await pointers
            .Find(document => document.Id == MongoTvPublishedPointerDocument.PublishedPointerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (pointer is null)
        {
            return null;
        }

        return await LoadCapturedCoreAsync(pointer, cancellationToken);
    }

    private async Task<PublishedTvGeneration> LoadCapturedCoreAsync(
        MongoTvPublishedPointerDocument pointer,
        CancellationToken cancellationToken)
    {
        ValidatePointerShape(pointer);
        MongoTvSyncManifestDocument? manifestDocument = await manifests
            .Find(document => document.Id == pointer.ManifestId)
            .FirstOrDefaultAsync(cancellationToken);
        if (manifestDocument is null)
        {
            throw Invalid("tv_published_manifest_missing");
        }

        TvGenerationManifest manifest;
        try
        {
            manifest = manifestDocument.ToDomain();
            new TvSnapshotValidator().Validate(manifest);
        }
        catch (TvSourceSnapshotRejectedException)
        {
            throw Invalid("tv_published_manifest_invalid");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or FormatException)
        {
            throw Invalid("tv_published_manifest_invalid");
        }

        ValidatePointerManifest(pointer, manifestDocument, manifest);
        List<MongoTvShowDocument> rowDocuments = await shows
            .Find(document => document.DocumentKind == MongoTvShowDocument.GenerationDocumentKind
                && document.GenerationId == pointer.GenerationId)
            .SortBy(document => document.TraktId)
            .ToListAsync(cancellationToken);
        if (rowDocuments.Count != manifestDocument.ShowCount)
        {
            throw Invalid("tv_published_row_count_invalid");
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
                            $"generation:{pointer.GenerationId}:{show.TraktId}"),
                        StringComparison.Ordinal)
                    || !string.Equals(show.GenerationId, pointer.GenerationId, StringComparison.Ordinal))
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
            throw Invalid("tv_published_row_invalid");
        }

        List<MongoTvLifecycleEventDocument> eventDocuments = await lifecycleEvents
            .Find(document => document.GenerationId == pointer.GenerationId)
            .SortBy(document => document.TraktId)
            .ThenBy(document => document.LifecycleVersion)
            .ThenBy(document => document.EventId)
            .ToListAsync(cancellationToken);
        if (eventDocuments.Count != manifestDocument.LifecycleEventCount
            || !eventDocuments.Select(document => document.EventId)
                .SequenceEqual(manifest.LifecycleEventIds, StringComparer.Ordinal))
        {
            throw Invalid("tv_published_lifecycle_events_invalid");
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
            throw Invalid("tv_published_lifecycle_events_invalid");
        }

        TvSnapshotValidator validator = new();
        string membershipHash = validator.ComputeMembershipHash(generationShows);
        string progressHash = validator.ComputeProgressHash(generationShows);
        if (!string.Equals(membershipHash, manifest.MembershipHash, StringComparison.Ordinal)
            || !string.Equals(progressHash, manifest.ProgressHash, StringComparison.Ordinal))
        {
            throw Invalid("tv_published_hash_invalid");
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
            throw Invalid("tv_published_generation_semantics_invalid");
        }

        return new PublishedTvGeneration(manifest, generationShows);
    }

    private static void ValidatePointerShape(MongoTvPublishedPointerDocument pointer)
    {
        if (!pointer.HasValidShape())
        {
            throw Invalid("tv_published_pointer_invalid");
        }
    }

    private static void ValidatePointerManifest(
        MongoTvPublishedPointerDocument pointer,
        MongoTvSyncManifestDocument manifestDocument,
        TvGenerationManifest manifest)
    {
        if (!string.Equals(
                manifestDocument.Id,
                $"generation:{pointer.GenerationId}",
                StringComparison.Ordinal)
            || !string.Equals(
                manifestDocument.DocumentKind,
                MongoTvSyncManifestDocument.ManifestDocumentKind,
                StringComparison.Ordinal)
            || !string.Equals(manifest.GenerationId, pointer.GenerationId, StringComparison.Ordinal)
            || manifestDocument.ShowCount != pointer.ShowCount
            || manifestDocument.LifecycleEventCount != pointer.LifecycleEventCount
            || manifestDocument.LifecycleEventCount != manifest.LifecycleEventIds.Count
            || !string.Equals(
                manifest.MembershipHash,
                pointer.MembershipHash,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.ProgressHash,
                pointer.ProgressHash,
                StringComparison.Ordinal)
            || manifest.PublishedAt != pointer.PublishedAt)
        {
            throw Invalid("tv_published_pointer_invalid");
        }
    }

    private static TvPublishedGenerationInvalidException Invalid(string code)
    {
        return new TvPublishedGenerationInvalidException(code);
    }
}
