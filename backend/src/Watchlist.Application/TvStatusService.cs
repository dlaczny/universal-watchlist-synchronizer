using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class TvStatusService(
    ITvGenerationRepository generationRepository,
    ITraktConnectionService traktConnectionService,
    TimeProvider timeProvider) : ITvStatusService
{
    private static readonly IReadOnlyList<string> PhaseOneHealthReasons =
        ["plex_history_phase_not_implemented", "worker_tv_mutation_disabled"];

    public async Task<TvSyncStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        TraktConnectionStatusDto connection = await traktConnectionService.GetStatusAsync(cancellationToken);
        PublishedTvGeneration? published = await generationRepository.GetPublishedAsync(cancellationToken);
        if (published is null)
        {
            return new TvSyncStatusDto(
                connection.Status,
                null,
                null,
                null,
                0,
                0,
                0,
                0,
                false,
                PhaseOneHealthReasons);
        }

        TvGenerationManifest manifest = published.Manifest;
        DateTimeOffset? lastScheduledFullAt = manifest.LastScheduledFullAt;
        return new TvSyncStatusDto(
            connection.Status,
            null,
            lastScheduledFullAt,
            lastScheduledFullAt is DateTimeOffset publishedAt
                ? timeProvider.GetUtcNow() - publishedAt
                : null,
            published.Shows.Count(show => show.LifecycleState == TvLifecycleState.Active),
            published.Shows.Count(show => show.LifecycleState == TvLifecycleState.CaughtUp),
            published.Shows.Count(show => show.LifecycleState == TvLifecycleState.SourceRemoved),
            manifest.EnrichmentErrors.Count,
            manifest.MutationCapable,
            manifest.HealthReasons);
    }
}
