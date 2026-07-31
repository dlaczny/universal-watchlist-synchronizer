using Microsoft.Extensions.Logging;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

/// <summary>
/// Coordinates deterministic TV generation retention planning and persistence.
/// </summary>
public sealed class TvGenerationRetentionService(
    ITvGenerationRetentionRepository repository,
    TvGenerationRetentionPlanner planner,
    TvGenerationRetentionPolicy policy,
    TimeProvider timeProvider,
    ILogger<TvGenerationRetentionService> logger) : ITvGenerationRetentionService
{
    public Task PruneRequiredAsync(CancellationToken cancellationToken)
    {
        return RunRequiredAsync("pre_sync", cancellationToken);
    }

    public async Task PruneBestEffortAsync(CancellationToken cancellationToken)
    {
        const string mode = "post_publish";
        try
        {
            await RunCoreAsync(mode, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "TV generation retention {Mode} deferred with code {Code} and exception type {ExceptionType}.",
                mode,
                "tv_generation_retention_deferred",
                exception.GetType().Name);
        }
    }

    private async Task RunRequiredAsync(
        string mode,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunCoreAsync(mode, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TvGenerationRetentionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "TV generation retention {Mode} failed with code {Code} and exception type {ExceptionType}.",
                mode,
                TvGenerationRetentionException.StableCode,
                exception.GetType().Name);
            throw new TvGenerationRetentionException(exception);
        }
    }

    private async Task RunCoreAsync(
        string mode,
        CancellationToken cancellationToken)
    {
        TvGenerationRetentionSnapshot snapshot = await repository
            .ReadSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        TvGenerationRetentionPlan plan = planner.Create(
            snapshot,
            policy,
            timeProvider.GetUtcNow().ToUniversalTime());
        TvGenerationRetentionDeleteResult result = await repository
            .ApplyAsync(plan, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "TV retention {Mode} completed. Retained={Retained}; ExpiredManifests={ExpiredManifests}; ExpiredOrphans={ExpiredOrphans}; DeferredOrphans={DeferredOrphans}; UncertainOrphans={UncertainOrphans}; ShowDocumentsDeleted={ShowDocumentsDeleted}; LifecycleEventsDeleted={LifecycleEventsDeleted}; ManifestsDeleted={ManifestsDeleted}.",
            mode,
            plan.RetainedGenerationIds.Count,
            plan.ExpiredManifestGenerationIds.Count,
            plan.ExpiredOrphanGenerationIds.Count,
            plan.DeferredOrphanGenerationIds.Count,
            plan.UncertainOrphanGenerationIds.Count,
            result.ShowDocumentsDeleted,
            result.LifecycleEventsDeleted,
            result.ManifestsDeleted);
    }
}
