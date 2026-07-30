namespace Watchlist.Application;

/// <summary>
/// Summarizes one stored TV generation manifest for retention planning.
/// </summary>
public sealed record TvStoredGenerationSummary(
    string GenerationId,
    DateTimeOffset PublishedAt);

/// <summary>
/// Captures persistence-neutral TV generation state for retention planning.
/// </summary>
public sealed record TvGenerationRetentionSnapshot(
    string? CurrentGenerationId,
    IReadOnlyList<TvStoredGenerationSummary> Manifests,
    IReadOnlyList<string> OrphanGenerationIds);
