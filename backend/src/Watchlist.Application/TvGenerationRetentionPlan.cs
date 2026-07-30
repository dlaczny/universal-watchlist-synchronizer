using System.Collections.ObjectModel;

namespace Watchlist.Application;

/// <summary>
/// Describes a deterministic, side-effect-free TV generation retention decision.
/// </summary>
public sealed record TvGenerationRetentionPlan
{
    public TvGenerationRetentionPlan(
        string? expectedCurrentGenerationId,
        IReadOnlyList<string> retainedGenerationIds,
        IReadOnlyList<string> expiredManifestGenerationIds,
        IReadOnlyList<string> expiredOrphanGenerationIds,
        IReadOnlyList<string> deferredOrphanGenerationIds,
        IReadOnlyList<string> uncertainOrphanGenerationIds)
    {
        ExpectedCurrentGenerationId = expectedCurrentGenerationId;
        RetainedGenerationIds = Snapshot(retainedGenerationIds);
        ExpiredManifestGenerationIds = Snapshot(expiredManifestGenerationIds);
        ExpiredOrphanGenerationIds = Snapshot(expiredOrphanGenerationIds);
        DeferredOrphanGenerationIds = Snapshot(deferredOrphanGenerationIds);
        UncertainOrphanGenerationIds = Snapshot(uncertainOrphanGenerationIds);
    }

    public string? ExpectedCurrentGenerationId { get; }

    public IReadOnlyList<string> RetainedGenerationIds { get; }

    public IReadOnlyList<string> ExpiredManifestGenerationIds { get; }

    public IReadOnlyList<string> ExpiredOrphanGenerationIds { get; }

    public IReadOnlyList<string> DeferredOrphanGenerationIds { get; }

    public IReadOnlyList<string> UncertainOrphanGenerationIds { get; }

    private static IReadOnlyList<string> Snapshot(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<string>(values.ToArray());
    }
}
