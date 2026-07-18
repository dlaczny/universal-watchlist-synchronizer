using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Reduces complete Trakt source facts into the non-destructive Phase 1 lifecycle.
/// </summary>
public sealed class TvLifecycleEvaluator
{
    public TvLifecycleDecision Evaluate(
        TvShow? previous,
        long traktId,
        bool presentInCurrentSource,
        bool inWatchlist,
        int airedEpisodes,
        int completedEpisodes,
        TvGenerationKind generationKind,
        string generationId,
        DateTimeOffset occurredAt)
    {
        ValidateCurrentInputs(
            traktId,
            presentInCurrentSource,
            inWatchlist,
            airedEpisodes,
            completedEpisodes,
            generationKind,
            generationId,
            occurredAt);

        if (previous is null)
        {
            if (!presentInCurrentSource)
            {
                throw Rejected("tv_lifecycle_new_row_absent");
            }

            TvLifecycleState initialState = DeterminePresentState(
                inWatchlist,
                airedEpisodes,
                completedEpisodes);
            return Emit(
                traktId,
                previousState: null,
                initialState,
                previousVersion: 0,
                missingScheduledConfirmations: 0,
                presentInCurrentSource,
                inWatchlist,
                airedEpisodes,
                completedEpisodes,
                generationId,
                occurredAt,
                "added",
                "tracked_source_added");
        }

        ValidatePrevious(previous, traktId);

        bool isSameGeneration = string.Equals(
            previous.GenerationId,
            generationId,
            StringComparison.Ordinal);
        if (!presentInCurrentSource
            && (previous.AiredEpisodes != airedEpisodes
                || previous.CompletedEpisodes != completedEpisodes))
        {
            throw Rejected(isSameGeneration
                ? "tv_lifecycle_generation_reused"
                : "tv_lifecycle_absence_facts_invalid");
        }

        if (isSameGeneration)
        {
            if (!presentInCurrentSource)
            {
                return Retain(previous);
            }

            bool factsMatch = previous.InWatchlist == inWatchlist
                && previous.AiredEpisodes == airedEpisodes
                && previous.CompletedEpisodes == completedEpisodes;
            if (!factsMatch)
            {
                throw Rejected("tv_lifecycle_generation_reused");
            }

            if (presentInCurrentSource
                && DeterminePresentState(inWatchlist, airedEpisodes, completedEpisodes)
                    != previous.LifecycleState)
            {
                throw Rejected("tv_lifecycle_generation_reused");
            }

            return Retain(previous);
        }

        if (!presentInCurrentSource)
        {
            return EvaluateAbsence(
                previous,
                generationKind,
                generationId,
                occurredAt);
        }

        TvLifecycleState currentState = DeterminePresentState(
            inWatchlist,
            airedEpisodes,
            completedEpisodes);
        if (previous.LifecycleState == TvLifecycleState.SourceRemoved
            || (previous.LifecycleState == TvLifecycleState.CaughtUp
                && currentState == TvLifecycleState.Active))
        {
            return Emit(
                traktId,
                previous.LifecycleState,
                currentState,
                previous.LifecycleVersion,
                0,
                presentInCurrentSource,
                inWatchlist,
                airedEpisodes,
                completedEpisodes,
                generationId,
                occurredAt,
                "reactivated",
                "tracked_source_reactivated");
        }

        if (previous.LifecycleState == TvLifecycleState.Active
            && currentState == TvLifecycleState.CaughtUp)
        {
            return Emit(
                traktId,
                previous.LifecycleState,
                currentState,
                previous.LifecycleVersion,
                0,
                presentInCurrentSource,
                inWatchlist,
                airedEpisodes,
                completedEpisodes,
                generationId,
                occurredAt,
                "caught_up",
                "all_aired_episodes_watched");
        }

        if (previous.LifecycleState != currentState)
        {
            throw Rejected("tv_lifecycle_transition_unsupported");
        }

        return new TvLifecycleDecision(
            currentState,
            previous.LifecycleVersion,
            0,
            null);
    }

