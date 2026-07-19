using System.Collections.ObjectModel;
using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Describes one immutable TV generation that can be selected for publication.
/// </summary>
public sealed record TvGenerationManifest(
    string GenerationId,
    string? PreviousGenerationId,
    TvGenerationKind Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    DateTimeOffset PublishedAt,
    TraktActivityCursor ActivityCursor,
    int WatchlistPageCount,
    int WatchlistItemCount,
    int ProgressPageCount,
    int ProgressItemCount,
    string RequestContractVersion,
    IReadOnlyDictionary<string, string> RequestFilters,
    string MembershipHash,
    string ProgressHash,
    DateTimeOffset? PlexHistoryCollectedAt,
    DateTimeOffset? PlexHistoryWatermark,
    DateTimeOffset? ProviderEnrichmentCompletedAt,
    string ValidationStatus,
    IReadOnlyList<string> ValidationFailureReasons,
    IReadOnlyList<string> LifecycleEventIds,
    IReadOnlyList<string> CleanupEventIds,
    bool MutationCapable,
    IReadOnlyList<string> HealthReasons,
    IReadOnlyList<string> EnrichmentErrors)
{
    private static readonly IReadOnlyList<string> RequiredHealthReasons = Array.AsReadOnly(
        new[]
        {
            "plex_history_phase_not_implemented",
            "worker_tv_mutation_disabled"
        });

    private IReadOnlyDictionary<string, string> _requestFilters = Snapshot(RequestFilters);
    private IReadOnlyList<string> _validationFailureReasons = Snapshot(ValidationFailureReasons);
    private IReadOnlyList<string> _lifecycleEventIds = Snapshot(LifecycleEventIds);
    private IReadOnlyList<string> _cleanupEventIds = Snapshot(CleanupEventIds);
    private IReadOnlyList<string> _healthReasons = Snapshot(HealthReasons);
    private IReadOnlyList<string> _enrichmentErrors = Snapshot(EnrichmentErrors);

    /// <summary>
    /// Gets the durable publication time of the most recent scheduled full generation.
    /// Null is reserved for manifests written before this provenance field existed.
    /// </summary>
    public DateTimeOffset? LastScheduledFullAt { get; init; }

    public IReadOnlyDictionary<string, string> RequestFilters
    {
        get => _requestFilters;
        init => _requestFilters = Snapshot(value);
    }

    public IReadOnlyList<string> ValidationFailureReasons
    {
        get => _validationFailureReasons;
        init => _validationFailureReasons = Snapshot(value);
    }

    public IReadOnlyList<string> LifecycleEventIds
    {
        get => _lifecycleEventIds;
        init => _lifecycleEventIds = Snapshot(value);
    }

    public IReadOnlyList<string> CleanupEventIds
    {
        get => _cleanupEventIds;
        init => _cleanupEventIds = Snapshot(value);
    }

    public IReadOnlyList<string> HealthReasons
    {
        get => _healthReasons;
        init => _healthReasons = Snapshot(value);
    }

    public IReadOnlyList<string> EnrichmentErrors
    {
        get => _enrichmentErrors;
        init => _enrichmentErrors = Snapshot(value);
    }

    /// <summary>
    /// Validates a complete draft and constructs the locked Phase 1 publication envelope.
    /// </summary>
    public static TvGenerationManifest CreatePhaseOne(
        TvGenerationDraft draft,
        string? previousGenerationId,
        DateTimeOffset publishedAt,
        DateTimeOffset? providerEnrichmentCompletedAt,
        DateTimeOffset? previousLastScheduledFullAt = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        TvSnapshotValidator validator = new();
        validator.Validate(draft);
        TvGenerationManifest manifest = new(
            draft.GenerationId,
            previousGenerationId,
            draft.Kind,
            draft.StartedAt,
            draft.CompletedAt,
            publishedAt,
            draft.ActivityAfter,
            draft.WatchlistPageCount,
            draft.WatchlistItemCount,
            draft.ProgressPageCount,
            draft.ProgressItemCount,
            draft.RequestContractVersion,
            draft.RequestFilters,
            draft.MembershipHash,
            draft.ProgressHash,
            null,
            null,
            providerEnrichmentCompletedAt,
            "valid",
            [],
            draft.LifecycleEvents.Select(item => item.Id).ToArray(),
            [],
            false,
            RequiredHealthReasons,
            draft.EnrichmentErrors)
        {
            LastScheduledFullAt = draft.Kind == TvGenerationKind.ScheduledFull
                ? publishedAt
                : previousLastScheduledFullAt
        };
        validator.Validate(manifest);
        return manifest;
    }

    internal static bool HasRequiredPhaseOneHealthReasons(IReadOnlyList<string> values)
    {
        return values.SequenceEqual(RequiredHealthReasons, StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> Snapshot(
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        SortedDictionary<string, string> snapshot = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> value in values)
        {
            snapshot.Add(value.Key, value.Value);
        }

        return new ReadOnlyDictionary<string, string>(snapshot);
    }

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
