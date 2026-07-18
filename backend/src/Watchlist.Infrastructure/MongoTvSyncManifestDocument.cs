using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoTvSyncManifestDocument
{
    public const string ManifestDocumentKind = "manifest";

    [BsonId]
    public string Id { get; init; } = string.Empty;

    [BsonElement("documentKind")]
    public string DocumentKind { get; init; } = ManifestDocumentKind;

    [BsonElement("generationId")]
    public string GenerationId { get; init; } = string.Empty;

    [BsonElement("previousGenerationId")]
    public string? PreviousGenerationId { get; init; }

    [BsonElement("kind")]
    public TvGenerationKind Kind { get; init; }

    [BsonElement("startedAt")]
    public DateTimeOffset StartedAt { get; init; }

    [BsonElement("completedAt")]
    public DateTimeOffset CompletedAt { get; init; }

    [BsonElement("publishedAt")]
    public DateTimeOffset PublishedAt { get; init; }

    [BsonElement("activityShowWatchlistedAt")]
    public DateTimeOffset ActivityShowWatchlistedAt { get; init; }

    [BsonElement("activityEpisodeWatchedAt")]
    public DateTimeOffset ActivityEpisodeWatchedAt { get; init; }

    [BsonElement("watchlistPageCount")]
    public int WatchlistPageCount { get; init; }

    [BsonElement("watchlistItemCount")]
    public int WatchlistItemCount { get; init; }

    [BsonElement("progressPageCount")]
    public int ProgressPageCount { get; init; }

    [BsonElement("progressItemCount")]
    public int ProgressItemCount { get; init; }

    [BsonElement("requestContractVersion")]
    public string RequestContractVersion { get; init; } = string.Empty;

    [BsonElement("requestFilters")]
    public IReadOnlyDictionary<string, string> RequestFilters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [BsonElement("membershipHash")]
    public string MembershipHash { get; init; } = string.Empty;

    [BsonElement("progressHash")]
    public string ProgressHash { get; init; } = string.Empty;

    [BsonElement("plexHistoryCollectedAt")]
    public DateTimeOffset? PlexHistoryCollectedAt { get; init; }

    [BsonElement("plexHistoryWatermark")]
    public DateTimeOffset? PlexHistoryWatermark { get; init; }

    [BsonElement("providerEnrichmentCompletedAt")]
    public DateTimeOffset? ProviderEnrichmentCompletedAt { get; init; }

    [BsonElement("validationStatus")]
    public string ValidationStatus { get; init; } = string.Empty;

    [BsonElement("validationFailureReasons")]
    public IReadOnlyList<string> ValidationFailureReasons { get; init; } = [];

    [BsonElement("lifecycleEventIds")]
    public IReadOnlyList<string> LifecycleEventIds { get; init; } = [];

    [BsonElement("cleanupEventIds")]
    public IReadOnlyList<string> CleanupEventIds { get; init; } = [];

    [BsonElement("mutationCapable")]
    public bool MutationCapable { get; init; }

    [BsonElement("healthReasons")]
    public IReadOnlyList<string> HealthReasons { get; init; } = [];

    [BsonElement("enrichmentErrors")]
    public IReadOnlyList<string> EnrichmentErrors { get; init; } = [];

    [BsonElement("showCount")]
    public int ShowCount { get; init; }

    [BsonElement("lifecycleEventCount")]
    public int LifecycleEventCount { get; init; }

    public static MongoTvSyncManifestDocument FromDomain(
        TvGenerationManifest manifest,
        int showCount)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new MongoTvSyncManifestDocument
        {
            Id = $"generation:{manifest.GenerationId}",
            DocumentKind = ManifestDocumentKind,
            GenerationId = manifest.GenerationId,
            PreviousGenerationId = manifest.PreviousGenerationId,
            Kind = manifest.Kind,
            StartedAt = manifest.StartedAt,
            CompletedAt = manifest.CompletedAt,
            PublishedAt = manifest.PublishedAt,
            ActivityShowWatchlistedAt = manifest.ActivityCursor.ShowWatchlistedAt,
            ActivityEpisodeWatchedAt = manifest.ActivityCursor.EpisodeWatchedAt,
            WatchlistPageCount = manifest.WatchlistPageCount,
            WatchlistItemCount = manifest.WatchlistItemCount,
            ProgressPageCount = manifest.ProgressPageCount,
            ProgressItemCount = manifest.ProgressItemCount,
            RequestContractVersion = manifest.RequestContractVersion,
            RequestFilters = new SortedDictionary<string, string>(
                manifest.RequestFilters.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal),
                StringComparer.Ordinal),
            MembershipHash = manifest.MembershipHash,
            ProgressHash = manifest.ProgressHash,
            PlexHistoryCollectedAt = manifest.PlexHistoryCollectedAt,
            PlexHistoryWatermark = manifest.PlexHistoryWatermark,
            ProviderEnrichmentCompletedAt = manifest.ProviderEnrichmentCompletedAt,
            ValidationStatus = manifest.ValidationStatus,
            ValidationFailureReasons = manifest.ValidationFailureReasons.ToArray(),
            LifecycleEventIds = manifest.LifecycleEventIds.ToArray(),
            CleanupEventIds = manifest.CleanupEventIds.ToArray(),
            MutationCapable = manifest.MutationCapable,
            HealthReasons = manifest.HealthReasons.ToArray(),
            EnrichmentErrors = manifest.EnrichmentErrors.ToArray(),
            ShowCount = showCount,
            LifecycleEventCount = manifest.LifecycleEventIds.Count
        };
    }

    public TvGenerationManifest ToDomain()
    {
        if (!string.Equals(DocumentKind, ManifestDocumentKind, StringComparison.Ordinal)
            || RequestFilters is null
            || ValidationFailureReasons is null
            || LifecycleEventIds is null
            || CleanupEventIds is null
            || HealthReasons is null
            || EnrichmentErrors is null)
        {
            throw new InvalidOperationException("tv_generation_manifest_invalid");
        }

        return new TvGenerationManifest(
            GenerationId,
            PreviousGenerationId,
            Kind,
            StartedAt,
            CompletedAt,
            PublishedAt,
            new TraktActivityCursor(
                ActivityShowWatchlistedAt,
                ActivityEpisodeWatchedAt),
            WatchlistPageCount,
            WatchlistItemCount,
            ProgressPageCount,
            ProgressItemCount,
            RequestContractVersion,
            new SortedDictionary<string, string>(
                RequestFilters.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal),
                StringComparer.Ordinal),
            MembershipHash,
            ProgressHash,
            PlexHistoryCollectedAt,
            PlexHistoryWatermark,
            ProviderEnrichmentCompletedAt,
            ValidationStatus,
            ValidationFailureReasons.ToArray(),
            LifecycleEventIds.ToArray(),
            CleanupEventIds.ToArray(),
            MutationCapable,
            HealthReasons.ToArray(),
            EnrichmentErrors.ToArray());
    }
}