    internal static string ComputePredicateHash(
        long traktId,
        string eventType,
        TvLifecycleState? previousState,
        TvLifecycleState state,
        bool presentInCurrentSource,
        bool inWatchlist,
        int airedEpisodes,
        int completedEpisodes,
        int missingScheduledConfirmations)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartArray();
            writer.WriteStringValue("tv_lifecycle_event_v1");
            writer.WriteNumberValue(traktId);
            writer.WriteStringValue(eventType);
            if (previousState is TvLifecycleState previousValue)
            {
                writer.WriteNumberValue((int)previousValue);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteNumberValue((int)state);
            writer.WriteBooleanValue(presentInCurrentSource);
            writer.WriteBooleanValue(inWatchlist);
            writer.WriteNumberValue(airedEpisodes);
            writer.WriteNumberValue(completedEpisodes);
            writer.WriteNumberValue(missingScheduledConfirmations);
            writer.WriteEndArray();
            writer.Flush();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static TvLifecycleDecision EvaluateAbsence(
        TvShow previous,
        TvGenerationKind generationKind,
        string generationId,
        DateTimeOffset occurredAt)
    {
        if (generationKind == TvGenerationKind.ActivityFull
            || previous.LifecycleState == TvLifecycleState.SourceRemoved)
        {
            return Retain(previous);
        }

        int confirmations;
        try
        {
            confirmations = checked(previous.MissingScheduledConfirmations + 1);
        }
        catch (OverflowException)
        {
            throw Rejected("tv_lifecycle_confirmation_overflow");
        }

        if (confirmations < 2)
        {
            return new TvLifecycleDecision(
                previous.LifecycleState,
                previous.LifecycleVersion,
                confirmations,
                null);
        }

        return Emit(
            previous.TraktId,
            previous.LifecycleState,
            TvLifecycleState.SourceRemoved,
            previous.LifecycleVersion,
            2,
            presentInCurrentSource: false,
            inWatchlist: false,
            previous.AiredEpisodes,
            previous.CompletedEpisodes,
            generationId,
            occurredAt,
            "source_removed",
            "source_absent_two_scheduled_generations");
    }

    private static TvLifecycleDecision Emit(
        long traktId,
        TvLifecycleState? previousState,
        TvLifecycleState state,
        long previousVersion,
        int missingScheduledConfirmations,
        bool presentInCurrentSource,
        bool inWatchlist,
        int airedEpisodes,
        int completedEpisodes,
        string generationId,
        DateTimeOffset occurredAt,
        string eventType,
        string reason)
    {
        long nextVersion;
        try
        {
            nextVersion = checked(previousVersion + 1);
        }
        catch (OverflowException)
        {
            throw Rejected("tv_lifecycle_version_overflow");
        }

        string eventId = $"tv:{traktId}:{nextVersion}:{eventType}";
        string predicateHash = ComputePredicateHash(
            traktId,
            eventType,
            previousState,
            state,
            presentInCurrentSource,
            inWatchlist,
            airedEpisodes,
            completedEpisodes,
            missingScheduledConfirmations);
        TvLifecycleEvent lifecycleEvent = new(
            eventId,
            traktId,
            nextVersion,
            generationId,
            eventType,
            occurredAt,
            predicateHash,
            reason);
        return new TvLifecycleDecision(
            state,
            nextVersion,
            missingScheduledConfirmations,
            lifecycleEvent);
    }

    private static TvLifecycleDecision Retain(TvShow previous)
    {
        return new TvLifecycleDecision(
            previous.LifecycleState,
            previous.LifecycleVersion,
            previous.MissingScheduledConfirmations,
            null);
    }

    private static TvLifecycleState DeterminePresentState(
        bool inWatchlist,
        int airedEpisodes,
        int completedEpisodes)
    {
        if (inWatchlist)
        {
            return TvLifecycleState.Active;
        }

        if (airedEpisodes == 0)
        {
            throw Rejected("tv_lifecycle_progress_without_aired_episode");
        }

        return completedEpisodes == airedEpisodes
            ? TvLifecycleState.CaughtUp
            : TvLifecycleState.Active;
    }

    private static void ValidateCurrentInputs(
        long traktId,
        bool presentInCurrentSource,
        bool inWatchlist,
        int airedEpisodes,
        int completedEpisodes,
        TvGenerationKind generationKind,
        string generationId,
        DateTimeOffset occurredAt)
    {
        if (traktId <= 0)
        {
            throw Rejected("tv_lifecycle_trakt_id_invalid");
        }

        if (!Enum.IsDefined(generationKind))
        {
            throw Rejected("tv_lifecycle_generation_kind_invalid");
        }

        if (!IsCanonicalRequired(generationId))
        {
            throw Rejected("tv_lifecycle_generation_id_invalid");
        }

        if (!IsUtc(occurredAt))
        {
            throw Rejected("tv_lifecycle_timestamp_invalid");
        }

        if (inWatchlist && !presentInCurrentSource)
        {
            throw Rejected("tv_lifecycle_watchlist_presence_invalid");
        }

        ValidateCounts(airedEpisodes, completedEpisodes, "tv_lifecycle_counts_invalid");
    }

    private static void ValidatePrevious(TvShow previous, long traktId)
    {
        if (previous.TraktId != traktId
            || previous.LifecycleVersion <= 0
            || !IsCanonicalRequired(previous.GenerationId)
            || !IsPhaseOneState(previous.LifecycleState))
        {
            throw Rejected("tv_lifecycle_previous_invalid");
        }

        ValidateCounts(
            previous.AiredEpisodes,
            previous.CompletedEpisodes,
            "tv_lifecycle_previous_invalid");
        bool stateFactsValid = previous.LifecycleState switch
        {
            TvLifecycleState.Active => true,
            TvLifecycleState.CaughtUp => !previous.InWatchlist
                && previous.AiredEpisodes > 0
                && previous.CompletedEpisodes == previous.AiredEpisodes,
            TvLifecycleState.SourceRemoved => !previous.InWatchlist,
            _ => false
        };
        bool confirmationCountValid = previous.LifecycleState == TvLifecycleState.SourceRemoved
            ? previous.MissingScheduledConfirmations == 2
            : previous.MissingScheduledConfirmations is 0 or 1;
        if (!stateFactsValid
            || !confirmationCountValid
            || !IsStableEventId(
                previous.LastLifecycleEvent,
                previous.TraktId,
                previous.LifecycleVersion,
                previous.LifecycleState))
        {
            throw Rejected("tv_lifecycle_previous_invalid");
        }
    }

    private static void ValidateCounts(int airedEpisodes, int completedEpisodes, string reason)
    {
        if (airedEpisodes < 0 || completedEpisodes < 0 || completedEpisodes > airedEpisodes)
        {
            throw Rejected(reason);
        }
    }

    private static bool IsStableEventId(
        string? value,
        long traktId,
        long version,
        TvLifecycleState state)
    {
        if (value is null || !IsCanonicalRequired(value))
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
        return eventType is "added" or "caught_up" or "reactivated" or "source_removed"
            && versionMatchesEvent
            && stateMatchesEvent;
    }

    private static bool IsPhaseOneState(TvLifecycleState state)
    {
        return state is TvLifecycleState.Active
            or TvLifecycleState.CaughtUp
            or TvLifecycleState.SourceRemoved;
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

    private static TvSourceSnapshotRejectedException Rejected(string reason)
    {
        return new TvSourceSnapshotRejectedException(reason);
    }
}
