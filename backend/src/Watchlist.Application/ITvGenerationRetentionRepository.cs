namespace Watchlist.Application;

public sealed record TvGenerationRetentionDeleteResult(
    long ShowDocumentsDeleted,
    long LifecycleEventsDeleted,
    long ManifestsDeleted);

public interface ITvGenerationRetentionRepository
{
    Task<TvGenerationRetentionSnapshot> ReadSnapshotAsync(
        CancellationToken cancellationToken);

    Task<TvGenerationRetentionDeleteResult> ApplyAsync(
        TvGenerationRetentionPlan plan,
        CancellationToken cancellationToken);
}
