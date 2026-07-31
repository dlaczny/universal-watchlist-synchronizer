namespace Watchlist.Application;

/// <summary>
/// Coordinates required and best-effort pruning of immutable TV generations.
/// </summary>
public interface ITvGenerationRetentionService
{
    Task PruneRequiredAsync(CancellationToken cancellationToken);

    Task PruneBestEffortAsync(CancellationToken cancellationToken);
}
