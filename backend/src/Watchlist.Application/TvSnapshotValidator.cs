using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Validates complete TV generations and computes their canonical source hashes.
/// </summary>
public sealed class TvSnapshotValidator
{
    public const string RequestContractVersion = "trakt-tv-v1";

    private const int MaximumPageSize = 100;
    private const string ValidationStatus = "valid";

    /// <summary>
    /// Creates the exact Trakt TV request filters represented by version one of the contract.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CreateRequestFilters(int pageSize)
    {
        if (pageSize is <= 0 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        string pageSizeValue = pageSize.ToString(CultureInfo.InvariantCulture);
        SortedDictionary<string, string> filters = new(StringComparer.Ordinal)
        {
            ["detailed.count_specials"] = "false",
            ["detailed.hidden"] = "false",
            ["detailed.specials"] = "false",
            ["season.extended"] = "full",
            ["show.extended"] = "full",
            ["watched.hide_completed"] = "false",
            ["watched.hide_not_completed"] = "false",
            ["watched.limit"] = pageSizeValue,
            ["watched.only_rewatching"] = "false",
            ["watchlist.limit"] = pageSizeValue,
            ["watchlist.order"] = "asc",
            ["watchlist.sort"] = "added"
        };
        return new ReadOnlyDictionary<string, string>(filters);
    }

    /// <summary>
    /// Computes the canonical hash of Trakt show identity and explicit watchlist membership.
    /// </summary>
    public string ComputeMembershipHash(IReadOnlyList<TvShow> shows)
    {
        ArgumentNullException.ThrowIfNull(shows);
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartArray();
            foreach (TvShow? show in shows.OrderBy(item => item?.TraktId ?? long.MinValue))
            {
                if (show is null)
                {
                    writer.WriteNullValue();
                    continue;
                }

                writer.WriteStartArray();
                writer.WriteNumberValue(show.TraktId);
                writer.WriteBooleanValue(show.InWatchlist);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.Flush();
        }

        return Hash(buffer.WrittenSpan);
    }

    /// <summary>
    /// Computes the canonical hash of Trakt progress and watched regular episodes.
    /// </summary>
    public string ComputeProgressHash(IReadOnlyList<TvShow> shows)
    {
        ArgumentNullException.ThrowIfNull(shows);
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartArray();
            foreach (TvShow? show in shows.OrderBy(item => item?.TraktId ?? long.MinValue))
            {
                if (show is null)
                {
                    writer.WriteNullValue();
                    continue;
                }

                writer.WriteStartArray();
                writer.WriteNumberValue(show.TraktId);
                writer.WriteNumberValue(show.AiredEpisodes);
                writer.WriteNumberValue(show.CompletedEpisodes);
                writer.WriteStartArray();
                IEnumerable<TvEpisodeProgress> watchedEpisodes = show.Seasons
                    .Where(season => season is not null)
                    .SelectMany(season => season.Episodes.Where(episode => episode is not null))
                    .Where(episode => episode.Watched)
                    .OrderBy(episode => episode.SeasonNumber)
                    .ThenBy(episode => episode.EpisodeNumber)
                    .ThenBy(episode => episode.TraktEpisodeId);
                foreach (TvEpisodeProgress episode in watchedEpisodes)
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(episode.TraktEpisodeId);
                    writer.WriteNumberValue(episode.SeasonNumber);
                    writer.WriteNumberValue(episode.EpisodeNumber);
                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.Flush();
        }

        return Hash(buffer.WrittenSpan);
    }

    /// <summary>
    /// Rejects a draft that cannot be published as a complete Phase 1 generation.
    /// </summary>
    public void Validate(TvGenerationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateGenerationEnvelope(
            draft.GenerationId,
            draft.Kind,
            draft.StartedAt,
            draft.CompletedAt,
            draft.ActivityAfter,
            draft.WatchlistPageCount,
            draft.WatchlistItemCount,
            draft.ProgressPageCount,
            draft.ProgressItemCount,
            draft.RequestContractVersion,
            draft.RequestFilters,
            draft.MembershipHash,
            draft.ProgressHash);
        ValidateActivityCursor(draft.ActivityBefore, draft.CompletedAt);
        if (draft.ActivityBefore != draft.ActivityAfter)
        {
            throw Rejected("tv_snapshot_activity_changed");
        }

        Dictionary<long, TvShow> showsByTraktId = ValidateShows(draft);
        ValidateLifecycleEvents(draft, showsByTraktId);
        ValidateStrings(draft.EnrichmentErrors, "tv_snapshot_enrichment_errors_invalid");

        string membershipHash = ComputeMembershipHash(draft.Shows);
        string progressHash = ComputeProgressHash(draft.Shows);
        if (!string.Equals(draft.MembershipHash, membershipHash, StringComparison.Ordinal))
        {
            throw Rejected("tv_snapshot_membership_hash_invalid");
        }

        if (!string.Equals(draft.ProgressHash, progressHash, StringComparison.Ordinal))
        {
            throw Rejected("tv_snapshot_progress_hash_invalid");
        }
    }

    /// <summary>
    /// Rejects a manifest that does not preserve the locked Phase 1 safety envelope.
    /// </summary>
    public void Validate(TvGenerationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateGenerationEnvelope(
            manifest.GenerationId,
            manifest.Kind,
            manifest.StartedAt,
            manifest.CompletedAt,
            manifest.ActivityCursor,
            manifest.WatchlistPageCount,
            manifest.WatchlistItemCount,
            manifest.ProgressPageCount,
            manifest.ProgressItemCount,
            manifest.RequestContractVersion,
            manifest.RequestFilters,
            manifest.MembershipHash,
            manifest.ProgressHash);

        if (!IsUtc(manifest.PublishedAt) || manifest.PublishedAt < manifest.CompletedAt)
        {
            throw Rejected("tv_manifest_published_at_invalid");
        }

        if (manifest.PreviousGenerationId is not null
            && (!IsCanonicalRequired(manifest.PreviousGenerationId)
                || string.Equals(
                    manifest.PreviousGenerationId,
                    manifest.GenerationId,
                    StringComparison.Ordinal)))
        {
            throw Rejected("tv_manifest_previous_generation_invalid");
        }

        if (manifest.ProviderEnrichmentCompletedAt is DateTimeOffset providerCompletedAt
            && (!IsUtc(providerCompletedAt)
                || providerCompletedAt < manifest.StartedAt
                || providerCompletedAt > manifest.CompletedAt))
        {
            throw Rejected("tv_manifest_provider_time_invalid");
        }

        if (manifest.PlexHistoryCollectedAt is not null
            || manifest.PlexHistoryWatermark is not null
            || manifest.MutationCapable
            || manifest.CleanupEventIds.Count != 0
            || !string.Equals(manifest.ValidationStatus, ValidationStatus, StringComparison.Ordinal)
            || manifest.ValidationFailureReasons.Count != 0
            || !TvGenerationManifest.HasRequiredPhaseOneHealthReasons(manifest.HealthReasons))
        {
            throw Rejected("tv_manifest_phase_one_safety_invalid");
        }

        ValidateLifecycleEventIds(manifest.LifecycleEventIds);
        ValidateStrings(manifest.EnrichmentErrors, "tv_manifest_enrichment_errors_invalid");
    }

    private static Dictionary<long, TvShow> ValidateShows(TvGenerationDraft draft)
    {
        Dictionary<long, TvShow> showsByTraktId = [];
        HashSet<int> tvdbShowIds = [];
        HashSet<int> tmdbShowIds = [];
        HashSet<string> imdbShowIds = new(StringComparer.Ordinal);
        HashSet<long> traktEpisodeIds = [];
        HashSet<int> tvdbEpisodeIds = [];
        long? previousTraktId = null;

        foreach (TvShow? candidate in draft.Shows)
        {
            if (candidate is null)
            {
                throw Rejected("tv_snapshot_show_null");
            }

            TvShow show = candidate;
            if (show.TraktId <= 0
                || !string.Equals(show.Id, $"tv-trakt-{show.TraktId}", StringComparison.Ordinal)
                || previousTraktId is long priorTraktId && show.TraktId <= priorTraktId
                || !showsByTraktId.TryAdd(show.TraktId, show))
            {
                throw Rejected("tv_snapshot_show_identity_invalid");
            }

            previousTraktId = show.TraktId;
            ValidateNullableIdentity(show.TvdbId, tvdbShowIds, "tv_snapshot_tvdb_identity_invalid");
            ValidateNullableIdentity(show.TmdbId, tmdbShowIds, "tv_snapshot_tmdb_identity_invalid");
            if (show.ImdbId is not null
                && (!IsNormalizedImdbId(show.ImdbId) || !imdbShowIds.Add(show.ImdbId)))
            {
                throw Rejected("tv_snapshot_imdb_identity_invalid");
            }

            if (!Enum.IsDefined(show.IdentityStatus)
                || !IsCanonicalRequired(show.Title)
                || string.IsNullOrWhiteSpace(show.TraktStatus)
                || show.Year is <= 0
                || !string.Equals(show.GenerationId, draft.GenerationId, StringComparison.Ordinal))
            {
                throw Rejected("tv_snapshot_show_shape_invalid");
            }

            ValidateCounts(
                show.AiredEpisodes,
                show.CompletedEpisodes,
                "tv_snapshot_show_progress_invalid");
            ValidateShowTimes(show, draft.CompletedAt);
            ValidateProviderAvailability(show.Availability, draft.CompletedAt);
            ValidateLifecycleState(show);
            ValidateShowEpisodes(show, draft.CompletedAt, traktEpisodeIds, tvdbEpisodeIds);
        }

        return showsByTraktId;
    }

    private static void ValidateShowEpisodes(
        TvShow show,
        DateTimeOffset completedAt,
        HashSet<long> traktEpisodeIds,
        HashSet<int> tvdbEpisodeIds)
    {
        List<TvEpisodeProgress> regularEpisodes = [];
        int? previousSeasonNumber = null;
        long totalAired = 0;
        long totalCompleted = 0;
        foreach (TvSeasonProgress? candidate in show.Seasons)
        {
            if (candidate is null)
            {
                throw Rejected("tv_snapshot_season_null");
            }

            TvSeasonProgress season = candidate;
            if (season.SeasonNumber <= 0
                || previousSeasonNumber is int priorSeasonNumber
                    && season.SeasonNumber <= priorSeasonNumber)
            {
                throw Rejected("tv_snapshot_season_identity_invalid");
            }

            previousSeasonNumber = season.SeasonNumber;
            ValidateCounts(
                season.AiredEpisodes,
                season.CompletedEpisodes,
                "tv_snapshot_season_progress_invalid");
            if (season.AiredEpisodes > season.Episodes.Count)
            {
                throw Rejected("tv_snapshot_season_progress_invalid");
            }

            ValidateProviderAvailability(season.Availability, completedAt);
            int watchedCount = 0;
            int? previousEpisodeNumber = null;
            foreach (TvEpisodeProgress? episodeCandidate in season.Episodes)
            {
                if (episodeCandidate is null)
                {
                    throw Rejected("tv_snapshot_episode_null");
                }

                TvEpisodeProgress episode = episodeCandidate;
                if (episode.TraktEpisodeId <= 0
                    || episode.TvdbId is <= 0
                    || episode.SeasonNumber != season.SeasonNumber
                    || episode.EpisodeNumber <= 0
                    || previousEpisodeNumber is int priorEpisodeNumber
                        && episode.EpisodeNumber <= priorEpisodeNumber
                    || !traktEpisodeIds.Add(episode.TraktEpisodeId)
                    || episode.TvdbId is int tvdbEpisodeId && !tvdbEpisodeIds.Add(tvdbEpisodeId))
                {
                    throw Rejected("tv_snapshot_episode_identity_invalid");
                }

                previousEpisodeNumber = episode.EpisodeNumber;
                ValidateEpisodeTimes(episode, completedAt);
                if (episode.Title is not null && !IsCanonicalRequired(episode.Title))
                {
                    throw Rejected("tv_snapshot_episode_shape_invalid");
                }

                if (episode.Watched)
                {
                    watchedCount++;
                }

                regularEpisodes.Add(episode);
            }

            if (watchedCount != season.CompletedEpisodes)
            {
                throw Rejected("tv_snapshot_season_completed_invalid");
            }

            totalAired += season.AiredEpisodes;
            totalCompleted += season.CompletedEpisodes;
        }

        if (totalAired != show.AiredEpisodes || totalCompleted != show.CompletedEpisodes)
        {
            throw Rejected("tv_snapshot_show_totals_invalid");
        }

        ValidateEpisodeJoins(show, regularEpisodes);
        ValidateSpecialIdentities(show, traktEpisodeIds, tvdbEpisodeIds);
    }

    private static void ValidateEpisodeJoins(TvShow show, IReadOnlyList<TvEpisodeProgress> episodes)
    {
        if (show.LastWatchedEpisode is TvEpisodeProgress lastWatched
            && (!lastWatched.Watched || !episodes.Contains(lastWatched)))
        {
            throw Rejected("tv_snapshot_last_episode_invalid");
        }

        if (show.NextEpisode is TvEpisodeProgress nextEpisode
            && (nextEpisode.Watched || !episodes.Contains(nextEpisode)))
        {
            throw Rejected("tv_snapshot_next_episode_invalid");
        }
    }

    private static void ValidateSpecialIdentities(
        TvShow show,
        HashSet<long> traktEpisodeIds,
        HashSet<int> tvdbEpisodeIds)
    {
        int? previousEpisodeNumber = null;
        foreach (TvSpecialEpisodeIdentity? candidate in show.SpecialEpisodeIdentities)
        {
            if (candidate is null)
            {
                throw Rejected("tv_snapshot_special_null");
            }

            TvSpecialEpisodeIdentity special = candidate;
            if (special.TraktEpisodeId <= 0
                || special.TvdbId is <= 0
                || special.SeasonNumber != 0
                || special.EpisodeNumber <= 0
                || previousEpisodeNumber is int priorEpisodeNumber
                    && special.EpisodeNumber <= priorEpisodeNumber
                || !traktEpisodeIds.Add(special.TraktEpisodeId)
                || special.TvdbId is int tvdbEpisodeId && !tvdbEpisodeIds.Add(tvdbEpisodeId))
            {
                throw Rejected("tv_snapshot_special_identity_invalid");
            }

            previousEpisodeNumber = special.EpisodeNumber;
        }
    }

    private static void ValidateLifecycleState(TvShow show)
    {
        bool isPhaseOneState = show.LifecycleState is TvLifecycleState.Active
            or TvLifecycleState.CaughtUp
            or TvLifecycleState.SourceRemoved;
        bool confirmationCountValid = show.LifecycleState == TvLifecycleState.SourceRemoved
            ? show.MissingScheduledConfirmations == 2
            : show.MissingScheduledConfirmations is 0 or 1;
        if (!isPhaseOneState
            || show.LifecycleVersion <= 0
            || !confirmationCountValid
            || !IsStableEventId(
                show.LastLifecycleEvent,
                show.TraktId,
                show.LifecycleVersion,
                show.LifecycleState))
        {
            throw Rejected("tv_snapshot_lifecycle_invalid");
        }

        if (show.InWatchlist && show.LifecycleState != TvLifecycleState.Active)
        {
            throw Rejected("tv_snapshot_lifecycle_invalid");
        }

        if (show.LifecycleState == TvLifecycleState.CaughtUp
            && (show.InWatchlist
                || show.AiredEpisodes == 0
                || show.CompletedEpisodes != show.AiredEpisodes))
        {
            throw Rejected("tv_snapshot_lifecycle_invalid");
        }

        if (show.LifecycleState == TvLifecycleState.SourceRemoved && show.InWatchlist)
        {
            throw Rejected("tv_snapshot_lifecycle_invalid");
        }
    }

    private static void ValidateLifecycleEvents(
        TvGenerationDraft draft,
        IReadOnlyDictionary<long, TvShow> showsByTraktId)
    {
        HashSet<string> eventIds = new(StringComparer.Ordinal);
        HashSet<long> eventShows = [];
        long? previousTraktId = null;
        foreach (TvLifecycleEvent? candidate in draft.LifecycleEvents)
        {
            if (candidate is null)
            {
                throw Rejected("tv_snapshot_lifecycle_event_null");
            }

            TvLifecycleEvent lifecycleEvent = candidate;
            if (!showsByTraktId.TryGetValue(lifecycleEvent.TraktId, out TvShow? show)
                || previousTraktId is long priorTraktId
                    && lifecycleEvent.TraktId <= priorTraktId
                || !eventShows.Add(lifecycleEvent.TraktId)
                || !eventIds.Add(lifecycleEvent.Id)
                || lifecycleEvent.Version != show.LifecycleVersion
                || !string.Equals(lifecycleEvent.Id, show.LastLifecycleEvent, StringComparison.Ordinal)
                || !string.Equals(
                    lifecycleEvent.Id,
                    $"tv:{lifecycleEvent.TraktId}:{lifecycleEvent.Version}:{lifecycleEvent.EventType}",
                    StringComparison.Ordinal)
                || !string.Equals(
                    lifecycleEvent.GenerationId,
                    draft.GenerationId,
                    StringComparison.Ordinal)
                || !IsUtc(lifecycleEvent.OccurredAt)
                || lifecycleEvent.OccurredAt < draft.StartedAt
                || lifecycleEvent.OccurredAt > draft.CompletedAt
                || !HasValidEventVersion(lifecycleEvent)
                || lifecycleEvent.EventType != "source_removed"
                    && show.MissingScheduledConfirmations != 0
                || !IsLowerHexHash(lifecycleEvent.PredicateHash)
                || !HasExpectedEventReason(lifecycleEvent))
            {
                throw Rejected("tv_snapshot_lifecycle_event_invalid");
            }

            previousTraktId = lifecycleEvent.TraktId;
            if (!HasExpectedPredicateHash(lifecycleEvent, show))
            {
                throw Rejected("tv_snapshot_lifecycle_event_hash_invalid");
            }
        }
    }

    private static bool HasExpectedPredicateHash(TvLifecycleEvent lifecycleEvent, TvShow show)
    {
        bool stateMatchesEvent = lifecycleEvent.EventType switch
        {
            "added" => show.LifecycleState is TvLifecycleState.Active or TvLifecycleState.CaughtUp,
            "caught_up" => show.LifecycleState == TvLifecycleState.CaughtUp,
            "reactivated" => show.LifecycleState is TvLifecycleState.Active or TvLifecycleState.CaughtUp,
            "source_removed" => show.LifecycleState == TvLifecycleState.SourceRemoved,
            _ => false
        };
        if (!stateMatchesEvent)
        {
            return false;
        }

        bool presentInCurrentSource = lifecycleEvent.EventType != "source_removed";
        bool inWatchlist = presentInCurrentSource && show.InWatchlist;
        int missingConfirmations = lifecycleEvent.EventType == "source_removed" ? 2 : 0;
        IReadOnlyList<TvLifecycleState?> previousStates = lifecycleEvent.EventType switch
        {
            "added" => [null],
            "caught_up" => [TvLifecycleState.Active],
            "reactivated" when show.LifecycleState == TvLifecycleState.CaughtUp =>
                [TvLifecycleState.SourceRemoved],
            "reactivated" => [TvLifecycleState.CaughtUp, TvLifecycleState.SourceRemoved],
            "source_removed" => [TvLifecycleState.Active, TvLifecycleState.CaughtUp],
            _ => []
        };
        return previousStates.Any(previousState => string.Equals(
            lifecycleEvent.PredicateHash,
            TvLifecycleEvaluator.ComputePredicateHash(
                show.TraktId,
                lifecycleEvent.EventType,
                previousState,
                show.LifecycleState,
                presentInCurrentSource,
                inWatchlist,
                show.AiredEpisodes,
                show.CompletedEpisodes,
                missingConfirmations),
            StringComparison.Ordinal));
    }

    private static bool HasExpectedEventReason(TvLifecycleEvent lifecycleEvent)
    {
        string? expected = lifecycleEvent.EventType switch
        {
            "added" => "tracked_source_added",
            "caught_up" => "all_aired_episodes_watched",
            "reactivated" => "tracked_source_reactivated",
            "source_removed" => "source_absent_two_scheduled_generations",
            _ => null
        };
        return string.Equals(lifecycleEvent.Reason, expected, StringComparison.Ordinal);
    }

    private static bool HasValidEventVersion(TvLifecycleEvent lifecycleEvent)
    {
        return lifecycleEvent.EventType == "added"
            ? lifecycleEvent.Version == 1
            : lifecycleEvent.Version > 1;
    }

    private static void ValidateGenerationEnvelope(
        string generationId,
        TvGenerationKind kind,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        TraktActivityCursor activityCursor,
        int watchlistPageCount,
        int watchlistItemCount,
        int progressPageCount,
        int progressItemCount,
        string requestContractVersion,
        IReadOnlyDictionary<string, string> requestFilters,
        string membershipHash,
        string progressHash)
    {
        if (!IsCanonicalRequired(generationId) || !Enum.IsDefined(kind))
        {
            throw Rejected("tv_generation_identity_invalid");
        }

        if (!IsUtc(startedAt) || !IsUtc(completedAt) || completedAt < startedAt)
        {
            throw Rejected("tv_generation_time_invalid");
        }

        ValidateActivityCursor(activityCursor, completedAt);
        if (watchlistPageCount <= 0
            || watchlistItemCount < 0
            || progressPageCount <= 0
            || progressItemCount < 0)
        {
            throw Rejected("tv_generation_counts_invalid");
        }

        if (!string.Equals(requestContractVersion, RequestContractVersion, StringComparison.Ordinal)
            || !HasExactRequestFilters(requestFilters))
        {
            throw Rejected("tv_generation_request_contract_invalid");
        }

        if (!IsLowerHexHash(membershipHash) || !IsLowerHexHash(progressHash))
        {
            throw Rejected("tv_generation_hash_format_invalid");
        }
    }

    private static bool HasExactRequestFilters(IReadOnlyDictionary<string, string> filters)
    {
        if (filters is null
            || !filters.TryGetValue("watchlist.limit", out string? watchlistLimit)
            || !filters.TryGetValue("watched.limit", out string? watchedLimit)
            || !string.Equals(watchlistLimit, watchedLimit, StringComparison.Ordinal)
            || !int.TryParse(
                watchlistLimit,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int pageSize)
            || pageSize is <= 0 or > MaximumPageSize)
        {
            return false;
        }

        IReadOnlyDictionary<string, string> expected = CreateRequestFilters(pageSize);
        return filters.Count == expected.Count
            && expected.All(item => filters.TryGetValue(item.Key, out string? value)
                && string.Equals(item.Value, value, StringComparison.Ordinal));
    }

    private static void ValidateActivityCursor(
        TraktActivityCursor activityCursor,
        DateTimeOffset completedAt)
    {
        if (activityCursor is null
            || !IsUtc(activityCursor.ShowWatchlistedAt)
            || !IsUtc(activityCursor.EpisodeWatchedAt)
            || activityCursor.ShowWatchlistedAt > completedAt
            || activityCursor.EpisodeWatchedAt > completedAt)
        {
            throw Rejected("tv_generation_activity_cursor_invalid");
        }
    }

    private static void ValidateShowTimes(TvShow show, DateTimeOffset completedAt)
    {
        if (!IsUtc(show.AddedAt)
            || !IsUtc(show.UpdatedAt)
            || !IsUtc(show.MetadataFetchedAt)
            || show.AddedAt > show.UpdatedAt
            || show.UpdatedAt > completedAt
            || show.MetadataFetchedAt > completedAt)
        {
            throw Rejected("tv_snapshot_show_time_invalid");
        }
    }

    private static void ValidateEpisodeTimes(TvEpisodeProgress episode, DateTimeOffset completedAt)
    {
        if (episode.AiredAt is DateTimeOffset airedTimestamp && !IsUtc(airedTimestamp))
        {
            throw Rejected("tv_snapshot_episode_time_invalid");
        }

        if (episode.WatchedAt is DateTimeOffset watchedAt)
        {
            if (!IsUtc(watchedAt)
                || watchedAt > completedAt)
            {
                throw Rejected("tv_snapshot_episode_time_invalid");
            }
        }

        if (!episode.Watched && episode.WatchedAt is not null)
        {
            throw Rejected("tv_snapshot_episode_time_invalid");
        }
    }

    private static void ValidateProviderAvailability(
        TvProviderAvailability availability,
        DateTimeOffset completedAt)
    {
        if (availability is null
            || !Enum.IsDefined(availability.State)
            || !IsRegionCode(availability.Region)
            || availability.FetchedAt is DateTimeOffset fetchedAt
                && (!IsUtc(fetchedAt) || fetchedAt > completedAt)
            || availability.Link is not null && !IsCanonicalRequired(availability.Link))
        {
            throw Rejected("tv_snapshot_provider_invalid");
        }

        bool availabilityShapeValid = availability.State switch
        {
            TvProviderState.Available => availability.FetchedAt is not null
                && availability.Offers.Count > 0,
            TvProviderState.ConfirmedUnavailable => availability.FetchedAt is not null
                && availability.Offers.Count == 0,
            TvProviderState.Unknown => availability.Offers.Count == 0,
            TvProviderState.Stale => availability.FetchedAt is not null,
            _ => false
        };
        if (!availabilityShapeValid)
        {
            throw Rejected("tv_snapshot_provider_invalid");
        }

        HashSet<(int ProviderId, TvProviderCategory Category)> offers = [];
        foreach (TvProviderOffer? candidate in availability.Offers)
        {
            if (candidate is null)
            {
                throw Rejected("tv_snapshot_provider_offer_null");
            }

            TvProviderOffer offer = candidate;
            if (offer.ProviderId <= 0
                || !IsCanonicalRequired(offer.ProviderName)
                || !Enum.IsDefined(offer.Category)
                || offer.LogoUrl is not null && !IsCanonicalRequired(offer.LogoUrl)
                || !offers.Add((offer.ProviderId, offer.Category)))
            {
                throw Rejected("tv_snapshot_provider_offer_invalid");
            }
        }
    }

    private static void ValidateNullableIdentity(
        int? identity,
        HashSet<int> identities,
        string reason)
    {
        if (identity is int value && (value <= 0 || !identities.Add(value)))
        {
            throw Rejected(reason);
        }
    }

    private static void ValidateCounts(int airedEpisodes, int completedEpisodes, string reason)
    {
        if (airedEpisodes < 0 || completedEpisodes < 0 || completedEpisodes > airedEpisodes)
        {
            throw Rejected(reason);
        }
    }

    private static void ValidateLifecycleEventIds(IReadOnlyList<string> eventIds)
    {
        HashSet<string> uniqueIds = new(StringComparer.Ordinal);
        long? previousTraktId = null;
        foreach (string? eventId in eventIds)
        {
            if (!IsCanonicalRequired(eventId)
                || !uniqueIds.Add(eventId)
                || !HasStableEventIdShape(eventId))
            {
                throw Rejected("tv_manifest_lifecycle_event_ids_invalid");
            }

            string[] parts = eventId.Split(':');
            long traktId = long.Parse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture);
            if (previousTraktId is long priorTraktId && traktId <= priorTraktId)
            {
                throw Rejected("tv_manifest_lifecycle_event_ids_invalid");
            }

            previousTraktId = traktId;
        }
    }

