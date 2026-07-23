using System.Globalization;
using System.Security.Cryptography;
using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Collects, validates, stages, and publishes one complete Trakt TV generation.
/// </summary>
public sealed class TvSyncService(
    ITraktAccessTokenProvider accessTokenProvider,
    ITraktTvClient traktClient,
    ITmdbTvEnrichmentService enrichmentService,
    ITvGenerationRepository generationRepository,
    ITraktOperationCoordinator operationCoordinator,
    TimeProvider timeProvider,
    TimeSpan metadataRefreshInterval) : ITvSyncService
{
    private const string CompletedStatus = "completed";

    private readonly TimeSpan metadataRefreshInterval = EnsurePositive(
        metadataRefreshInterval,
        nameof(metadataRefreshInterval));
    private readonly TvLifecycleEvaluator lifecycleEvaluator = new();
    private readonly TvSnapshotValidator snapshotValidator = new();

    public async Task<TvSyncResultDto> SyncAsync(
        TvGenerationKind kind,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        await using IAsyncDisposable lease = await operationCoordinator
            .AcquireAsync(cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset startedAt = UtcNow();
        string generationId = CreateGenerationId(startedAt);
        string accessToken = await accessTokenProvider
            .GetValidAccessTokenAsync(cancellationToken)
            .ConfigureAwait(false);
        PublishedTvGeneration? previousGeneration = await generationRepository
            .GetPublishedAsync(cancellationToken)
            .ConfigureAwait(false);
        TraktActivityCursor activityBefore = await traktClient
            .GetLastActivitiesAsync(accessToken, cancellationToken)
            .ConfigureAwait(false);
        TraktPagedResult<TraktWatchlistShow> watchlist = await traktClient
            .GetWatchlistAsync(accessToken, cancellationToken)
            .ConfigureAwait(false);
        TraktPagedResult<TraktWatchedShowProgress> progress = await traktClient
            .GetWatchedProgressAsync(accessToken, cancellationToken)
            .ConfigureAwait(false);
        ValidatePagination(watchlist, progress);

        SortedDictionary<long, SourceShow> currentSources = BuildCurrentSources(
            watchlist.Items,
            progress.Items);
        SortedDictionary<long, TvShow> previousShows = BuildPreviousIndex(previousGeneration);
        long[] catalogIds = currentSources.Keys
            .Concat(previousShows.Keys)
            .Distinct()
            .Order()
            .ToArray();

        List<TvShow> shows = [];
        List<TvLifecycleEvent> lifecycleEvents = [];
        List<string> enrichmentErrors = [];
        DateTimeOffset assembledAt = UtcNow();
        foreach (long traktId in catalogIds)
        {
            currentSources.TryGetValue(traktId, out SourceShow? source);
            previousShows.TryGetValue(traktId, out TvShow? previous);
            ShowAssembly assembly = source is null
                ? await AssembleAbsentAsync(
                    accessToken,
                    previous!,
                    assembledAt,
                    cancellationToken)
                : await AssembleCurrentAsync(
                    accessToken,
                    source,
                    previous,
                    assembledAt,
                    cancellationToken);

            TmdbTvEnrichmentResult enrichment = await enrichmentService
                .EnrichAsync(
                    assembly.Metadata,
                    assembly.Seasons.Select(season => season.SeasonNumber).ToArray(),
                    previous,
                    assembledAt,
                    cancellationToken)
                .ConfigureAwait(false);
            TvLifecycleDecision lifecycle = lifecycleEvaluator.Evaluate(
                previous,
                traktId,
                source is not null,
                source?.Watchlist is not null,
                assembly.AiredEpisodes,
                assembly.CompletedEpisodes,
                kind,
                generationId,
                assembledAt);
            TvShow show = CreateShow(
                generationId,
                source,
                previous,
                assembly,
                enrichment,
                lifecycle,
                assembledAt);
            shows.Add(show);
            if (lifecycle.Event is not null)
            {
                lifecycleEvents.Add(lifecycle.Event);
            }

            enrichmentErrors.AddRange(enrichment.Errors);
        }

        DateTimeOffset providerEnrichmentCompletedAt = UtcNow();
        TraktActivityCursor activityAfter = await traktClient
            .GetLastActivitiesAsync(accessToken, cancellationToken)
            .ConfigureAwait(false);
        DateTimeOffset completedAt = UtcNow();
        TvShow[] orderedShows = shows.OrderBy(show => show.TraktId).ToArray();
        TvLifecycleEvent[] orderedEvents = lifecycleEvents
            .OrderBy(lifecycleEvent => lifecycleEvent.TraktId)
            .ToArray();
        string[] orderedErrors = enrichmentErrors
            .Order(StringComparer.Ordinal)
            .ToArray();
        TvGenerationDraft draft = new(
            generationId,
            kind,
            startedAt,
            completedAt,
            activityBefore,
            activityAfter,
            watchlist.PageCount,
            watchlist.Items.Count,
            progress.PageCount,
            progress.Items.Count,
            TvSnapshotValidator.RequestContractVersion,
            TvSnapshotValidator.CreateRequestFilters(watchlist.PageSize),
            snapshotValidator.ComputeMembershipHash(orderedShows),
            snapshotValidator.ComputeProgressHash(orderedShows),
            orderedShows,
            orderedEvents,
            orderedErrors);
        snapshotValidator.Validate(draft);

        await generationRepository.StageAsync(draft, cancellationToken).ConfigureAwait(false);
        DateTimeOffset publishedAt = UtcNow();
        TvGenerationManifest manifest = TvGenerationManifest.CreatePhaseOne(
            draft,
            previousGeneration?.Manifest.GenerationId,
            publishedAt,
            providerEnrichmentCompletedAt,
            ResolveLastScheduledFullAt(previousGeneration));
        await generationRepository.PublishAsync(manifest, cancellationToken).ConfigureAwait(false);
        DateTimeOffset finishedAt = UtcNow();
        int providerFailures = orderedErrors.Count(IsProviderFailure);
        return new TvSyncResultDto(
            CompletedStatus,
            startedAt,
            finishedAt,
            generationId,
            FormatKind(kind),
            watchlist.Items.Count,
            progress.Items.Count,
            orderedShows.Length,
            providerFailures,
            manifest.MutationCapable,
            manifest.HealthReasons);
    }

    private async Task<ShowAssembly> AssembleCurrentAsync(
        string accessToken,
        SourceShow source,
        TvShow? previous,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        TraktDetailedShowProgress detailed = await traktClient
            .GetDetailedProgressAsync(accessToken, source.Ids.TraktId, cancellationToken)
            .ConfigureAwait(false);
        ValidateDetailedProgress(source, detailed, now);

        SortedSet<int> numberedSeasonNumbers = new(
            detailed.Seasons.Select(season => season.SeasonNumber));
        if (source.Progress?.NextEpisode is TraktSeasonEpisode nextEpisode)
        {
            if (nextEpisode.SeasonNumber <= 0)
            {
                throw Rejected("tv_schedule_summary_identity_conflict");
            }

            numberedSeasonNumbers.Add(nextEpisode.SeasonNumber);
        }

        Dictionary<int, IReadOnlyList<TraktSeasonEpisode>> schedules = [];
        IReadOnlyList<TraktSeasonEpisode> specials = await traktClient
            .GetSeasonAsync(accessToken, source.Ids.TraktId, 0, cancellationToken)
            .ConfigureAwait(false);
        schedules.Add(0, specials);
        foreach (int seasonNumber in numberedSeasonNumbers)
        {
            IReadOnlyList<TraktSeasonEpisode> schedule = await traktClient
                .GetSeasonAsync(
                    accessToken,
                    source.Ids.TraktId,
                    seasonNumber,
                    cancellationToken)
                .ConfigureAwait(false);
            schedules.Add(seasonNumber, schedule);
        }

        IReadOnlyList<TvSpecialEpisodeIdentity> specialIdentities = BuildSpecialIdentities(
            schedules[0]);
        IReadOnlyList<SeasonAssembly> seasonAssemblies = BuildSeasonAssemblies(
            source,
            detailed,
            numberedSeasonNumbers,
            schedules,
            now,
            out TvEpisodeProgress? lastWatched,
            out TvEpisodeProgress? next);
        TraktShowMetadata metadata = await ResolveMetadataAsync(
            accessToken,
            source.Ids,
            previous,
            now,
            requireExactSourceIds: true,
            cancellationToken);
        return new ShowAssembly(
            metadata,
            detailed.AiredEpisodes,
            detailed.CompletedEpisodes,
            lastWatched,
            next,
            seasonAssemblies,
            specialIdentities);
    }

    private async Task<ShowAssembly> AssembleAbsentAsync(
        string accessToken,
        TvShow previous,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        TraktShowIds previousIds = new(
            previous.TraktId,
            previous.TvdbId,
            previous.TmdbId,
            previous.ImdbId);
        TraktShowMetadata metadata = await ResolveMetadataAsync(
            accessToken,
            previousIds,
            previous,
            now,
            requireExactSourceIds: false,
            cancellationToken);
        IReadOnlyList<TraktSeasonEpisode> specialSchedule = await traktClient
            .GetSeasonAsync(
                accessToken,
                previous.TraktId,
                0,
                cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<TvSpecialEpisodeIdentity> specialIdentities =
            BuildSpecialIdentities(specialSchedule);
        SeasonAssembly[] seasons = previous.Seasons
            .OrderBy(season => season.SeasonNumber)
            .Select(season => new SeasonAssembly(
                season.SeasonNumber,
                season.AiredEpisodes,
                season.CompletedEpisodes,
                season.HasKnownFutureEpisode,
                season.Episodes))
            .ToArray();
        return new ShowAssembly(
            metadata,
            previous.AiredEpisodes,
            previous.CompletedEpisodes,
            previous.LastWatchedEpisode,
            previous.NextEpisode,
            seasons,
            specialIdentities);
    }

    private async Task<TraktShowMetadata> ResolveMetadataAsync(
        string accessToken,
        TraktShowIds sourceIds,
        TvShow? previous,
        DateTimeOffset now,
        bool requireExactSourceIds,
        CancellationToken cancellationToken)
    {
        bool sourceIdsMatch = previous is not null
            && previous.TraktId == sourceIds.TraktId
            && previous.TvdbId == sourceIds.TvdbId
            && previous.TmdbId == sourceIds.TmdbId
            && string.Equals(previous.ImdbId, sourceIds.ImdbId, StringComparison.Ordinal);
        bool canReuse = sourceIdsMatch
            && previous!.MetadataFetchedAt <= now
            && now - previous.MetadataFetchedAt < metadataRefreshInterval;
        if (canReuse)
        {
            return new TraktShowMetadata(
                sourceIds,
                previous!.Title,
                previous.Year,
                previous.Overview,
                previous.TraktStatus);
        }

        TraktShowMetadata metadata = await traktClient
            .GetShowMetadataAsync(accessToken, sourceIds.TraktId, cancellationToken)
            .ConfigureAwait(false);
        return requireExactSourceIds
            ? ValidateExactMetadata(metadata, sourceIds)
            : ValidateAndMergeAbsentMetadata(metadata, sourceIds);
    }

    private static IReadOnlyList<SeasonAssembly> BuildSeasonAssemblies(
        SourceShow source,
        TraktDetailedShowProgress detailed,
        IEnumerable<int> numberedSeasonNumbers,
        IReadOnlyDictionary<int, IReadOnlyList<TraktSeasonEpisode>> schedules,
        DateTimeOffset now,
        out TvEpisodeProgress? lastWatched,
        out TvEpisodeProgress? next)
    {
        Dictionary<int, TraktDetailedSeasonProgress> detailedBySeason = detailed.Seasons
            .ToDictionary(season => season.SeasonNumber);
        List<SeasonAssembly> result = [];
        Dictionary<(int SeasonNumber, int EpisodeNumber), TvEpisodeProgress> episodesByPosition = [];
        HashSet<long> traktEpisodeIds = [];
        HashSet<int> tvdbEpisodeIds = [];
        foreach (int seasonNumber in numberedSeasonNumbers.Order())
        {
            if (!schedules.TryGetValue(seasonNumber, out IReadOnlyList<TraktSeasonEpisode>? schedule))
            {
                throw Rejected("tv_schedule_season_missing");
            }

            Dictionary<int, TraktSeasonEpisode> scheduleByEpisode = BuildScheduleIndex(
                schedule,
                seasonNumber,
                traktEpisodeIds,
                tvdbEpisodeIds);
            detailedBySeason.TryGetValue(
                seasonNumber,
                out TraktDetailedSeasonProgress? detailedSeason);
            Dictionary<int, TraktDetailedEpisodeProgress> detailedEpisodes = detailedSeason?.Episodes
                .ToDictionary(episode => episode.EpisodeNumber)
                ?? [];
            foreach (int episodeNumber in detailedEpisodes.Keys)
            {
                if (!scheduleByEpisode.ContainsKey(episodeNumber))
                {
                    throw Rejected("tv_schedule_episode_missing");
                }
            }

            List<TvEpisodeProgress> episodes = [];
            foreach (TraktSeasonEpisode scheduleEpisode in scheduleByEpisode.Values
                         .OrderBy(episode => episode.EpisodeNumber))
            {
                detailedEpisodes.TryGetValue(
                    scheduleEpisode.EpisodeNumber,
                    out TraktDetailedEpisodeProgress? progressEpisode);
                TvEpisodeProgress episode = new(
                    scheduleEpisode.TraktEpisodeId,
                    scheduleEpisode.TvdbId,
                    scheduleEpisode.SeasonNumber,
                    scheduleEpisode.EpisodeNumber,
                    scheduleEpisode.Title,
                    scheduleEpisode.FirstAired,
                    progressEpisode?.Completed ?? false,
                    progressEpisode?.Completed == true ? progressEpisode.LastWatchedAt : null);
                episodes.Add(episode);
                episodesByPosition.Add(
                    (episode.SeasonNumber, episode.EpisodeNumber),
                    episode);
            }

            int airedEpisodes = detailedSeason?.AiredEpisodes ?? 0;
            int completedEpisodes = detailedSeason?.CompletedEpisodes ?? 0;
            bool hasKnownFutureEpisode = source.Progress?.NextEpisode?.SeasonNumber == seasonNumber
                || scheduleByEpisode.Values.Any(episode =>
                    episode.FirstAired is null || episode.FirstAired > now);
            result.Add(new SeasonAssembly(
                seasonNumber,
                airedEpisodes,
                completedEpisodes,
                hasKnownFutureEpisode,
                episodes));
        }

        lastWatched = ResolveSummaryEpisode(
            source.Progress?.LastEpisode,
            episodesByPosition,
            expectedWatched: true);
        next = ResolveSummaryEpisode(
            source.Progress?.NextEpisode,
            episodesByPosition,
            expectedWatched: false);
        return result;
    }

    private static Dictionary<int, TraktSeasonEpisode> BuildScheduleIndex(
        IReadOnlyList<TraktSeasonEpisode> schedule,
        int expectedSeason,
        HashSet<long> traktEpisodeIds,
        HashSet<int> tvdbEpisodeIds)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        Dictionary<int, TraktSeasonEpisode> result = [];
        foreach (TraktSeasonEpisode? candidate in schedule)
        {
            if (candidate is null
                || candidate.SeasonNumber != expectedSeason
                || candidate.EpisodeNumber <= 0
                || candidate.TraktEpisodeId <= 0
                || candidate.TvdbId is <= 0
                || candidate.Title is not null && !IsCanonical(candidate.Title)
                || candidate.FirstAired is DateTimeOffset airedAt && airedAt.Offset != TimeSpan.Zero)
            {
                throw Rejected("tv_schedule_episode_invalid");
            }

            if (!result.TryAdd(candidate.EpisodeNumber, candidate))
            {
                throw Rejected("tv_schedule_episode_duplicate");
            }

            if (!traktEpisodeIds.Add(candidate.TraktEpisodeId))
            {
                throw Rejected("tv_schedule_episode_identity_duplicate");
            }

            TraktSeasonEpisode normalized = candidate;
            if (candidate.TvdbId is int tvdbId && !tvdbEpisodeIds.Add(tvdbId))
            {
                normalized = candidate with { TvdbId = null };
            }

            result[candidate.EpisodeNumber] = normalized;
        }

        return result;
    }

    private static IReadOnlyList<TvSpecialEpisodeIdentity> BuildSpecialIdentities(
        IReadOnlyList<TraktSeasonEpisode> schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        Dictionary<int, TvSpecialEpisodeIdentity> identities = [];
        HashSet<int> episodeNumbers = [];
        HashSet<long> traktIds = [];
        HashSet<int> tvdbIds = [];
        foreach (TraktSeasonEpisode? candidate in schedule)
        {
            if (candidate is null
                || candidate.SeasonNumber != 0
                || candidate.EpisodeNumber <= 0
                || candidate.TraktEpisodeId <= 0
                || candidate.TvdbId is int tvdbId && tvdbId <= 0
                || !episodeNumbers.Add(candidate.EpisodeNumber)
                || !traktIds.Add(candidate.TraktEpisodeId)
                || candidate.TvdbId is int identifiedTvdbId && !tvdbIds.Add(identifiedTvdbId))
            {
                return [];
            }

            if (candidate.TvdbId is not int specialTvdbId)
            {
                continue;
            }

            identities.Add(
                candidate.EpisodeNumber,
                new TvSpecialEpisodeIdentity(
                    candidate.TraktEpisodeId,
                    specialTvdbId,
                    0,
                    candidate.EpisodeNumber));
        }

        return identities.Values.OrderBy(identity => identity.EpisodeNumber).ToArray();
    }

    private static TvEpisodeProgress? ResolveSummaryEpisode(
        TraktSeasonEpisode? summary,
        IReadOnlyDictionary<(int SeasonNumber, int EpisodeNumber), TvEpisodeProgress> episodes,
        bool expectedWatched)
    {
        if (summary is null)
        {
            return null;
        }

        if (!episodes.TryGetValue(
                (summary.SeasonNumber, summary.EpisodeNumber),
                out TvEpisodeProgress? episode)
            || episode.TraktEpisodeId != summary.TraktEpisodeId
            || episode.TvdbId is int episodeTvdbId
                && summary.TvdbId is int summaryTvdbId
                && episodeTvdbId != summaryTvdbId
            || episode.Watched != expectedWatched)
        {
            throw Rejected("tv_schedule_summary_identity_conflict");
        }

        return episode;
    }

    private static TvShow CreateShow(
        string generationId,
        SourceShow? source,
        TvShow? previous,
        ShowAssembly assembly,
        TmdbTvEnrichmentResult enrichment,
        TvLifecycleDecision lifecycle,
        DateTimeOffset now)
    {
        Dictionary<int, TvProviderAvailability> availability = enrichment.SeasonAvailability
            .ToDictionary(item => item.Key, item => item.Value);
        TvSeasonProgress[] seasons = assembly.Seasons
            .OrderBy(season => season.SeasonNumber)
            .Select(season => new TvSeasonProgress(
                season.SeasonNumber,
                season.AiredEpisodes,
                season.CompletedEpisodes,
                season.HasKnownFutureEpisode,
                availability.TryGetValue(
                    season.SeasonNumber,
                    out TvProviderAvailability? seasonAvailability)
                        ? seasonAvailability
                        : throw Rejected("tv_enrichment_season_missing"),
                season.Episodes))
            .ToArray();
        DateTimeOffset addedAt = previous?.AddedAt
            ?? source?.Watchlist?.ListedAt
            ?? now;
        string lastLifecycleEvent = lifecycle.Event?.Id
            ?? previous?.LastLifecycleEvent
            ?? throw Rejected("tv_lifecycle_event_missing");
        return new TvShow(
            $"tv-trakt-{assembly.Metadata.Ids.TraktId}",
            assembly.Metadata.Ids.TraktId,
            enrichment.TvdbId,
            enrichment.TmdbId,
            enrichment.ImdbId,
            enrichment.IdentityStatus,
            enrichment.Title,
            enrichment.Year,
            enrichment.Overview,
            enrichment.PosterUrl,
            enrichment.BackdropUrl,
            assembly.Metadata.Status ?? throw Rejected("tv_metadata_status_missing"),
            source?.Watchlist is not null,
            assembly.AiredEpisodes,
            assembly.CompletedEpisodes,
            assembly.LastWatchedEpisode,
            assembly.NextEpisode,
            seasons,
            assembly.SpecialEpisodeIdentities,
            enrichment.Availability,
            lifecycle.State,
            lastLifecycleEvent,
            lifecycle.LifecycleVersion,
            lifecycle.MissingScheduledConfirmations,
            addedAt,
            now,
            enrichment.MetadataFetchedAt,
            generationId,
            previous?.LegacySourceId);
    }

    private static SortedDictionary<long, SourceShow> BuildCurrentSources(
        IReadOnlyList<TraktWatchlistShow> watchlist,
        IReadOnlyList<TraktWatchedShowProgress> progress)
    {
        SortedDictionary<long, SourceShow> result = [];
        foreach (TraktWatchlistShow? item in watchlist)
        {
            ValidateWatchlistItem(item);
            if (!result.TryAdd(
                    item.Ids.TraktId,
                    new SourceShow(item.Ids, item, null)))
            {
                throw Rejected("tv_source_watchlist_duplicate");
            }
        }

        foreach (TraktWatchedShowProgress? item in progress)
        {
            ValidateProgressItem(item);
            if (result.TryGetValue(item.Ids.TraktId, out SourceShow? existing))
            {
                if (existing.Progress is not null)
                {
                    throw Rejected("tv_source_progress_duplicate");
                }

                if (existing.Ids != item.Ids)
                {
                    throw Rejected("tv_source_identity_conflict");
                }

                result[item.Ids.TraktId] = existing with { Progress = item };
            }
            else
            {
                result.Add(item.Ids.TraktId, new SourceShow(item.Ids, null, item));
            }
        }

        return result;
    }

    private static SortedDictionary<long, TvShow> BuildPreviousIndex(
        PublishedTvGeneration? previousGeneration)
    {
        SortedDictionary<long, TvShow> result = [];
        if (previousGeneration is null)
        {
            return result;
        }

        foreach (TvShow? show in previousGeneration.Shows)
        {
            if (show is null || show.TraktId <= 0 || !result.TryAdd(show.TraktId, show))
            {
                throw new TvPublishedGenerationInvalidException(
                    "tv_published_show_identity_invalid");
            }
        }

        return result;
    }

    private static void ValidateDetailedProgress(
        SourceShow source,
        TraktDetailedShowProgress detailed,
        DateTimeOffset now)
    {
        if (detailed is null
            || detailed.AiredEpisodes < 0
            || detailed.CompletedEpisodes < 0
            || detailed.CompletedEpisodes > detailed.AiredEpisodes)
        {
            throw Rejected("tv_detailed_progress_invalid");
        }

        if (source.Progress is not null
            && (source.Progress.AiredEpisodes != detailed.AiredEpisodes
                || source.Progress.CompletedEpisodes != detailed.CompletedEpisodes))
        {
            throw Rejected("tv_detailed_progress_totals_conflict");
        }

        HashSet<int> seasons = [];
        int totalAired = 0;
        int totalCompleted = 0;
        foreach (TraktDetailedSeasonProgress? season in detailed.Seasons)
        {
            if (season is null
                || season.SeasonNumber <= 0
                || season.AiredEpisodes < 0
                || season.CompletedEpisodes < 0
                || season.CompletedEpisodes > season.AiredEpisodes
                || !seasons.Add(season.SeasonNumber))
            {
                throw Rejected("tv_detailed_season_invalid");
            }

            HashSet<int> episodes = [];
            int completedCount = 0;
            foreach (TraktDetailedEpisodeProgress? episode in season.Episodes)
            {
                if (episode is null
                    || episode.SeasonNumber != season.SeasonNumber
                    || episode.EpisodeNumber <= 0
                    || !episodes.Add(episode.EpisodeNumber)
                    || episode.LastWatchedAt is DateTimeOffset watchedAt
                        && (watchedAt.Offset != TimeSpan.Zero || watchedAt > now)
                    || !episode.Completed && episode.LastWatchedAt is not null)
                {
                    throw Rejected("tv_detailed_episode_invalid");
                }

                if (episode.Completed)
                {
                    completedCount++;
                }
            }

            if (completedCount != season.CompletedEpisodes
                || season.AiredEpisodes > season.Episodes.Count)
            {
                throw Rejected("tv_detailed_season_counts_invalid");
            }

            totalAired = checked(totalAired + season.AiredEpisodes);
            totalCompleted = checked(totalCompleted + season.CompletedEpisodes);
        }

        if (totalAired != detailed.AiredEpisodes
            || totalCompleted != detailed.CompletedEpisodes)
        {
            throw Rejected("tv_detailed_progress_totals_invalid");
        }
    }

    private static TraktShowMetadata ValidateExactMetadata(
        TraktShowMetadata metadata,
        TraktShowIds sourceIds)
    {
        if (metadata is null
            || metadata.Ids != sourceIds
            || !HasValidMetadataShape(metadata))
        {
            throw Rejected("tv_metadata_identity_conflict");
        }

        return metadata;
    }

    private static TraktShowMetadata ValidateAndMergeAbsentMetadata(
        TraktShowMetadata metadata,
        TraktShowIds previousIds)
    {
        if (metadata is null
            || metadata.Ids.TraktId != previousIds.TraktId
            || !OptionalIdentityCompatible(metadata.Ids.TvdbId, previousIds.TvdbId)
            || !OptionalIdentityCompatible(metadata.Ids.TmdbId, previousIds.TmdbId)
            || !OptionalIdentityCompatible(metadata.Ids.ImdbId, previousIds.ImdbId)
            || !HasValidMetadataShape(metadata))
        {
            throw Rejected("tv_metadata_identity_conflict");
        }

        TraktShowIds mergedIds = new(
            metadata.Ids.TraktId,
            metadata.Ids.TvdbId ?? previousIds.TvdbId,
            metadata.Ids.TmdbId ?? previousIds.TmdbId,
            metadata.Ids.ImdbId ?? previousIds.ImdbId);
        return metadata with { Ids = mergedIds };
    }

    private static bool HasValidMetadataShape(TraktShowMetadata metadata)
    {
        return HasValidIds(metadata.Ids)
            && IsCanonical(metadata.Title)
            && metadata.Year is not <= 0
            && (metadata.Overview is null || IsCanonical(metadata.Overview))
            && IsCanonical(metadata.Status);
    }

    private static bool OptionalIdentityCompatible<T>(T? current, T? previous)
        where T : struct
    {
        return current is null || previous is null || EqualityComparer<T>.Default.Equals(current.Value, previous.Value);
    }

    private static bool OptionalIdentityCompatible(string? current, string? previous)
    {
        return current is null
            || previous is null
            || string.Equals(current, previous, StringComparison.Ordinal);
    }

    private static void ValidateWatchlistItem(TraktWatchlistShow? item)
    {
        if (item is null
            || !HasValidIds(item.Ids)
            || !IsCanonical(item.Title)
            || item.Year is <= 0
            || item.ListedAt == default
            || item.ListedAt.Offset != TimeSpan.Zero)
        {
            throw Rejected("tv_source_watchlist_invalid");
        }
    }

    private static void ValidateProgressItem(TraktWatchedShowProgress? item)
    {
        if (item is null
            || !HasValidIds(item.Ids)
            || !IsCanonical(item.Title)
            || item.Year is <= 0
            || item.AiredEpisodes < 0
            || item.CompletedEpisodes < 0
            || item.CompletedEpisodes > item.AiredEpisodes
            || !HasValidSummaryEpisode(item.NextEpisode)
            || !HasValidSummaryEpisode(item.LastEpisode))
        {
            throw Rejected("tv_source_progress_invalid");
        }
    }

    private static bool HasValidIds(TraktShowIds? ids)
    {
        return ids is not null
            && ids.TraktId > 0
            && ids.TvdbId is not <= 0
            && ids.TmdbId is not <= 0
            && (ids.ImdbId is null
                || ids.ImdbId.Length > 2
                    && ids.ImdbId.StartsWith("tt", StringComparison.Ordinal)
                    && ids.ImdbId.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0);
    }

    private static bool HasValidSummaryEpisode(TraktSeasonEpisode? episode)
    {
        return episode is null
            || episode.TraktEpisodeId > 0
                && episode.TvdbId is not <= 0
                && episode.SeasonNumber > 0
                && episode.EpisodeNumber > 0;
    }

    private static void ValidatePagination(
        TraktPagedResult<TraktWatchlistShow> watchlist,
        TraktPagedResult<TraktWatchedShowProgress> progress)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        ArgumentNullException.ThrowIfNull(progress);
        if (watchlist.PageSize != progress.PageSize)
        {
            throw Rejected("tv_source_pagination_mismatch");
        }
    }

    private static bool IsProviderFailure(string error)
    {
        return error.Contains("_providers;", StringComparison.Ordinal);
    }

    private static DateTimeOffset? ResolveLastScheduledFullAt(
        PublishedTvGeneration? previousGeneration)
    {
        return previousGeneration?.Manifest.LastScheduledFullAt
            ?? previousGeneration?.Manifest.PublishedAt;
    }

    private static string FormatKind(TvGenerationKind kind)
    {
        return kind switch
        {
            TvGenerationKind.ScheduledFull => "scheduled_full",
            TvGenerationKind.ActivityFull => "activity_full",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static string CreateGenerationId(DateTimeOffset timestamp)
    {
        Span<byte> suffix = stackalloc byte[16];
        RandomNumberGenerator.Fill(suffix);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"tv-{timestamp:yyyyMMddHHmmssfff}-{Convert.ToHexString(suffix).ToLowerInvariant()}");
    }

    private DateTimeOffset UtcNow()
    {
        return timeProvider.GetUtcNow().ToUniversalTime();
    }

    private static bool IsCanonical(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    private static TimeSpan EnsurePositive(TimeSpan value, string parameterName)
    {
        return value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(parameterName);
    }

    private static TvSourceSnapshotRejectedException Rejected(string reason)
    {
        return new TvSourceSnapshotRejectedException(reason);
    }

    private sealed record SourceShow(
        TraktShowIds Ids,
        TraktWatchlistShow? Watchlist,
        TraktWatchedShowProgress? Progress);

    private sealed record SeasonAssembly(
        int SeasonNumber,
        int AiredEpisodes,
        int CompletedEpisodes,
        bool HasKnownFutureEpisode,
        IReadOnlyList<TvEpisodeProgress> Episodes);

    private sealed record ShowAssembly(
        TraktShowMetadata Metadata,
        int AiredEpisodes,
        int CompletedEpisodes,
        TvEpisodeProgress? LastWatchedEpisode,
        TvEpisodeProgress? NextEpisode,
        IReadOnlyList<SeasonAssembly> Seasons,
        IReadOnlyList<TvSpecialEpisodeIdentity> SpecialEpisodeIdentities);
}
