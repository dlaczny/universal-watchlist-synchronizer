using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Application.Tests;

public sealed class TvSnapshotValidatorTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedAt = StartedAt.AddMinutes(5);
    private static readonly TraktActivityCursor ActivityCursor = new(
        StartedAt.AddHours(-1),
        StartedAt.AddMinutes(-30));

    private readonly TvSnapshotValidator validator = new();

    [Fact]
    public void Validate_ValidDraft_Succeeds()
    {
        TvGenerationDraft draft = CreateValidDraft();

        Action action = () => validator.Validate(draft);

        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_ValidEmptyCurrentSource_Succeeds()
    {
        TvGenerationDraft draft = CreateDraft([], []);

        Action action = () => validator.Validate(draft);

        action.Should().NotThrow();
        draft.WatchlistPageCount.Should().Be(1);
        draft.WatchlistItemCount.Should().Be(0);
        draft.ProgressPageCount.Should().Be(1);
        draft.ProgressItemCount.Should().Be(0);
        draft.MembershipHash.Should().Be(Hash("[]"));
        draft.ProgressHash.Should().Be(Hash("[]"));
    }

    [Fact]
    public void Validate_ActivityCursorChangesDuringRead_IsRejected()
    {
        TvGenerationDraft draft = CreateValidDraft() with
        {
            ActivityAfter = ActivityCursor with
            {
                EpisodeWatchedAt = ActivityCursor.EpisodeWatchedAt.AddSeconds(1)
            }
        };

        AssertRejected(draft);
    }

    [Theory]
    [InlineData("trakt")]
    [InlineData("tvdb")]
    [InlineData("tmdb")]
    [InlineData("imdb")]
    public void Validate_DuplicateShowIdentity_IsRejected(string identity)
    {
        TvShow first = CreateShow(42);
        TvShow second = CreateShow(43);
        second = identity switch
        {
            "trakt" => second with { TraktId = first.TraktId, Id = first.Id },
            "tvdb" => second with { TvdbId = first.TvdbId },
            "tmdb" => second with { TmdbId = first.TmdbId },
            "imdb" => second with { ImdbId = first.ImdbId },
            _ => throw new InvalidOperationException()
        };
        TvGenerationDraft draft = CreateDraft([first, second], []);

        AssertRejected(draft);
    }

    [Theory]
    [InlineData("trakt_zero")]
    [InlineData("tvdb_zero")]
    [InlineData("tmdb_negative")]
    [InlineData("imdb_malformed")]
    [InlineData("public_id_mismatch")]
    [InlineData("identity_state")]
    public void Validate_InvalidShowIdentity_IsRejected(string invalidPart)
    {
        TvShow show = CreateShow(42);
        show = invalidPart switch
        {
            "trakt_zero" => show with { TraktId = 0, Id = "tv-trakt-0" },
            "tvdb_zero" => show with { TvdbId = 0 },
            "tmdb_negative" => show with { TmdbId = -1 },
            "imdb_malformed" => show with { ImdbId = "TT 42" },
            "public_id_mismatch" => show with { Id = "tv-trakt-43" },
            "identity_state" => show with { IdentityStatus = (TvIdentityStatus)99 },
            _ => throw new InvalidOperationException()
        };
        TvGenerationDraft draft = CreateDraft([show], []);

        AssertRejected(draft);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public void Validate_InvalidShowProgressCounts_AreRejected(int aired, int completed)
    {
        TvShow show = CreateShow(42) with
        {
            AiredEpisodes = aired,
            CompletedEpisodes = completed
        };
        TvGenerationDraft draft = CreateDraft([show], []);

        AssertRejected(draft);
    }

    [Theory]
    [InlineData("last_foreign")]
    [InlineData("next_foreign")]
    [InlineData("last_unwatched")]
    [InlineData("next_watched")]
    public void Validate_InvalidLastOrNextEpisodeReference_IsRejected(string invalidPart)
    {
        TvShow show = CreateShow(42);
        TvEpisodeProgress foreign = CreateEpisode(9_999, 19_999, 1, 9, watched: false);
        show = invalidPart switch
        {
            "last_foreign" => show with { LastWatchedEpisode = foreign with { Watched = true } },
            "next_foreign" => show with { NextEpisode = foreign },
            "last_unwatched" => show with { LastWatchedEpisode = show.LastWatchedEpisode! with { Watched = false } },
            "next_watched" => show with { NextEpisode = show.NextEpisode! with { Watched = true } },
            _ => throw new InvalidOperationException()
        };
        TvGenerationDraft draft = CreateDraft([show], []);

        AssertRejected(draft);
    }

    [Theory]
    [InlineData("null_season")]
    [InlineData("season_zero")]
    [InlineData("duplicate_season")]
    [InlineData("unordered_seasons")]
    [InlineData("null_episode")]
    [InlineData("episode_zero")]
    [InlineData("episode_season_mismatch")]
    [InlineData("duplicate_episode_number")]
    [InlineData("duplicate_episode_trakt")]
    [InlineData("duplicate_episode_tvdb")]
    [InlineData("unordered_episodes")]
    [InlineData("season_completed_mismatch")]
    [InlineData("show_totals_mismatch")]
    public void Validate_InvalidRegularSeasonOrEpisode_IsRejected(string invalidPart)
    {
        TvShow show = CreateShow(42);
        TvSeasonProgress season = show.Seasons[0];
        TvEpisodeProgress first = season.Episodes[0];
        TvEpisodeProgress second = season.Episodes[1];
        show = invalidPart switch
        {
            "null_season" => show with { Seasons = new TvSeasonProgress[] { null! } },
            "season_zero" => show with { Seasons = [season with { SeasonNumber = 0 }] },
            "duplicate_season" => show with { Seasons = [season, season] },
            "unordered_seasons" => show with
            {
                Seasons = [season with { SeasonNumber = 2, Episodes = [] }, season]
            },
            "null_episode" => show with
            {
                Seasons = [season with { Episodes = new TvEpisodeProgress[] { null! } }]
            },
            "episode_zero" => show with
            {
                Seasons = [season with { Episodes = [first with { EpisodeNumber = 0 }, second] }]
            },
            "episode_season_mismatch" => show with
            {
                Seasons = [season with { Episodes = [first with { SeasonNumber = 2 }, second] }]
            },
            "duplicate_episode_number" => show with
            {
                Seasons = [season with { Episodes = [first, second with { EpisodeNumber = 1 }] }]
            },
            "duplicate_episode_trakt" => show with
            {
                Seasons = [season with { Episodes = [first, second with { TraktEpisodeId = first.TraktEpisodeId }] }]
            },
            "duplicate_episode_tvdb" => show with
            {
                Seasons = [season with { Episodes = [first, second with { TvdbId = first.TvdbId }] }]
            },
            "unordered_episodes" => show with
            {
                Seasons = [season with { Episodes = [second, first] }]
            },
            "season_completed_mismatch" => show with
            {
                Seasons = [season with { CompletedEpisodes = 2 }]
            },
            "show_totals_mismatch" => show with { CompletedEpisodes = 2 },
            _ => throw new InvalidOperationException()
        };
        TvGenerationDraft draft = CreateDraft([show], []);

        AssertRejected(draft);
    }

    [Fact]
    public void Validate_GlobalTraktEpisodeIdentityReuseAcrossShows_IsRejected()
    {
        TvShow first = CreateShow(42);
        TvShow second = CreateShow(43);
        TvSeasonProgress secondSeason = second.Seasons[0];
        TvEpisodeProgress duplicate = secondSeason.Episodes[0] with
        {
            TraktEpisodeId = first.Seasons[0].Episodes[0].TraktEpisodeId
        };
        second = second with
        {
            Seasons = [secondSeason with { Episodes = [duplicate, secondSeason.Episodes[1]] }],
            LastWatchedEpisode = duplicate
        };

        AssertRejected(CreateDraft([first, second], []));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("season_nonzero")]
    [InlineData("trakt_zero")]
    [InlineData("tvdb_zero")]
    [InlineData("tvdb_null")]
    [InlineData("episode_zero")]
    [InlineData("duplicate_number")]
    [InlineData("duplicate_trakt")]
    [InlineData("duplicate_tvdb")]
    [InlineData("regular_overlap")]
    [InlineData("unordered")]
    public void Validate_InvalidSpecialIdentity_IsRejected(string invalidPart)
    {
        TvShow show = CreateShow(42);
        TvSpecialEpisodeIdentity first = new(8_001, 9_001, 0, 1);
        TvSpecialEpisodeIdentity second = new(8_002, 9_002, 0, 2);
        IReadOnlyList<TvSpecialEpisodeIdentity> specials = invalidPart switch
        {
            "null" => new TvSpecialEpisodeIdentity[] { null! },
            "season_nonzero" => [first with { SeasonNumber = 1 }],
            "trakt_zero" => [first with { TraktEpisodeId = 0 }],
            "tvdb_zero" => [first with { TvdbId = 0 }],
            "tvdb_null" => [first with { TvdbId = null }],
            "episode_zero" => [first with { EpisodeNumber = 0 }],
            "duplicate_number" => [first, second with { EpisodeNumber = 1 }],
            "duplicate_trakt" => [first, second with { TraktEpisodeId = first.TraktEpisodeId }],
            "duplicate_tvdb" => [first, second with { TvdbId = first.TvdbId }],
            "regular_overlap" => [first with { TraktEpisodeId = show.Seasons[0].Episodes[0].TraktEpisodeId }],
            "unordered" => [second, first],
            _ => throw new InvalidOperationException()
        };
        show = show with { SpecialEpisodeIdentities = specials };

        AssertRejected(CreateDraft([show], []));
    }

    [Theory]
    [InlineData("state_cleanup")]
    [InlineData("state_retired")]
    [InlineData("state_unknown")]
    [InlineData("version_zero")]
    [InlineData("confirmations_negative")]
    [InlineData("confirmations_too_many")]
    [InlineData("source_removed_confirmations")]
    [InlineData("last_event_blank")]
    [InlineData("last_event_malformed")]
    [InlineData("watchlisted_not_active")]
    public void Validate_InvalidPhaseOneLifecycleState_IsRejected(string invalidPart)
    {
        TvShow show = CreateShow(42);
        show = invalidPart switch
        {
            "state_cleanup" => show with { LifecycleState = TvLifecycleState.TerminalCleanupPending },
            "state_retired" => show with { LifecycleState = TvLifecycleState.RetiredTerminal },
            "state_unknown" => show with { LifecycleState = (TvLifecycleState)99 },
            "version_zero" => show with { LifecycleVersion = 0 },
            "confirmations_negative" => show with { MissingScheduledConfirmations = -1 },
            "confirmations_too_many" => show with { MissingScheduledConfirmations = 3 },
            "source_removed_confirmations" => show with
            {
                LifecycleState = TvLifecycleState.SourceRemoved,
                MissingScheduledConfirmations = 1
            },
            "last_event_blank" => show with { LastLifecycleEvent = "" },
            "last_event_malformed" => show with { LastLifecycleEvent = "tv:42:2:unknown" },
            "watchlisted_not_active" => show with { LifecycleState = TvLifecycleState.CaughtUp },
            _ => throw new InvalidOperationException()
        };

        AssertRejected(CreateDraft([show], []));
    }

    [Theory]
    [InlineData(TvLifecycleState.Active, "caught_up", 2L)]
    [InlineData(TvLifecycleState.CaughtUp, "source_removed", 2L)]
    [InlineData(TvLifecycleState.SourceRemoved, "added", 1L)]
    [InlineData(TvLifecycleState.Active, "added", 2L)]
    [InlineData(TvLifecycleState.Active, "reactivated", 1L)]
    public void Validate_LastLifecycleEventStateOrVersionIsImpossible_IsRejected(
        TvLifecycleState state,
        string eventType,
        long version)
    {
        TvShow show = CreateShowInLifecycleState(state) with
        {
            LifecycleVersion = version,
            LastLifecycleEvent = $"tv:42:{version}:{eventType}"
        };

        AssertRejected(CreateDraft([show], []));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("id")]
    [InlineData("show")]
    [InlineData("version")]
    [InlineData("generation")]
    [InlineData("type")]
    [InlineData("time")]
    [InlineData("hash_format")]
    [InlineData("hash_semantics")]
    [InlineData("reason")]
    public void Validate_InvalidLifecycleEvent_IsRejected(string invalidPart)
    {
        TvGenerationDraft valid = CreateValidDraft();
        TvLifecycleEvent source = valid.LifecycleEvents[0];
        IReadOnlyList<TvLifecycleEvent> events;
        if (invalidPart == "null")
        {
            events = new TvLifecycleEvent[] { null! };
        }
        else
        {
            TvLifecycleEvent invalid = invalidPart switch
            {
                "id" => source with { Id = "bad" },
                "show" => source with { TraktId = 99 },
                "version" => source with { Version = 0 },
                "generation" => source with { GenerationId = "another-generation" },
                "type" => source with { EventType = "terminal_cleanup_pending" },
                "time" => source with { OccurredAt = CompletedAt.AddSeconds(1) },
                "hash_format" => source with { PredicateHash = new string('A', 64) },
                "hash_semantics" => source with { PredicateHash = new string('0', 64) },
                "reason" => source with { Reason = "" },
                _ => throw new InvalidOperationException()
            };
            events = [invalid];
        }

        AssertRejected(valid with { LifecycleEvents = events });
    }

    [Fact]
    public void Validate_DuplicateOrUnorderedLifecycleEvents_AreRejected()
    {
        TvShow first = CreateShow(42);
        TvShow second = CreateShow(43);
        TvLifecycleEvent firstEvent = CreateAddedEvent(first);
        TvLifecycleEvent secondEvent = CreateAddedEvent(second);

        AssertRejected(CreateDraft([first, second], [firstEvent, firstEvent]));
        AssertRejected(CreateDraft([first, second], [secondEvent, firstEvent]));
    }

    [Fact]
    public void Validate_LifecycleEventTypeCannotProduceCurrentState_IsRejected()
    {
        TvShow show = CreateShow(42) with
        {
            InWatchlist = false,
            LifecycleState = TvLifecycleState.SourceRemoved,
            MissingScheduledConfirmations = 2
        };
        TvLifecycleEvent impossibleAddedEvent = new(
            "tv:42:1:added",
            42,
            1,
            "generation-1",
            "added",
            StartedAt.AddMinutes(1),
            Hash("[\"tv_lifecycle_event_v1\",42,\"added\",null,2,true,false,2,1,0]"),
            "tracked_source_added");

        AssertRejected(CreateDraft([show], [impossibleAddedEvent]));
    }

    [Fact]
    public void Validate_AddedLifecycleEventAfterVersionOne_IsRejected()
    {
        TvGenerationDraft valid = CreateValidDraft();
        TvShow show = valid.Shows[0] with
        {
            LifecycleVersion = 2,
            LastLifecycleEvent = "tv:42:2:added"
        };
        TvLifecycleEvent lifecycleEvent = valid.LifecycleEvents[0] with
        {
            Id = "tv:42:2:added",
            Version = 2
        };

        AssertRejected(CreateDraft([show], [lifecycleEvent]));
    }

    [Fact]
    public void Validate_TransitionLifecycleEventAtVersionOne_IsRejected()
    {
        TvShow source = CreateShow(42);
        TvSeasonProgress season = source.Seasons[0];
        TvEpisodeProgress completedSecond = season.Episodes[1] with
        {
            Watched = true,
            WatchedAt = season.Episodes[1].AiredAt!.Value.AddHours(1)
        };
        TvShow show = source with
        {
            InWatchlist = false,
            CompletedEpisodes = 2,
            LastWatchedEpisode = completedSecond,
            NextEpisode = null,
            Seasons = [season with { CompletedEpisodes = 2, Episodes = [season.Episodes[0], completedSecond] }],
            LifecycleState = TvLifecycleState.CaughtUp,
            LastLifecycleEvent = "tv:42:1:caught_up"
        };
        TvLifecycleEvent lifecycleEvent = new(
            "tv:42:1:caught_up",
            42,
            1,
            "generation-1",
            "caught_up",
            StartedAt.AddMinutes(1),
            Hash("[\"tv_lifecycle_event_v1\",42,\"caught_up\",0,1,true,false,2,2,0]"),
            "all_aired_episodes_watched");

        AssertRejected(CreateDraft([show], [lifecycleEvent]));
    }

    [Fact]
    public void Validate_CurrentLifecycleEventWithRetainedAbsenceConfirmation_IsRejected()
    {
        TvGenerationDraft valid = CreateValidDraft();
        TvShow show = valid.Shows[0] with { MissingScheduledConfirmations = 1 };

        AssertRejected(CreateDraft([show], valid.LifecycleEvents));
    }

    [Theory]
    [InlineData("generation_blank")]
    [InlineData("kind_unknown")]
    [InlineData("started_non_utc")]
    [InlineData("completed_non_utc")]
    [InlineData("completed_before_started")]
    [InlineData("cursor_non_utc")]
    [InlineData("cursor_after_completed")]
    [InlineData("watchlist_pages_zero")]
    [InlineData("watchlist_items_negative")]
    [InlineData("progress_pages_zero")]
    [InlineData("progress_items_negative")]
    [InlineData("contract_version")]
    [InlineData("filters")]
    [InlineData("membership_hash_format")]
    [InlineData("membership_hash_value")]
    [InlineData("progress_hash_format")]
    [InlineData("progress_hash_value")]
    public void Validate_InvalidDraftEnvelope_IsRejected(string invalidPart)
    {
        TvGenerationDraft draft = CreateValidDraft();
        draft = invalidPart switch
        {
            "generation_blank" => draft with { GenerationId = "" },
            "kind_unknown" => draft with { Kind = (TvGenerationKind)99 },
            "started_non_utc" => draft with { StartedAt = StartedAt.ToOffset(TimeSpan.FromHours(2)) },
            "completed_non_utc" => draft with { CompletedAt = CompletedAt.ToOffset(TimeSpan.FromHours(2)) },
            "completed_before_started" => draft with { CompletedAt = StartedAt.AddSeconds(-1) },
            "cursor_non_utc" => draft with
            {
                ActivityBefore = ActivityCursor with
                {
                    ShowWatchlistedAt = ActivityCursor.ShowWatchlistedAt.ToOffset(TimeSpan.FromHours(2))
                },
                ActivityAfter = ActivityCursor with
                {
                    ShowWatchlistedAt = ActivityCursor.ShowWatchlistedAt.ToOffset(TimeSpan.FromHours(2))
                }
            },
            "cursor_after_completed" => draft with
            {
                ActivityBefore = ActivityCursor with { EpisodeWatchedAt = CompletedAt.AddSeconds(1) },
                ActivityAfter = ActivityCursor with { EpisodeWatchedAt = CompletedAt.AddSeconds(1) }
            },
            "watchlist_pages_zero" => draft with { WatchlistPageCount = 0 },
            "watchlist_items_negative" => draft with { WatchlistItemCount = -1 },
            "progress_pages_zero" => draft with { ProgressPageCount = 0 },
            "progress_items_negative" => draft with { ProgressItemCount = -1 },
            "contract_version" => draft with { RequestContractVersion = "trakt-tv-v2" },
            "filters" => draft with
            {
                RequestFilters = new Dictionary<string, string> { ["watched.hide_completed"] = "true" }
            },
            "membership_hash_format" => draft with { MembershipHash = new string('A', 64) },
            "membership_hash_value" => draft with { MembershipHash = new string('0', 64) },
            "progress_hash_format" => draft with { ProgressHash = new string('A', 64) },
            "progress_hash_value" => draft with { ProgressHash = new string('0', 64) },
            _ => throw new InvalidOperationException()
        };

        AssertRejected(draft);
    }

    [Fact]
    public void Validate_PageCountsAreShapeCheckedWithoutInventingHeaderProvenance()
    {
        TvGenerationDraft draft = CreateValidDraft() with
        {
            WatchlistPageCount = 9,
            WatchlistItemCount = 1,
            ProgressPageCount = 7,
            ProgressItemCount = 1
        };

        Action action = () => validator.Validate(draft);

        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_NullShowOrEnrichmentError_IsRejected()
    {
        TvGenerationDraft draft = CreateValidDraft();

        AssertRejected(draft with { Shows = new TvShow[] { null! } });
        AssertRejected(draft with { EnrichmentErrors = new string[] { null! } });
    }

    [Fact]
    public void Validate_NoncanonicalShowOrdering_IsRejected()
    {
        TvShow first = CreateShow(42);
        TvShow second = CreateShow(43);

        AssertRejected(CreateDraft([second, first], []));
    }

    [Fact]
    public void Validate_InvalidProviderRowsAndTimestamps_AreRejected()
    {
        TvShow show = CreateShow(42);
        TvProviderAvailability invalidOffers = new(
            TvProviderState.Available,
            "PL",
            CompletedAt.AddMinutes(-1),
            null,
            new TvProviderOffer[] { null! });
        AssertRejected(CreateDraft([show with { Availability = invalidOffers }], []));

        TvProviderAvailability nonUtc = new(
            TvProviderState.Unknown,
            "PL",
            CompletedAt.ToOffset(TimeSpan.FromHours(2)),
            null,
            []);
        AssertRejected(CreateDraft([show with { Availability = nonUtc }], []));
    }

    [Fact]
    public void Validate_InvalidShowTimestampOrdering_IsRejected()
    {
        TvShow show = CreateShow(42);
        AssertRejected(CreateDraft([show with { AddedAt = show.UpdatedAt.AddSeconds(1) }], []));
    }

    [Fact]
    public void Validate_WatchedEpisodeBeforeCurrentAiredDate_IsAccepted()
    {
        TvShow show = CreateShow(42);
        TvSeasonProgress season = show.Seasons[0];
        TvEpisodeProgress first = season.Episodes[0] with
        {
            WatchedAt = season.Episodes[0].AiredAt!.Value.AddSeconds(-1)
        };
        TvShow correctedAirDate = show with
        {
            Seasons = [season with { Episodes = [first, season.Episodes[1]] }],
            LastWatchedEpisode = first
        };
        TvGenerationDraft draft = CreateDraft([correctedAirDate], [CreateAddedEvent(correctedAirDate)]);

        Action action = () => validator.Validate(draft);

        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_UnwatchedEpisodeWithWatchTimestamp_IsRejected()
    {
        TvShow show = CreateShow(42);
        TvSeasonProgress season = show.Seasons[0];
        TvEpisodeProgress second = season.Episodes[1] with
        {
            WatchedAt = season.Episodes[1].AiredAt!.Value.AddHours(1)
        };
        TvShow invalidEpisode = show with
        {
            Seasons = [season with { Episodes = [season.Episodes[0], second] }],
            NextEpisode = second
        };

        AssertRejected(CreateDraft([invalidEpisode], []));
    }

    [Fact]
    public void Validate_WatchedEpisodeWithoutOptionalWatchTimestamp_IsAccepted()
    {
        TvShow source = CreateShow(42);
        TvSeasonProgress season = source.Seasons[0];
        TvEpisodeProgress watched = season.Episodes[0] with { WatchedAt = null };
        TvShow show = source with
        {
            LastWatchedEpisode = watched,
            Seasons = [season with { Episodes = [watched, season.Episodes[1]] }]
        };
        TvGenerationDraft draft = CreateDraft([show], [CreateAddedEvent(show)]);

        Action action = () => validator.Validate(draft);

        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_CompletedShowWithoutOptionalLastEpisodeJoin_IsAccepted()
    {
        TvShow show = CreateShow(42) with { LastWatchedEpisode = null };
        TvGenerationDraft draft = CreateDraft([show], [CreateAddedEvent(show)]);

        Action action = () => validator.Validate(draft);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(" ended ")]
    [InlineData("CANCELED")]
    [InlineData("  RETURNING SERIES  ")]
    public void Validate_RawTraktStatusVariant_IsPreservedAndAccepted(string status)
    {
        TvShow show = CreateShow(42) with { TraktStatus = status };
        TvGenerationDraft draft = CreateDraft([show], [CreateAddedEvent(show)]);

        Action action = () => validator.Validate(draft);

        action.Should().NotThrow();
        draft.Shows[0].TraktStatus.Should().Be(status);
    }

    [Fact]
    public void Validate_WhitespaceOnlyTraktStatus_IsRejected()
    {
        TvShow show = CreateShow(42) with { TraktStatus = "   " };

        AssertRejected(CreateDraft([show], [CreateAddedEvent(show)]));
    }

    [Fact]
    public void Validate_FirstScheduledAbsenceOfUnstartedWatchlistShow_IsAccepted()
    {
        TvShow retained = CreateShow(42) with
        {
            InWatchlist = false,
            AiredEpisodes = 0,
            CompletedEpisodes = 0,
            LastWatchedEpisode = null,
            NextEpisode = null,
            Seasons = [],
            MissingScheduledConfirmations = 1
        };
        TvGenerationDraft draft = CreateDraft([retained], []);

        Action action = () => validator.Validate(draft);

        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_ActivityAbsenceOfCompletedExplicitWatchlistShow_IsAccepted()
    {
        TvShow source = CreateShow(42);
        TvSeasonProgress season = source.Seasons[0];
        TvEpisodeProgress completedSecond = season.Episodes[1] with
        {
            Watched = true,
            WatchedAt = season.Episodes[1].AiredAt!.Value.AddHours(1)
        };
        TvShow retained = source with
        {
            InWatchlist = false,
            CompletedEpisodes = 2,
            LastWatchedEpisode = completedSecond,
            NextEpisode = null,
            Seasons = [season with { CompletedEpisodes = 2, Episodes = [season.Episodes[0], completedSecond] }]
        };
        TvGenerationDraft draft = CreateDraft([retained], []);

        Action action = () => validator.Validate(draft);

        action.Should().NotThrow();
    }

    [Fact]
    public void ComputeMembershipHash_UsesCanonicalTraktMembershipAndSpecialIdentities()
    {
        TvShow first = CreateShow(42);
        TvShow second = CreateShow(43) with { InWatchlist = false };
        string expected = Hash("[[42,true,[]],[43,false,[]]]");

        string forward = validator.ComputeMembershipHash([first, second]);
        string reverse = validator.ComputeMembershipHash([second, first]);
        TvShow presentationChanged = first with
        {
            Title = "Different title",
            Availability = new TvProviderAvailability(
                TvProviderState.Available,
                "PL",
                CompletedAt,
                "https://example.invalid",
                [new TvProviderOffer(999, "Different provider", TvProviderCategory.Buy, null)]),
            UpdatedAt = CompletedAt
        };

        forward.Should().Be(expected);
        reverse.Should().Be(expected);
        validator.ComputeMembershipHash([presentationChanged, second]).Should().Be(expected);
    }

    [Fact]
    public void ComputeProgressHash_UsesEveryWatchedRegularEpisodeInCanonicalOrder()
    {
        TvShow show = CreateShow(42);
        string expected = Hash("[[42,2,1,[[1001,1,1]]]]");
        TvSeasonProgress season = show.Seasons[0];
        TvShow reordered = show with
        {
            Seasons = [season with { Episodes = season.Episodes.Reverse().ToArray() }],
            UpdatedAt = CompletedAt.AddDays(1),
            Title = "Presentation changed"
        };

        validator.ComputeProgressHash([show]).Should().Be(expected);
        validator.ComputeProgressHash([reordered]).Should().Be(expected);
    }

    [Fact]
    public void ComputeHashes_IncludeSpecialIdentitiesOnlyInMembershipHash()
    {
        TvShow show = CreateShow(42);
        TvShow withSpecial = show with
        {
            SpecialEpisodeIdentities = [new TvSpecialEpisodeIdentity(8_001, 9_001, 0, 1)]
        };

        validator.ComputeMembershipHash([withSpecial]).Should()
            .Be(Hash("[[42,true,[[8001,9001,0,1]]]]"));
        validator.ComputeMembershipHash([withSpecial]).Should()
            .NotBe(validator.ComputeMembershipHash([show]));
        validator.ComputeProgressHash([withSpecial]).Should().Be(validator.ComputeProgressHash([show]));
    }

    [Fact]
    public void CreateRequestFilters_ReturnsExactVersionOneSemanticsAndDefensiveOrdinalSnapshot()
    {
        IReadOnlyDictionary<string, string> filters = TvSnapshotValidator.CreateRequestFilters(100);

        filters.Should().HaveCount(12);
        filters["detailed.count_specials"].Should().Be("false");
        filters["detailed.hidden"].Should().Be("false");
        filters["detailed.specials"].Should().Be("false");
        filters["season.extended"].Should().Be("full");
        filters["show.extended"].Should().Be("full");
        filters["watched.hide_completed"].Should().Be("false");
        filters["watched.hide_not_completed"].Should().Be("false");
        filters["watched.limit"].Should().Be("100");
        filters["watched.only_rewatching"].Should().Be("false");
        filters["watchlist.limit"].Should().Be("100");
        filters["watchlist.order"].Should().Be("asc");
        filters["watchlist.sort"].Should().Be("added");
        filters.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void CreateRequestFilters_PageSizeAboveTraktMaximum_IsRejected()
    {
        Action action = () => TvSnapshotValidator.CreateRequestFilters(101);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Validate_RequestFilterPageSizeAboveTraktMaximum_IsRejected()
    {
        Dictionary<string, string> filters = new(TvSnapshotValidator.CreateRequestFilters(100))
        {
            ["watchlist.limit"] = "101",
            ["watched.limit"] = "101"
        };
        TvGenerationDraft draft = CreateValidDraft() with { RequestFilters = filters };

        AssertRejected(draft);
    }

    [Fact]
    public void GenerationContracts_DefensivelySnapshotEverySuppliedCollection()
    {
        TvShow show = CreateShow(42);
        TvLifecycleEvent lifecycleEvent = CreateAddedEvent(show);
        List<TvShow> shows = [show];
        List<TvLifecycleEvent> events = [lifecycleEvent];
        List<string> errors = ["provider_unavailable"];
        Dictionary<string, string> filters = new(TvSnapshotValidator.CreateRequestFilters(100));
        TvGenerationDraft draft = CreateDraft(shows, events, filters, errors);

        shows.Clear();
        events.Clear();
        errors.Clear();
        filters["watched.hide_completed"] = "true";

        draft.Shows.Should().ContainSingle();
        draft.LifecycleEvents.Should().ContainSingle();
        draft.EnrichmentErrors.Should().ContainSingle().Which.Should().Be("provider_unavailable");
        draft.RequestFilters["watched.hide_completed"].Should().Be("false");

        List<string> validationFailures = [];
        List<string> lifecycleIds = [lifecycleEvent.Id];
        List<string> cleanupIds = [];
        List<string> healthReasons =
        [
            "plex_history_phase_not_implemented",
            "worker_tv_mutation_disabled"
        ];
        List<string> manifestErrors = ["provider_unavailable"];
        Dictionary<string, string> manifestFilters = new(TvSnapshotValidator.CreateRequestFilters(100));
        TvGenerationManifest manifest = CreateDirectManifest(
            manifestFilters,
            validationFailures,
            lifecycleIds,
            cleanupIds,
            healthReasons,
            manifestErrors);

        manifestFilters.Clear();
        validationFailures.Add("late_failure");
        lifecycleIds.Clear();
        cleanupIds.Add("late_cleanup");
        healthReasons.Clear();
        manifestErrors.Clear();

        manifest.RequestFilters.Should().NotBeEmpty();
        manifest.ValidationFailureReasons.Should().BeEmpty();
        manifest.LifecycleEventIds.Should().ContainSingle();
        manifest.CleanupEventIds.Should().BeEmpty();
        manifest.HealthReasons.Should().Equal(
            "plex_history_phase_not_implemented",
            "worker_tv_mutation_disabled");
        manifest.EnrichmentErrors.Should().ContainSingle();
    }

    [Fact]
    public void CreatePhaseOne_ValidatesDraftAndLocksNonMutationManifestEnvelope()
    {
        TvGenerationDraft draft = CreateValidDraft();

        TvGenerationManifest manifest = TvGenerationManifest.CreatePhaseOne(
            draft,
            previousGenerationId: "generation-0",
            publishedAt: CompletedAt.AddSeconds(1),
            providerEnrichmentCompletedAt: CompletedAt);

        manifest.GenerationId.Should().Be(draft.GenerationId);
        manifest.PreviousGenerationId.Should().Be("generation-0");
        manifest.ValidationStatus.Should().Be("valid");
        manifest.ValidationFailureReasons.Should().BeEmpty();
        manifest.PlexHistoryCollectedAt.Should().BeNull();
        manifest.PlexHistoryWatermark.Should().BeNull();
        manifest.LastScheduledFullAt.Should().Be(CompletedAt.AddSeconds(1));
        manifest.CleanupEventIds.Should().BeEmpty();
        manifest.MutationCapable.Should().BeFalse();
        manifest.HealthReasons.Should().Equal(
            "plex_history_phase_not_implemented",
            "worker_tv_mutation_disabled");
        manifest.LifecycleEventIds.Should().Equal(draft.LifecycleEvents.Select(item => item.Id));
        Action action = () => validator.Validate(manifest);
        action.Should().NotThrow();
    }

    [Fact]
    public void CreatePhaseOne_ActivityFull_PreservesPriorScheduledPublicationTime()
    {
        TvGenerationDraft draft = CreateValidDraft() with
        {
            Kind = TvGenerationKind.ActivityFull
        };
        DateTimeOffset priorScheduledAt = CompletedAt.AddMinutes(-30);

        TvGenerationManifest manifest = TvGenerationManifest.CreatePhaseOne(
            draft,
            previousGenerationId: "generation-0",
            publishedAt: CompletedAt.AddSeconds(1),
            providerEnrichmentCompletedAt: CompletedAt,
            previousLastScheduledFullAt: priorScheduledAt);

        manifest.LastScheduledFullAt.Should().Be(priorScheduledAt);
        Action action = () => validator.Validate(manifest);
        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_LegacyManifestWithoutLastScheduledPublicationTime_RemainsReadable()
    {
        TvGenerationManifest manifest = TvGenerationManifest.CreatePhaseOne(
            CreateValidDraft(),
            previousGenerationId: null,
            publishedAt: CompletedAt.AddSeconds(1),
            providerEnrichmentCompletedAt: CompletedAt) with
        {
            LastScheduledFullAt = null
        };

        Action action = () => validator.Validate(manifest);

        action.Should().NotThrow();
    }

    [Fact]
    public void CreatePhaseOne_InvalidDraft_IsRejectedBeforeManifestConstruction()
    {
        TvGenerationDraft invalid = CreateValidDraft() with { MembershipHash = new string('0', 64) };

        Action action = () => TvGenerationManifest.CreatePhaseOne(
            invalid,
            previousGenerationId: null,
            publishedAt: CompletedAt.AddSeconds(1),
            providerEnrichmentCompletedAt: null);

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    [Theory]
    [InlineData("mutation")]
    [InlineData("health")]
    [InlineData("plex_collected")]
    [InlineData("plex_watermark")]
    [InlineData("cleanup")]
    [InlineData("validation_status")]
    [InlineData("validation_failures")]
    [InlineData("published_before_completed")]
    [InlineData("lifecycle_added_version")]
    [InlineData("lifecycle_transition_version")]
    [InlineData("lifecycle_noncanonical_id")]
    [InlineData("lifecycle_duplicate_show")]
    [InlineData("lifecycle_unordered_shows")]
    public void Validate_UnsafePhaseOneManifest_IsRejected(string invalidPart)
    {
        TvGenerationManifest manifest = TvGenerationManifest.CreatePhaseOne(
            CreateValidDraft(),
            null,
            CompletedAt.AddSeconds(1),
            CompletedAt);
        manifest = invalidPart switch
        {
            "mutation" => manifest with { MutationCapable = true },
            "health" => manifest with { HealthReasons = ["worker_tv_mutation_disabled"] },
            "plex_collected" => manifest with { PlexHistoryCollectedAt = CompletedAt },
            "plex_watermark" => manifest with { PlexHistoryWatermark = CompletedAt },
            "cleanup" => manifest with { CleanupEventIds = ["cleanup"] },
            "validation_status" => manifest with { ValidationStatus = "invalid" },
            "validation_failures" => manifest with { ValidationFailureReasons = ["failure"] },
            "published_before_completed" => manifest with { PublishedAt = CompletedAt.AddSeconds(-1) },
            "lifecycle_added_version" => manifest with
            {
                LifecycleEventIds = ["tv:42:2:added"]
            },
            "lifecycle_transition_version" => manifest with
            {
                LifecycleEventIds = ["tv:42:1:caught_up"]
            },
            "lifecycle_noncanonical_id" => manifest with
            {
                LifecycleEventIds = ["tv:042:01:added"]
            },
            "lifecycle_duplicate_show" => manifest with
            {
                LifecycleEventIds = ["tv:42:1:added", "tv:42:2:reactivated"]
            },
            "lifecycle_unordered_shows" => manifest with
            {
                LifecycleEventIds = ["tv:43:1:added", "tv:42:1:added"]
            },
            _ => throw new InvalidOperationException()
        };

        Action action = () => validator.Validate(manifest);

        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    private void AssertRejected(TvGenerationDraft draft)
    {
        Action action = () => validator.Validate(draft);
        action.Should().Throw<TvSourceSnapshotRejectedException>();
    }

    private TvGenerationDraft CreateValidDraft()
    {
        TvShow show = CreateShow(42);
        return CreateDraft([show], [CreateAddedEvent(show)]);
    }

    private TvGenerationDraft CreateDraft(
        IReadOnlyList<TvShow> shows,
        IReadOnlyList<TvLifecycleEvent> events,
        IReadOnlyDictionary<string, string>? filters = null,
        IReadOnlyList<string>? enrichmentErrors = null)
    {
        return new TvGenerationDraft(
            "generation-1",
            TvGenerationKind.ScheduledFull,
            StartedAt,
            CompletedAt,
            ActivityCursor,
            ActivityCursor,
            1,
            shows.Count(item => item is not null && item.InWatchlist),
            1,
            shows.Count(item => item is not null && item.AiredEpisodes > 0),
            TvSnapshotValidator.RequestContractVersion,
            filters ?? TvSnapshotValidator.CreateRequestFilters(100),
            validator.ComputeMembershipHash(shows),
            validator.ComputeProgressHash(shows),
            shows,
            events,
            enrichmentErrors ?? []);
    }

    private static TvShow CreateShow(long traktId)
    {
        long suffix = traktId - 41;
        TvEpisodeProgress first = CreateEpisode(
            1_000 + suffix,
            checked((int)(2_000 + suffix)),
            1,
            1,
            watched: true);
        TvEpisodeProgress second = CreateEpisode(
            1_100 + suffix,
            checked((int)(2_100 + suffix)),
            1,
            2,
            watched: false);
        TvSeasonProgress season = new(
            1,
            2,
            1,
            false,
            TvProviderAvailability.Unknown("PL"),
            [first, second]);
        long version = 1;
        return new TvShow(
            $"tv-trakt-{traktId}",
            traktId,
            checked((int)(4_200 + suffix)),
            checked((int)(5_200 + suffix)),
            $"tt{traktId:0000000}",
            TvIdentityStatus.Verified,
            $"Example {traktId}",
            2026,
            "Overview",
            null,
            null,
            "returning series",
            true,
            2,
            1,
            first,
            second,
            [season],
            [],
            TvProviderAvailability.Unknown("PL"),
            TvLifecycleState.Active,
            $"tv:{traktId}:{version}:added",
            version,
            0,
            StartedAt.AddDays(-10),
            StartedAt.AddMinutes(-1),
            StartedAt.AddMinutes(-1),
            "generation-1",
            null);
    }

    private static TvShow CreateShowInLifecycleState(TvLifecycleState state)
    {
        TvShow source = CreateShow(42);
        if (state == TvLifecycleState.Active)
        {
            return source;
        }

        if (state == TvLifecycleState.SourceRemoved)
        {
            return source with
            {
                InWatchlist = false,
                LifecycleState = state,
                MissingScheduledConfirmations = 2
            };
        }

        TvSeasonProgress season = source.Seasons[0];
        TvEpisodeProgress completedSecond = season.Episodes[1] with
        {
            Watched = true,
            WatchedAt = season.Episodes[1].AiredAt!.Value.AddHours(1)
        };
        return source with
        {
            InWatchlist = false,
            CompletedEpisodes = 2,
            LastWatchedEpisode = completedSecond,
            NextEpisode = null,
            Seasons = [season with { CompletedEpisodes = 2, Episodes = [season.Episodes[0], completedSecond] }],
            LifecycleState = state
        };
    }

    private static TvEpisodeProgress CreateEpisode(
        long traktEpisodeId,
        int tvdbId,
        int seasonNumber,
        int episodeNumber,
        bool watched)
    {
        DateTimeOffset airedAt = StartedAt.AddDays(-episodeNumber);
        return new TvEpisodeProgress(
            traktEpisodeId,
            tvdbId,
            seasonNumber,
            episodeNumber,
            $"Episode {episodeNumber}",
            airedAt,
            watched,
            watched ? airedAt.AddHours(1) : null);
    }

    private static TvLifecycleEvent CreateAddedEvent(TvShow show)
    {
        TvLifecycleDecision decision = new TvLifecycleEvaluator().Evaluate(
            previous: null,
            show.TraktId,
            presentInCurrentSource: true,
            show.InWatchlist,
            show.AiredEpisodes,
            show.CompletedEpisodes,
            TvGenerationKind.ScheduledFull,
            "generation-1",
            StartedAt.AddMinutes(1));
        return decision.Event!;
    }

    private TvGenerationManifest CreateDirectManifest(
        IReadOnlyDictionary<string, string> filters,
        IReadOnlyList<string> validationFailures,
        IReadOnlyList<string> lifecycleIds,
        IReadOnlyList<string> cleanupIds,
        IReadOnlyList<string> healthReasons,
        IReadOnlyList<string> enrichmentErrors)
    {
        TvGenerationDraft draft = CreateValidDraft();
        return new TvGenerationManifest(
            draft.GenerationId,
            null,
            draft.Kind,
            draft.StartedAt,
            draft.CompletedAt,
            draft.CompletedAt.AddSeconds(1),
            draft.ActivityAfter,
            draft.WatchlistPageCount,
            draft.WatchlistItemCount,
            draft.ProgressPageCount,
            draft.ProgressItemCount,
            draft.RequestContractVersion,
            filters,
            draft.MembershipHash,
            draft.ProgressHash,
            null,
            null,
            draft.CompletedAt,
            "valid",
            validationFailures,
            lifecycleIds,
            cleanupIds,
            false,
            healthReasons,
            enrichmentErrors);
    }

    private static string Hash(string canonicalJson)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
            .ToLowerInvariant();
    }
}
