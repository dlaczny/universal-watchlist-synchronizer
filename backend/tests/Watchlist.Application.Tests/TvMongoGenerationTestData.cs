using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

internal static class TvMongoGenerationTestData
{
    internal static readonly DateTimeOffset BaseTime =
        DateTimeOffset.Parse("2026-07-18T10:00:00Z");

    internal static TvGenerationDraft CreateDraft(
        string generationId,
        long traktId,
        string title,
        DateTimeOffset? completedAt = null,
        bool includeLifecycleEvent = true,
        bool includeSpecial = true)
    {
        DateTimeOffset completed = completedAt ?? BaseTime;
        DateTimeOffset started = completed.AddMinutes(-5);
        TvLifecycleDecision decision = new TvLifecycleEvaluator().Evaluate(
            previous: null,
            traktId,
            presentInCurrentSource: true,
            inWatchlist: true,
            airedEpisodes: 2,
            completedEpisodes: 1,
            TvGenerationKind.ScheduledFull,
            generationId,
            completed);
        TvEpisodeProgress watchedEpisode = new(
            checked(traktId * 100 + 1),
            checked((int)traktId * 100 + 1),
            1,
            1,
            "Pilot",
            completed.AddDays(-14),
            true,
            completed.AddDays(-13));
        TvEpisodeProgress nextEpisode = new(
            checked(traktId * 100 + 2),
            checked((int)traktId * 100 + 2),
            1,
            2,
            "Second",
            completed.AddDays(-7),
            false,
            null);
        TvProviderAvailability seasonAvailability = new(
            TvProviderState.ConfirmedUnavailable,
            "PL",
            completed.AddHours(-2),
            $"https://www.themoviedb.org/tv/{traktId}/season/1/watch",
            []);
        TvSeasonProgress season = new(
            1,
            2,
            1,
            true,
            seasonAvailability,
            [watchedEpisode, nextEpisode]);
        TvProviderAvailability availability = new(
            TvProviderState.Available,
            "PL",
            completed.AddHours(-1),
            $"https://www.themoviedb.org/tv/{traktId}/watch",
            [
                new TvProviderOffer(
                    119,
                    "Prime Video",
                    TvProviderCategory.Flatrate,
                    "https://image.tmdb.org/t/p/w500/prime.jpg"),
                new TvProviderOffer(
                    120,
                    "Public TV",
                    TvProviderCategory.Free,
                    null),
                new TvProviderOffer(
                    121,
                    "Ad-Supported TV",
                    TvProviderCategory.Ads,
                    null),
                new TvProviderOffer(
                    119,
                    "Prime Video",
                    TvProviderCategory.Rent,
                    null),
                new TvProviderOffer(
                    119,
                    "Prime Video",
                    TvProviderCategory.Buy,
                    null)
            ]);
        TvShow show = new(
            $"tv-trakt-{traktId}",
            traktId,
            checked((int)traktId + 100_000),
            checked((int)traktId + 200_000),
            $"tt{traktId:0000000}",
            TvIdentityStatus.Verified,
            title,
            2024,
            $"Overview for {title}",
            $"https://image.tmdb.org/t/p/w500/{traktId}-poster.jpg",
            $"https://image.tmdb.org/t/p/w1280/{traktId}-backdrop.jpg",
            "returning series",
            true,
            2,
            1,
            watchedEpisode,
            nextEpisode,
            [season],
            includeSpecial
                ? [new TvSpecialEpisodeIdentity(
                    checked(traktId * 100 + 3),
                    checked((int)traktId * 100 + 3),
                    0,
                    3)]
                : [],
            availability,
            decision.State,
            decision.Event!.Id,
            decision.LifecycleVersion,
            decision.MissingScheduledConfirmations,
            completed.AddMonths(-2),
            completed.AddMinutes(-1),
            completed.AddHours(-3),
            generationId,
            $"legacy-source-{traktId}");
        IReadOnlyList<TvLifecycleEvent> events = includeLifecycleEvent
            ? [decision.Event]
            : [];
        TvGenerationDraft draft = new(
            generationId,
            TvGenerationKind.ScheduledFull,
            started,
            completed,
            new TraktActivityCursor(completed.AddMinutes(-4), completed.AddMinutes(-3)),
            new TraktActivityCursor(completed.AddMinutes(-4), completed.AddMinutes(-3)),
            1,
            1,
            1,
            1,
            TvSnapshotValidator.RequestContractVersion,
            TvSnapshotValidator.CreateRequestFilters(100),
            new string('0', 64),
            new string('0', 64),
            [show],
            events,
            []);
        TvSnapshotValidator validator = new();
        return draft with
        {
            MembershipHash = validator.ComputeMembershipHash(draft.Shows),
            ProgressHash = validator.ComputeProgressHash(draft.Shows)
        };
    }

    internal static TvGenerationManifest CreateManifest(
        TvGenerationDraft draft,
        string? previousGenerationId = null,
        DateTimeOffset? publishedAt = null)
    {
        return TvGenerationManifest.CreatePhaseOne(
            draft,
            previousGenerationId,
            publishedAt ?? draft.CompletedAt.AddMinutes(1),
            draft.CompletedAt);
    }
}
