using System.Globalization;
using System.Text.RegularExpressions;

namespace Watchlist.Application;

/// <summary>
/// Creates deterministic retention decisions without persistence side effects.
/// </summary>
public sealed partial class TvGenerationRetentionPlanner
{
    public TvGenerationRetentionPlan Create(
        TvGenerationRetentionSnapshot snapshot,
        TvGenerationRetentionPolicy policy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(policy);

        if (now == default || now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The planning timestamp must be a non-default UTC value.", nameof(now));
        }

        TvStoredGenerationSummary[] orderedManifests = snapshot.Manifests
            .OrderByDescending(item => item.PublishedAt)
            .ThenByDescending(item => item.GenerationId, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> manifestGenerationIds = GetUniqueManifestIds(orderedManifests);

        string[] orphanGenerationIds = snapshot.OrphanGenerationIds
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (orphanGenerationIds.Any(manifestGenerationIds.Contains))
        {
            throw new InvalidOperationException("tv_generation_retention_orphan_manifest_overlap");
        }

        if (snapshot.CurrentGenerationId is null)
        {
            return new TvGenerationRetentionPlan(
                null,
                orderedManifests.Select(item => item.GenerationId).ToArray(),
                [],
                [],
                [],
                orphanGenerationIds);
        }

        string currentGenerationId = snapshot.CurrentGenerationId;
        if (!orderedManifests.Any(
                item => string.Equals(item.GenerationId, currentGenerationId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("tv_generation_retention_current_manifest_missing");
        }

        HashSet<string> retainedGenerationIds = new(StringComparer.Ordinal)
        {
            currentGenerationId
        };
        List<string> expiredManifestGenerationIds = [];
        DateTimeOffset oldestRetainedAt = now - policy.MaxAge;

        foreach (TvStoredGenerationSummary manifest in orderedManifests)
        {
            if (string.Equals(manifest.GenerationId, currentGenerationId, StringComparison.Ordinal))
            {
                continue;
            }

            if (manifest.PublishedAt >= oldestRetainedAt
                && retainedGenerationIds.Count < policy.MaxGenerations)
            {
                retainedGenerationIds.Add(manifest.GenerationId);
            }
            else
            {
                expiredManifestGenerationIds.Add(manifest.GenerationId);
            }
        }

        List<string> expiredOrphanGenerationIds = [];
        List<string> deferredOrphanGenerationIds = [];
        List<string> uncertainOrphanGenerationIds = [];
        DateTimeOffset orphanExpirationBoundary = now - policy.OrphanGracePeriod;

        foreach (string orphanGenerationId in orphanGenerationIds)
        {
            if (!TryParseCreationTime(orphanGenerationId, out DateTimeOffset createdAt))
            {
                uncertainOrphanGenerationIds.Add(orphanGenerationId);
            }
            else if (createdAt <= orphanExpirationBoundary)
            {
                expiredOrphanGenerationIds.Add(orphanGenerationId);
            }
            else
            {
                deferredOrphanGenerationIds.Add(orphanGenerationId);
            }
        }

        return new TvGenerationRetentionPlan(
            currentGenerationId,
            SortOrdinal(retainedGenerationIds),
            SortOrdinal(expiredManifestGenerationIds),
            expiredOrphanGenerationIds,
            deferredOrphanGenerationIds,
            uncertainOrphanGenerationIds);
    }

    private static HashSet<string> GetUniqueManifestIds(
        IReadOnlyList<TvStoredGenerationSummary> manifests)
    {
        HashSet<string> generationIds = new(StringComparer.Ordinal);
        foreach (TvStoredGenerationSummary manifest in manifests)
        {
            if (!generationIds.Add(manifest.GenerationId))
            {
                throw new InvalidOperationException("tv_generation_retention_manifest_duplicate");
            }
        }

        return generationIds;
    }

    private static string[] SortOrdinal(IEnumerable<string> generationIds)
    {
        return generationIds.Order(StringComparer.Ordinal).ToArray();
    }

    private static bool TryParseCreationTime(
        string generationId,
        out DateTimeOffset createdAt)
    {
        createdAt = default;
        Match match = ProductionGenerationIdRegex().Match(generationId);
        return match.Success
            && DateTimeOffset.TryParseExact(
                match.Groups["timestamp"].Value,
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out createdAt);
    }

    [GeneratedRegex(
        "^tv-(?<timestamp>[0-9]{17})-[0-9a-f]{32}\\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProductionGenerationIdRegex();
}
