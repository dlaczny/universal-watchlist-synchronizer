using Watchlist.Domain;

namespace Watchlist.Application;

public sealed class TvExportService(ITvGenerationRepository generationRepository) : ITvExportService
{
    public async Task<WorkerTvSnapshotDto?> GetTvSyncSnapshotAsync(CancellationToken cancellationToken)
    {
        PublishedTvGeneration? published = await generationRepository.GetPublishedAsync(cancellationToken);
        if (published is null)
        {
            return null;
        }

        TvGenerationManifest manifest = published.Manifest;
        return new WorkerTvSnapshotDto(
            "1",
            manifest.GenerationId,
            manifest.PublishedAt,
            manifest.CompletedAt,
            ToApiValue(manifest.Kind),
            manifest.MutationCapable,
            manifest.HealthReasons,
            new WorkerTvPlexHistoryDto(false, false, null, null, null, null, null, null),
            published.Shows.OrderBy(show => show.TraktId)
                .Select(show => ToShow(show, manifest.CompletedAt))
                .ToArray(),
            []);
    }

    private static WorkerTvShowDto ToShow(TvShow show, DateTimeOffset generatedAt)
    {
        List<WorkerTvSeasonDto> seasons = show.Seasons
            .OrderBy(season => season.SeasonNumber)
            .Select(season => ToSeason(season, show.LifecycleState, generatedAt))
            .ToList();
        if (show.SpecialEpisodeIdentities.Count > 0)
        {
            if (show.SpecialEpisodeIdentities.Any(episode => episode.TraktEpisodeId <= 0))
            {
                throw new TvPublishedGenerationInvalidException("tv_export_episode_identity_missing");
            }

            seasons.Insert(0, new WorkerTvSeasonDto(
                0,
                0,
                0,
                false,
                [],
                "not_authorized",
                show.SpecialEpisodeIdentities.OrderBy(episode => episode.EpisodeNumber)
                    .Select(episode => new WorkerTvEpisodeDto(
                        episode.TraktEpisodeId,
                        episode.SeasonNumber,
                        episode.EpisodeNumber,
                        episode.TvdbId,
                        null,
                        null,
                        false,
                        false,
                        null,
                        null,
                        null,
                        null))
                    .ToArray()));
        }
        bool activeOrCaughtUp = show.LifecycleState is TvLifecycleState.Active or TvLifecycleState.CaughtUp;
        List<string> blockers = [TvBlockerCodes.Phase1ReadOnly];
        if (show.IdentityStatus == TvIdentityStatus.Missing)
        {
            blockers.Add(TvBlockerCodes.IdentityMissingTvdb);
        }
        else if (show.IdentityStatus == TvIdentityStatus.Conflict)
        {
            blockers.Add(TvBlockerCodes.IdentityConflict);
        }

        return new WorkerTvShowDto(
            show.TraktId,
            show.TvdbId,
            show.TmdbId,
            show.ImdbId,
            show.Title,
            show.Year,
            ToApiValue(show.IdentityStatus),
            show.InWatchlist,
            ToApiValue(show.LifecycleState),
            show.LifecycleVersion,
            show.TraktStatus,
            show.AiredEpisodes,
            show.CompletedEpisodes,
            ToNullableEpisode(show.LastWatchedEpisode, generatedAt),
            ToNullableEpisode(show.NextEpisode, generatedAt),
            activeOrCaughtUp,
            activeOrCaughtUp,
            show.InWatchlist || show.CompletedEpisodes < show.AiredEpisodes,
            seasons,
            ToAvailability(show.Availability),
            blockers);
    }

    private static WorkerTvSeasonDto ToSeason(
        TvSeasonProgress season,
        TvLifecycleState lifecycleState,
        DateTimeOffset generatedAt)
    {
        IReadOnlyList<WorkerTvEpisodeDto> episodes = season.Episodes
            .OrderBy(episode => episode.EpisodeNumber)
            .Select(episode => ToEpisode(episode, generatedAt))
            .ToArray();
        if (episodes.Any(episode => episode.TraktEpisodeId <= 0))
        {
            throw new TvPublishedGenerationInvalidException("tv_export_episode_identity_missing");
        }

        bool monitoredDesired = lifecycleState is TvLifecycleState.Active or TvLifecycleState.CaughtUp;
        return new WorkerTvSeasonDto(
            season.SeasonNumber,
            season.AiredEpisodes,
            season.CompletedEpisodes,
            monitoredDesired,
            episodes.Where(episode => episode.Aired && !episode.Watched)
                .Select(episode => episode.EpisodeNumber)
                .ToArray(),
            "not_authorized",
            episodes);
    }

    private static WorkerTvEpisodeDto? ToNullableEpisode(
        TvEpisodeProgress? episode,
        DateTimeOffset generatedAt)
    {
        return episode is null ? null : ToEpisode(episode, generatedAt);
    }

    private static WorkerTvEpisodeDto ToEpisode(TvEpisodeProgress episode, DateTimeOffset generatedAt)
    {
        if (episode.TraktEpisodeId <= 0)
        {
            throw new TvPublishedGenerationInvalidException("tv_export_episode_identity_missing");
        }

        return new WorkerTvEpisodeDto(
            episode.TraktEpisodeId,
            episode.SeasonNumber,
            episode.EpisodeNumber,
            episode.TvdbId,
            episode.Title,
            episode.AiredAt,
            episode.AiredAt is DateTimeOffset airedAt && airedAt <= generatedAt,
            episode.Watched,
            episode.WatchedAt,
            null,
            null,
            null);
    }

    private static TvProviderAvailabilityDto ToAvailability(TvProviderAvailability availability)
    {
        return new TvProviderAvailabilityDto(
            availability.State switch
            {
                TvProviderState.Available => "available",
                TvProviderState.ConfirmedUnavailable => "confirmed_unavailable",
                TvProviderState.Stale => "stale",
                _ => "unknown"
            },
            availability.Region,
            availability.FetchedAt,
            availability.Link,
            availability.Offers.OrderBy(offer => offer.Category).ThenBy(offer => offer.ProviderId)
                .Select(offer => new TvProviderOfferDto(
                    offer.ProviderId,
                    offer.ProviderName,
                    offer.Category.ToString().ToLowerInvariant(),
                    offer.LogoUrl))
                .ToArray());
    }

    private static string ToApiValue(TvGenerationKind kind) => kind switch
    {
        TvGenerationKind.ScheduledFull => "scheduled_full",
        TvGenerationKind.ActivityFull => "activity_full",
        _ => "unknown"
    };

    private static string ToApiValue(TvIdentityStatus status) => status switch
    {
        TvIdentityStatus.Verified => "verified",
        TvIdentityStatus.Missing => "missing",
        TvIdentityStatus.Conflict => "conflict",
        TvIdentityStatus.LegacyUnresolved => "legacy_unresolved",
        _ => "unknown"
    };

    private static string ToApiValue(TvLifecycleState state) => state switch
    {
        TvLifecycleState.Active => "active",
        TvLifecycleState.CaughtUp => "caught_up",
        TvLifecycleState.SourceRemoved => "source_removed",
        TvLifecycleState.TerminalCleanupPending => "terminal_cleanup_pending",
        TvLifecycleState.RetiredTerminal => "retired_terminal",
        _ => "unknown"
    };
}
