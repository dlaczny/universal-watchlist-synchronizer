namespace Watchlist.Application;

/// <summary>
/// Stages complete TV generations and advances their published pointer last.
/// </summary>
public interface ITvGenerationRepository
{
    Task StageAsync(TvGenerationDraft draft, CancellationToken cancellationToken);

    Task PublishAsync(TvGenerationManifest manifest, CancellationToken cancellationToken);

    Task<PublishedTvGeneration?> GetPublishedAsync(CancellationToken cancellationToken);
}