    private static void ValidateStrings(IReadOnlyList<string> values, string reason)
    {
        foreach (string? value in values)
        {
            if (!IsCanonicalRequired(value))
            {
                throw Rejected(reason);
            }
        }
    }

    private static bool IsStableEventId(
        string? value,
        long traktId,
        long version,
        TvLifecycleState state)
    {
        if (value is null)
        {
            return false;
        }

        string prefix = $"tv:{traktId}:{version}:";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string eventType = value[prefix.Length..];
        bool versionMatchesEvent = eventType == "added" ? version == 1 : version > 1;
        bool stateMatchesEvent = state switch
        {
            TvLifecycleState.Active => eventType is "added" or "reactivated",
            TvLifecycleState.CaughtUp => eventType is "added" or "caught_up" or "reactivated",
            TvLifecycleState.SourceRemoved => eventType == "source_removed",
            _ => false
        };
        return IsEventType(eventType) && versionMatchesEvent && stateMatchesEvent;
    }

    private static bool HasStableEventIdShape(string value)
    {
        string[] parts = value.Split(':');
        return parts.Length == 4
            && string.Equals(parts[0], "tv", StringComparison.Ordinal)
            && long.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long traktId)
            && traktId > 0
            && long.TryParse(
                parts[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long version)
            && version > 0
            && IsEventType(parts[3])
            && (parts[3] == "added" ? version == 1 : version > 1)
            && string.Equals(
                value,
                $"tv:{traktId}:{version}:{parts[3]}",
                StringComparison.Ordinal);
    }

    private static bool IsEventType(string value)
    {
        return value is "added" or "caught_up" or "reactivated" or "source_removed";
    }

    private static bool IsNormalizedImdbId(string value)
    {
        return value.Length > 2
            && value.StartsWith("tt", StringComparison.Ordinal)
            && value.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static bool IsRegionCode(string value)
    {
        return value.Length == 2
            && value[0] is >= 'A' and <= 'Z'
            && value[1] is >= 'A' and <= 'Z';
    }

    private static bool IsCanonicalRequired(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    private static bool IsUtc(DateTimeOffset value)
    {
        return value != default && value.Offset == TimeSpan.Zero;
    }

    private static bool IsLowerHexHash(string? value)
    {
        return value is { Length: 64 }
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static string Hash(ReadOnlySpan<byte> value)
    {
        return Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }

    private static TvSourceSnapshotRejectedException Rejected(string reason)
    {
        return new TvSourceSnapshotRejectedException(reason);
    }
}
