using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TraktTvClientTests
{
    private const string AccessToken = "access-token";
    private const string ClientId = "client-id";

    [Fact]
    public async Task AllReadOperations_SendExactAuthenticatedRequests()
    {
        RecordingHandler handler = new(request => request.PathAndQuery switch
        {
            "/sync/last_activities" => JsonResponse(ValidActivitiesJson()),
            "/sync/watchlist/shows/added/asc?page=1&limit=100" => JsonResponse("[]"),
            "/sync/progress/watched?hide_completed=false&hide_not_completed=false&only_rewatching=false&page=1&limit=100" => JsonResponse("[]"),
            "/shows/42/progress/watched?hidden=false&specials=false&count_specials=false" =>
                JsonResponse(ValidDetailedProgressJson()),
            "/shows/42?extended=full" => JsonResponse(ValidMetadataJson(42)),
            "/shows/42/seasons/3?extended=full" => JsonResponse("[]"),
            "/shows/42/seasons/0?extended=full" => JsonResponse("[]"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.PathAndQuery}")
        });
        TraktTvClient client = CreateClient(handler);

        await client.GetLastActivitiesAsync(AccessToken, CancellationToken.None);
        await client.GetWatchlistAsync(AccessToken, CancellationToken.None);
        await client.GetWatchedProgressAsync(AccessToken, CancellationToken.None);
        await client.GetDetailedProgressAsync(AccessToken, 42, CancellationToken.None);
        await client.GetShowMetadataAsync(AccessToken, 42, CancellationToken.None);
        await client.GetSeasonAsync(AccessToken, 42, 3, CancellationToken.None);
        await client.GetSeasonAsync(AccessToken, 42, 0, CancellationToken.None);

        handler.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/sync/last_activities",
            "/sync/watchlist/shows/added/asc?page=1&limit=100",
            "/sync/progress/watched?hide_completed=false&hide_not_completed=false&only_rewatching=false&page=1&limit=100",
            "/shows/42/progress/watched?hidden=false&specials=false&count_specials=false",
            "/shows/42?extended=full",
            "/shows/42/seasons/3?extended=full",
            "/shows/42/seasons/0?extended=full");
        handler.Requests.Should().OnlyContain(request =>
            request.Method == HttpMethod.Get
            && request.AuthorizationScheme == "Bearer"
            && request.AuthorizationParameter == AccessToken
            && request.ApiVersion == "2"
            && request.ApiKey == ClientId
            && request.UserAgent == "WatchlistApp/1.0 (+https://github.com/dlaczny/universal-watchlist-synchronizer)");
    }

    [Fact]
    public async Task GetLastActivitiesAsync_MapsRelevantUtcCursorCaseInsensitively()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {
              "EPISODES": { "WATCHED_AT": "2026-07-13T23:00:00+02:00" },
              "SHOWS": { "WATCHLISTED_AT": "2026-07-13T22:30:00+02:00" }
            }
            """));
        TraktTvClient client = CreateClient(handler);

        TraktActivityCursor result = await client.GetLastActivitiesAsync(
            AccessToken,
            CancellationToken.None);

        result.ShowWatchlistedAt.Should().Be(new DateTimeOffset(2026, 7, 13, 20, 30, 0, TimeSpan.Zero));
        result.EpisodeWatchedAt.Should().Be(new DateTimeOffset(2026, 7, 13, 21, 0, 0, TimeSpan.Zero));
        result.ShowWatchlistedAt.Offset.Should().Be(TimeSpan.Zero);
        result.EpisodeWatchedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task GetWatchlistAsync_FetchesAllPagesSequentiallyAndOrdersByTraktId()
    {
        RecordingHandler handler = new(request => request.PathAndQuery switch
        {
            "/sync/watchlist/shows/added/asc?page=1&limit=100" => PaginatedJsonResponse(
                WatchlistJson(30, "Third", "2026-07-13T12:00:00Z", 300, 3000, " TT0000030 "),
                2),
            "/sync/watchlist/shows/added/asc?page=2&limit=100" => PaginatedJsonResponse(
                WatchlistJson(10, "First", "2026-07-12T12:00:00Z", 100, 1000, "TT0000010"),
                2),
            _ => throw new InvalidOperationException($"Unexpected request: {request.PathAndQuery}")
        });
        TraktTvClient client = CreateClient(handler);

        TraktPagedResult<TraktWatchlistShow> result = await client.GetWatchlistAsync(
            AccessToken,
            CancellationToken.None);

        result.PageCount.Should().Be(2);
        result.PageSize.Should().Be(100);
        result.Items.Select(item => item.Ids.TraktId).Should().Equal(10, 30);
        result.Items[0].Should().Be(new TraktWatchlistShow(
            new TraktShowIds(10, 100, 1000, "tt0000010"),
            "First",
            2020,
            DateTimeOffset.Parse("2026-07-12T12:00:00Z")));
        handler.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/sync/watchlist/shows/added/asc?page=1&limit=100",
            "/sync/watchlist/shows/added/asc?page=2&limit=100");
    }

    [Fact]
    public async Task GetWatchlistAsync_UsesConfiguredPageSize()
    {
        RecordingHandler handler = new(request => request.PathAndQuery ==
            "/sync/watchlist/shows/added/asc?page=1&limit=37"
                ? JsonResponse("[]")
                : throw new InvalidOperationException($"Unexpected request: {request.PathAndQuery}"));
        TraktTvClient client = CreateClient(handler, pageSize: 37);

        TraktPagedResult<TraktWatchlistShow> result = await client.GetWatchlistAsync(
            AccessToken,
            CancellationToken.None);

        result.PageCount.Should().Be(1);
        result.PageSize.Should().Be(37);
        result.Items.Should().BeEmpty();
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task PaginatedRead_WhenConfiguredPageSizeIsUnsupported_FailsBeforeRequest(
        int pageSize)
    {
        RecordingHandler handler = new(_ => throw new InvalidOperationException(
            "No network request was expected."));
        TraktTvClient client = CreateClient(handler, pageSize);

        Func<Task> action = async () => await client.GetWatchlistAsync(
            AccessToken,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktUnavailableException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWatchedProgressAsync_FetchesAllPagesAndOrdersByTraktId()
    {
        RecordingHandler handler = new(request => request.PathAndQuery switch
        {
            "/sync/progress/watched?hide_completed=false&hide_not_completed=false&only_rewatching=false&page=1&limit=100" =>
                PaginatedJsonResponse(WatchedProgressJson(30, 8, 4), 2),
            "/sync/progress/watched?hide_completed=false&hide_not_completed=false&only_rewatching=false&page=2&limit=100" =>
                PaginatedJsonResponse(WatchedProgressJson(10, 20, 15), 2),
            _ => throw new InvalidOperationException($"Unexpected request: {request.PathAndQuery}")
        });
        TraktTvClient client = CreateClient(handler);

        TraktPagedResult<TraktWatchedShowProgress> result = await client.GetWatchedProgressAsync(
            AccessToken,
            CancellationToken.None);

        result.PageCount.Should().Be(2);
        result.PageSize.Should().Be(100);
        result.Items.Select(item => item.Ids.TraktId).Should().Equal(10, 30);
        result.Items[0].AiredEpisodes.Should().Be(20);
        result.Items[0].CompletedEpisodes.Should().Be(15);
        result.Items[0].NextEpisode.Should().Be(new TraktSeasonEpisode(
            10101,
            20101,
            2,
            1,
            "Next",
            DateTimeOffset.Parse("2026-08-01T20:00:00Z")));
        result.Items[0].LastEpisode.Should().Be(new TraktSeasonEpisode(
            10100,
            20100,
            1,
            10,
            "Last",
            DateTimeOffset.Parse("2026-07-01T20:00:00Z")));
    }

    [Theory]
    [InlineData("watchlist")]
    [InlineData("progress")]
    public async Task PaginatedReads_WhenNonemptyResponseHasNoPageCount_ThrowParseException(string operation)
    {
        string json = operation == "watchlist"
            ? WatchlistJson(10, "Show", "2026-07-13T12:00:00Z", 100, 1000, null)
            : WatchedProgressJson(10, 1, 0);
        RecordingHandler handler = new(_ => JsonResponse(json));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = operation == "watchlist"
            ? async () => await client.GetWatchlistAsync(AccessToken, CancellationToken.None)
            : async () => await client.GetWatchedProgressAsync(AccessToken, CancellationToken.None);

        TraktParseException exception = (await action.Should()
            .ThrowAsync<TraktParseException>())
            .Which;
        AssertSanitizedParseException(exception);
    }

    [Theory]
    [InlineData("watchlist")]
    [InlineData("progress")]
    public async Task PaginatedReads_WhenPageCountChanges_ThrowParseException(string operation)
    {
        RecordingHandler handler = new(request =>
        {
            bool isFirstPage = request.PathAndQuery.Contains("page=1", StringComparison.Ordinal);
            string json = operation == "watchlist"
                ? WatchlistJson(isFirstPage ? 10 : 20, "Show", "2026-07-13T12:00:00Z", 100, 1000, null)
                : WatchedProgressJson(isFirstPage ? 10 : 20, 1, 0);
            return PaginatedJsonResponse(json, isFirstPage ? 2 : 3);
        });
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = operation == "watchlist"
            ? async () => await client.GetWatchlistAsync(AccessToken, CancellationToken.None)
            : async () => await client.GetWatchedProgressAsync(AccessToken, CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task PaginatedRead_WhenPageCountIsUnreasonablyLarge_RejectsAfterFirstRequest()
    {
        RecordingHandler handler = new(request => request.PathAndQuery.Contains(
            "page=1",
            StringComparison.Ordinal)
                ? PaginatedJsonResponse(
                    WatchlistJson(10, "Show", "2026-07-13T12:00:00Z", 100, 1000, null),
                    int.MaxValue)
                : throw new InvalidOperationException("A second page must not be requested."));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetWatchlistAsync(
            AccessToken,
            CancellationToken.None);

        TraktParseException exception = (await action.Should()
            .ThrowAsync<TraktParseException>())
            .Which;
        AssertSanitizedParseException(exception);
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("watchlist")]
    [InlineData("progress")]
    public async Task PaginatedReads_WhenTraktIdRepeatsAcrossPages_ThrowParseException(string operation)
    {
        RecordingHandler handler = new(request =>
        {
            string json = operation == "watchlist"
                ? WatchlistJson(10, "Show", "2026-07-13T12:00:00Z", 100, 1000, null)
                : WatchedProgressJson(10, 1, 0);
            return PaginatedJsonResponse(json, 2);
        });
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = operation == "watchlist"
            ? async () => await client.GetWatchlistAsync(AccessToken, CancellationToken.None)
            : async () => await client.GetWatchedProgressAsync(AccessToken, CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
        handler.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("watchlist")]
    [InlineData("progress")]
    public async Task PaginatedReads_WhenResponseContainsNullRow_ThrowParseException(string operation)
    {
        RecordingHandler handler = new(_ => PaginatedJsonResponse("[null]", 1));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = operation == "watchlist"
            ? async () => await client.GetWatchlistAsync(AccessToken, CancellationToken.None)
            : async () => await client.GetWatchedProgressAsync(AccessToken, CancellationToken.None);

        TraktParseException exception = (await action.Should()
            .ThrowAsync<TraktParseException>())
            .Which;
        AssertSanitizedParseException(exception);
    }

    [Theory]
    [InlineData("watchlist", 0)]
    [InlineData("watchlist", -1)]
    [InlineData("progress", 0)]
    [InlineData("progress", -1)]
    public async Task ShowRows_WhenTraktIdIsNonpositive_ThrowParseException(
        string operation,
        long traktId)
    {
        string json = operation == "watchlist"
            ? WatchlistJson(traktId, "Show", "2026-07-13T12:00:00Z", 100, 1000, null)
            : WatchedProgressJson(traktId, 1, 0);
        RecordingHandler handler = new(_ => PaginatedJsonResponse(json, 1));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = operation == "watchlist"
            ? async () => await client.GetWatchlistAsync(AccessToken, CancellationToken.None)
            : async () => await client.GetWatchedProgressAsync(AccessToken, CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Theory]
    [InlineData("watchlist")]
    [InlineData("progress")]
    public async Task ShowRows_WhenTraktIdIsMissing_ThrowParseException(string operation)
    {
        string json = operation == "watchlist"
            ? """
                [
                  {
                    "listed_at": "2026-07-13T12:00:00Z",
                    "show": { "title": "Show", "year": 2020, "ids": { "tvdb": 100 } }
                  }
                ]
                """
            : """
                [
                  {
                    "show": { "title": "Show", "year": 2020, "ids": { "tvdb": 100 } },
                    "progress": { "aired": 0, "completed": 0, "next_episode": null, "last_episode": null }
                  }
                ]
                """;
        RecordingHandler handler = new(_ => PaginatedJsonResponse(json, 1));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = operation == "watchlist"
            ? async () => await client.GetWatchlistAsync(AccessToken, CancellationToken.None)
            : async () => await client.GetWatchedProgressAsync(AccessToken, CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(2, 3)]
    public async Task GetWatchedProgressAsync_WhenTotalsAreInvalid_ThrowsParseException(
        int aired,
        int completed)
    {
        RecordingHandler handler = new(_ => PaginatedJsonResponse(
            WatchedProgressJson(10, aired, completed),
            1));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetWatchedProgressAsync(
            AccessToken,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Theory]
    [InlineData("next_episode", "0", "1", "100", "200")]
    [InlineData("next_episode", "1", "0", "100", "200")]
    [InlineData("next_episode", "1", "1", "0", "200")]
    [InlineData("last_episode", "0", "1", "100", "200")]
    [InlineData("last_episode", "1", "0", "100", "200")]
    [InlineData("last_episode", "1", "1", "0", "200")]
    public async Task GetWatchedProgressAsync_WhenNextOrLastIdentityIsMalformed_ThrowsParseException(
        string property,
        string season,
        string episode,
        string trakt,
        string tvdb)
    {
        string json = WatchedProgressJson(10, 2, 1)
            .Replace($"\"{property}\": {{", $"\"{property}\": {{", StringComparison.Ordinal)
            .Replace(
                property == "next_episode" ? "\"season\": 2" : "\"season\": 1",
                $"\"season\": {season}",
                StringComparison.Ordinal)
            .Replace(
                property == "next_episode" ? "\"number\": 1" : "\"number\": 10",
                $"\"number\": {episode}",
                StringComparison.Ordinal)
            .Replace(
                property == "next_episode" ? "\"trakt\": 10101" : "\"trakt\": 10100",
                $"\"trakt\": {trakt}",
                StringComparison.Ordinal)
            .Replace(
                property == "next_episode" ? "\"tvdb\": 20101" : "\"tvdb\": 20100",
                $"\"tvdb\": {tvdb}",
                StringComparison.Ordinal);
        RecordingHandler handler = new(_ => PaginatedJsonResponse(json, 1));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetWatchedProgressAsync(
            AccessToken,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Theory]
    [InlineData("next_episode")]
    [InlineData("last_episode")]
    public async Task GetWatchedProgressAsync_WhenEpisodeTvdbIdIsZero_NormalizesItToMissing(
        string property)
    {
        string json = WatchedProgressJson(10, 2, 1)
            .Replace(
                property == "next_episode" ? "\"tvdb\": 20101" : "\"tvdb\": 20100",
                "\"tvdb\": 0",
                StringComparison.Ordinal);
        RecordingHandler handler = new(_ => PaginatedJsonResponse(json, 1));
        TraktTvClient client = CreateClient(handler);

        TraktPagedResult<TraktWatchedShowProgress> result = await client.GetWatchedProgressAsync(
            AccessToken,
            CancellationToken.None);

        TraktSeasonEpisode? episode = property == "next_episode"
            ? result.Items[0].NextEpisode
            : result.Items[0].LastEpisode;
        episode.Should().NotBeNull();
        episode!.TvdbId.Should().BeNull();
    }

    [Theory]
    [InlineData("next_episode")]
    [InlineData("last_episode")]
    public async Task GetWatchedProgressAsync_WhenNullableEpisodeMarkerIsMissing_ThrowsParseException(
        string missingProperty)
    {
        string remainingMarker = missingProperty == "next_episode"
            ? "\"last_episode\": null"
            : "\"next_episode\": null";
        string json = $$"""
            [
              {
                "show": {
                  "title": "Show",
                  "year": 2020,
                  "ids": { "trakt": 10 }
                },
                "progress": {
                  "aired": 0,
                  "completed": 0,
                  {{remainingMarker}}
                }
              }
            ]
            """;
        RecordingHandler handler = new(_ => PaginatedJsonResponse(json, 1));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetWatchedProgressAsync(
            AccessToken,
            CancellationToken.None);

        TraktParseException exception = (await action.Should()
            .ThrowAsync<TraktParseException>())
            .Which;
        AssertSanitizedParseException(exception);
    }

    [Fact]
    public async Task GetWatchedProgressAsync_WhenBothEpisodeMarkersAreExplicitNull_AcceptsRow()
    {
        RecordingHandler handler = new(_ => PaginatedJsonResponse("""
            [
              {
                "show": {
                  "title": "Show",
                  "year": 2020,
                  "ids": { "trakt": 10 }
                },
                "progress": {
                  "aired": 0,
                  "completed": 0,
                  "next_episode": null,
                  "last_episode": null
                }
              }
            ]
            """, 1));
        TraktTvClient client = CreateClient(handler);

        TraktPagedResult<TraktWatchedShowProgress> result = await client.GetWatchedProgressAsync(
            AccessToken,
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].NextEpisode.Should().BeNull();
        result.Items[0].LastEpisode.Should().BeNull();
    }

    [Fact]
    public async Task GetDetailedProgressAsync_MapsCanonicalSeasonsAndEpisodes()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {
              "aired": 3,
              "completed": 2,
              "seasons": [
                {
                  "number": 2,
                  "aired": 1,
                  "completed": 0,
                  "episodes": [
                    { "number": 1, "completed": false, "last_watched_at": null }
                  ]
                },
                {
                  "number": 1,
                  "aired": 2,
                  "completed": 2,
                  "episodes": [
                    { "number": 2, "completed": true, "last_watched_at": "2026-07-02T20:00:00+02:00" },
                    { "number": 1, "completed": true, "last_watched_at": "2026-07-01T20:00:00Z" }
                  ]
                }
              ]
            }
            """));
        TraktTvClient client = CreateClient(handler);

        TraktDetailedShowProgress result = await client.GetDetailedProgressAsync(
            AccessToken,
            42,
            CancellationToken.None);

        result.AiredEpisodes.Should().Be(3);
        result.CompletedEpisodes.Should().Be(2);
        result.Seasons.Select(season => season.SeasonNumber).Should().Equal(1, 2);
        result.Seasons[0].Episodes.Select(episode => episode.EpisodeNumber).Should().Equal(1, 2);
        result.Seasons[0].Episodes[1].Should().Be(new TraktDetailedEpisodeProgress(
            1,
            2,
            true,
            new DateTimeOffset(2026, 7, 2, 18, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(2, 3)]
    public async Task GetDetailedProgressAsync_WhenShowTotalsAreInvalid_ThrowsParseException(
        int aired,
        int completed)
    {
        string json = $$"""
            { "aired": {{aired}}, "completed": {{completed}}, "seasons": [] }
            """;
        RecordingHandler handler = new(_ => JsonResponse(json));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetDetailedProgressAsync(
            AccessToken,
            42,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetDetailedProgressAsync_WhenSeasonNumberRepeats_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {
              "aired": 0,
              "completed": 0,
              "seasons": [
                { "number": 1, "aired": 0, "completed": 0, "episodes": [] },
                { "number": 1, "aired": 0, "completed": 0, "episodes": [] }
              ]
            }
            """));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetDetailedProgressAsync(
            AccessToken,
            42,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetDetailedProgressAsync_WhenSeasonsContainNull_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            { "aired": 0, "completed": 0, "seasons": [null] }
            """));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetDetailedProgressAsync(
            AccessToken,
            42,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetDetailedProgressAsync_WhenEpisodeNumberRepeats_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {
              "aired": 1,
              "completed": 0,
              "seasons": [
                {
                  "number": 1,
                  "aired": 1,
                  "completed": 0,
                  "episodes": [
                    { "number": 1, "completed": false, "last_watched_at": null },
                    { "number": 1, "completed": false, "last_watched_at": null }
                  ]
                }
              ]
            }
            """));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetDetailedProgressAsync(
            AccessToken,
            42,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetDetailedProgressAsync_WhenEpisodeCompletedIsMissing_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {
              "aired": 1,
              "completed": 0,
              "seasons": [
                {
                  "number": 1,
                  "aired": 1,
                  "completed": 0,
                  "episodes": [
                    { "number": 1, "last_watched_at": null }
                  ]
                }
              ]
            }
            """));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetDetailedProgressAsync(
            AccessToken,
            42,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetDetailedProgressAsync_WhenEpisodesContainNull_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {
              "aired": 1,
              "completed": 0,
              "seasons": [
                { "number": 1, "aired": 1, "completed": 0, "episodes": [null] }
              ]
            }
            """));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetDetailedProgressAsync(
            AccessToken,
            42,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public async Task GetDetailedProgressAsync_WhenSeasonTotalsAreInvalid_ThrowsParseException(
        int aired,
        int completed)
    {
        string json = $$"""
            {
              "aired": 1,
              "completed": 0,
              "seasons": [
                { "number": 1, "aired": {{aired}}, "completed": {{completed}}, "episodes": [] }
              ]
            }
            """;
        RecordingHandler handler = new(_ => JsonResponse(json));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetDetailedProgressAsync(
            AccessToken,
            42,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetShowMetadataAsync_MapsMetadataAndPreservesUpstreamStatusVerbatim()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {
              "title": "  Example Show  ",
              "year": 2024,
              "overview": "  Overview  ",
              "status": "  RETURNING SERIES  ",
              "ids": {
                "trakt": 42,
                "tvdb": 100,
                "tmdb": 200,
                "imdb": " TT1234567 "
              }
            }
            """));
        TraktTvClient client = CreateClient(handler);

        TraktShowMetadata result = await client.GetShowMetadataAsync(
            AccessToken,
            42,
            CancellationToken.None);

        result.Should().Be(new TraktShowMetadata(
            new TraktShowIds(42, 100, 200, "tt1234567"),
            "Example Show",
            2024,
            "Overview",
            "  RETURNING SERIES  "));
    }

    [Theory]
    [InlineData("ended")]
    [InlineData("canceled")]
    [InlineData("Ended")]
    [InlineData("CANCELED")]
    [InlineData(" ended ")]
    public async Task GetShowMetadataAsync_PreservesStatusOrdinally(string status)
    {
        string json = $$"""
            {
              "title": "Show",
              "year": 2024,
              "status": "{{status}}",
              "ids": { "trakt": 42 }
            }
            """;
        RecordingHandler handler = new(_ => JsonResponse(json));
        TraktTvClient client = CreateClient(handler);

        TraktShowMetadata result = await client.GetShowMetadataAsync(
            AccessToken,
            42,
            CancellationToken.None);

        result.Status.Should().Be(status);
    }

    [Fact]
    public async Task GetShowMetadataAsync_WhenResponseIdentityConflicts_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse(ValidMetadataJson(43)));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetShowMetadataAsync(
            AccessToken,
            42,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetSeasonAsync_MapsAndOrdersFullEpisodeSchedule()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            [
              {
                "season": 3,
                "number": 2,
                "title": " Second ",
                "first_aired": "2026-08-02T20:00:00+02:00",
                "ids": { "trakt": 302, "tvdb": 1302 }
              },
              {
                "season": 3,
                "number": 1,
                "title": "First",
                "first_aired": null,
                "ids": { "trakt": 301, "tvdb": null }
              }
            ]
            """));
        TraktTvClient client = CreateClient(handler);

        IReadOnlyList<TraktSeasonEpisode> result = await client.GetSeasonAsync(
            AccessToken,
            42,
            3,
            CancellationToken.None);

        result.Select(episode => episode.EpisodeNumber).Should().Equal(1, 2);
        result[0].Should().Be(new TraktSeasonEpisode(301, null, 3, 1, "First", null));
        result[1].Should().Be(new TraktSeasonEpisode(
            302,
            1302,
            3,
            2,
            "Second",
            new DateTimeOffset(2026, 8, 2, 18, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task GetSeasonAsync_WhenSeasonZeroIsEmpty_ReturnsCompleteEmptyIdentitySet()
    {
        RecordingHandler handler = new(_ => JsonResponse("[]"));
        TraktTvClient client = CreateClient(handler);

        IReadOnlyList<TraktSeasonEpisode> result = await client.GetSeasonAsync(
            AccessToken,
            42,
            0,
            CancellationToken.None);

        result.Should().BeEmpty();
        handler.Requests.Should().ContainSingle(request =>
            request.PathAndQuery == "/shows/42/seasons/0?extended=full");
    }

    [Fact]
    public async Task GetSeasonAsync_WhenTvdbIdIsZero_NormalizesItToMissing()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            [
              {
                "season": 0,
                "number": 17,
                "ids": { "trakt": 2247122, "tvdb": 0 }
              }
            ]
            """));
        TraktTvClient client = CreateClient(handler);

        IReadOnlyList<TraktSeasonEpisode> result = await client.GetSeasonAsync(
            AccessToken,
            42,
            0,
            CancellationToken.None);

        result.Should().ContainSingle().Which.TvdbId.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetSeasonAsync_WhenEpisodeTraktIdIsNonpositive_ThrowsParseException(long episodeId)
    {
        string json = $$"""
            [
              {
                "season": 0,
                "number": 1,
                "title": "Special",
                "first_aired": null,
                "ids": { "trakt": {{episodeId}}, "tvdb": 1001 }
              }
            ]
            """;
        RecordingHandler handler = new(_ => JsonResponse(json));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetSeasonAsync(
            AccessToken,
            42,
            0,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetSeasonAsync_WhenEpisodeTraktIdIsMissing_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            [
              {
                "season": 0,
                "number": 1,
                "title": "Special",
                "ids": { "tvdb": 1001 }
              }
            ]
            """));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetSeasonAsync(
            AccessToken,
            42,
            0,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetSeasonAsync_WhenSeasonZeroContainsNumberedSeason_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse(SeasonJson(1001, 1, 1)));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetSeasonAsync(
            AccessToken,
            42,
            0,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetSeasonAsync_WhenEpisodeNumberRepeats_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            [
              { "season": 0, "number": 1, "ids": { "trakt": 1001, "tvdb": 2001 } },
              { "season": 0, "number": 1, "ids": { "trakt": 1002, "tvdb": 2002 } }
            ]
            """));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetSeasonAsync(
            AccessToken,
            42,
            0,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetSeasonAsync_WhenTraktEpisodeIdRepeats_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            [
              { "season": 0, "number": 1, "ids": { "trakt": 1001, "tvdb": 2001 } },
              { "season": 0, "number": 2, "ids": { "trakt": 1001, "tvdb": 2002 } }
            ]
            """));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetSeasonAsync(
            AccessToken,
            42,
            0,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Fact]
    public async Task GetSeasonAsync_WhenResponseContainsNullEpisode_ThrowsParseException()
    {
        RecordingHandler handler = new(_ => JsonResponse("[null]"));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetSeasonAsync(
            AccessToken,
            42,
            0,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktParseException>();
    }

    [Theory]
    [InlineData("activities")]
    [InlineData("watchlist")]
    [InlineData("progress")]
    [InlineData("detailed")]
    [InlineData("metadata")]
    [InlineData("season")]
    public async Task SuccessfulResponses_WhenJsonIsMalformed_ThrowSanitizedParseException(
        string operation)
    {
        RecordingHandler handler = new(_ => JsonResponse("[ secret-response-body"));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = Operation(client, operation);

        TraktParseException exception = (await action.Should()
            .ThrowAsync<TraktParseException>())
            .Which;
        AssertSanitizedParseException(exception);
        exception.ToString().Should().NotContain("secret-response-body");
        exception.ToString().Should().NotContain(AccessToken);
    }

    [Fact]
    public async Task Read_WhenRateLimited_MapsRetryAfterAndDoesNotRetry()
    {
        RecordingHandler handler = new(_ => Response(
            HttpStatusCode.TooManyRequests,
            "secret-rate-limit-response",
            response => response.Headers.TryAddWithoutValidation("Retry-After", "42")));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetWatchlistAsync(
            AccessToken,
            CancellationToken.None);

        TraktRateLimitedException exception = (await action.Should()
            .ThrowAsync<TraktRateLimitedException>())
            .Which;
        exception.RetryAfter.Should().Be(TimeSpan.FromSeconds(42));
        exception.Message.Should().Be("Trakt rate limited the request.");
        exception.InnerException.Should().BeNull();
        exception.ToString().Should().NotContain("secret-rate-limit-response");
        exception.ToString().Should().NotContain(AccessToken);
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Read_WhenDependencyReturnsOtherFailure_ThrowsUnavailableWithoutRetry(
        HttpStatusCode statusCode)
    {
        RecordingHandler handler = new(_ => Response(statusCode, "secret-upstream-response"));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetShowMetadataAsync(
            AccessToken,
            42,
            CancellationToken.None);

        TraktUnavailableException exception = (await action.Should()
            .ThrowAsync<TraktUnavailableException>())
            .Which;
        exception.ToString().Should().NotContain("secret-upstream-response");
        exception.ToString().Should().NotContain(AccessToken);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Read_WhenNetworkFails_ThrowsUnavailableWithoutSecretDiagnostics()
    {
        RecordingHandler handler = new(_ => throw new HttpRequestException(
            $"network failure with {AccessToken}"));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetLastActivitiesAsync(
            AccessToken,
            CancellationToken.None);

        TraktUnavailableException exception = (await action.Should()
            .ThrowAsync<TraktUnavailableException>())
            .Which;
        exception.InnerException.Should().BeNull();
        exception.ToString().Should().NotContain(AccessToken);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Read_WhenResponseContentTransportFails_ThrowsSanitizedUnavailable()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingContent(new HttpRequestException(
                $"content failure with {AccessToken}"))
        });
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetLastActivitiesAsync(
            AccessToken,
            CancellationToken.None);

        TraktUnavailableException exception = (await action.Should()
            .ThrowAsync<TraktUnavailableException>())
            .Which;
        exception.InnerException.Should().BeNull();
        exception.ToString().Should().NotContain(AccessToken);
    }

    [Fact]
    public async Task Read_WhenResponseContentThrowsIOException_ThrowsSanitizedUnavailable()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ThrowingReadStream(new IOException(
                $"stream reset with {AccessToken}")))
        });
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetLastActivitiesAsync(
            AccessToken,
            CancellationToken.None);

        TraktUnavailableException exception = (await action.Should()
            .ThrowAsync<TraktUnavailableException>())
            .Which;
        exception.InnerException.Should().BeNull();
        exception.ToString().Should().NotContain(AccessToken);
    }

    [Fact]
    public async Task Read_WhenResponseBodyBlocks_HttpClientTimeoutCoversBody()
    {
        using CancellationTokenSource callerCancellation = new(TimeSpan.FromSeconds(2));
        BlockingContent content = new();
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        });
        RecordingHttpClientFactory factory = new(handler, TimeSpan.FromMilliseconds(50));
        TraktTvClient client = new(factory, Options.Create(CreateOptions()));
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Func<Task> action = async () => await client.GetLastActivitiesAsync(
            AccessToken,
            callerCancellation.Token);

        await action.Should().ThrowAsync<TraktUnavailableException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
        callerCancellation.IsCancellationRequested.Should().BeFalse();
        content.Started.Task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Read_WhenTimeoutIsNotCallerCancellation_ThrowsUnavailable()
    {
        RecordingHandler handler = new(_ => throw new TaskCanceledException("upstream timeout"));
        TraktTvClient client = CreateClient(handler);

        Func<Task> action = async () => await client.GetLastActivitiesAsync(
            AccessToken,
            CancellationToken.None);

        await action.Should().ThrowAsync<TraktUnavailableException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ReturnedPaginatedCollections_CannotBeMutatedThroughListCast()
    {
        RecordingHandler handler = new(_ => PaginatedJsonResponse(
            WatchlistJson(10, "Show", "2026-07-13T12:00:00Z", 100, 1000, null),
            1));
        TraktTvClient client = CreateClient(handler);

        TraktPagedResult<TraktWatchlistShow> result = await client.GetWatchlistAsync(
            AccessToken,
            CancellationToken.None);
        IList<TraktWatchlistShow> mutableView = result.Items.Should()
            .BeAssignableTo<IList<TraktWatchlistShow>>()
            .Subject;

        Action action = () => mutableView[0] = result.Items[0] with { Title = "Changed" };
        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task Read_WhenCallerCancels_PropagatesCancellationWithoutMapping()
    {
        using CancellationTokenSource cancellationSource = new();
        BlockingHandler handler = new();
        RecordingHttpClientFactory factory = new(handler);
        TraktTvClient client = new(factory, Options.Create(CreateOptions()));

        Task<TraktActivityCursor> readTask = client.GetLastActivitiesAsync(
            AccessToken,
            cancellationSource.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellationSource.CancelAsync();

        Func<Task> action = async () => await readTask;
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SingletonClient_CreatesAndDisposesNamedFactoryClientPerOperation()
    {
        RecordingHandler handler = new(_ => JsonResponse(ValidActivitiesJson()));
        RecordingHttpClientFactory factory = new(handler);
        ServiceCollection services = new();
        services.AddSingleton<IHttpClientFactory>(factory);
        services.AddSingleton<IOptions<TraktOptions>>(Options.Create(CreateOptions()));
        services.AddSingleton<ITraktTvClient, TraktTvClient>();
        using ServiceProvider provider = services.BuildServiceProvider();
        ITraktTvClient firstResolution = provider.GetRequiredService<ITraktTvClient>();
        ITraktTvClient secondResolution = provider.GetRequiredService<ITraktTvClient>();

        await firstResolution.GetLastActivitiesAsync(AccessToken, CancellationToken.None);
        await secondResolution.GetLastActivitiesAsync(AccessToken, CancellationToken.None);

        firstResolution.Should().BeSameAs(secondResolution);
        factory.RequestedNames.Should().Equal("TraktTv", "TraktTv");
        factory.CreatedClients.Should().HaveCount(2);
        factory.CreatedClients.Should().OnlyContain(client => client.IsDisposed);
    }

    [Fact]
    public void AddWatchlistInfrastructure_RegistersSingletonTraktTvClientWithNamedHttpClient()
    {
        Dictionary<string, string?> settings = new()
        {
            ["MongoDb:ConnectionString"] = "mongodb://localhost:27017",
            ["MongoDb:DatabaseName"] = "watchlist-tests",
            ["Trakt:BaseUrl"] = "https://api.trakt.tv",
            ["Trakt:ClientId"] = ClientId
        };
        Microsoft.Extensions.Configuration.IConfiguration configuration =
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
        ServiceCollection services = new();
        services.AddWatchlistInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        ITraktTvClient first = provider.GetRequiredService<ITraktTvClient>();
        ITraktTvClient second = provider.GetRequiredService<ITraktTvClient>();

        first.Should().BeOfType<TraktTvClient>();
        second.Should().BeSameAs(first);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void AddWatchlistInfrastructure_WhenTraktPageSizeIsUnsupported_RejectsOptions(
        int pageSize)
    {
        Dictionary<string, string?> settings = new()
        {
            ["MongoDb:ConnectionString"] = "mongodb://localhost:27017",
            ["MongoDb:DatabaseName"] = "watchlist-tests",
            ["Trakt:BaseUrl"] = "https://api.trakt.tv",
            ["Trakt:ClientId"] = ClientId,
            ["Trakt:PageSize"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        ServiceCollection services = new();
        services.AddWatchlistInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Action action = () => _ = provider.GetRequiredService<IOptions<TraktOptions>>().Value;

        action.Should().Throw<OptionsValidationException>();
    }

    private static Func<Task> Operation(TraktTvClient client, string operation)
    {
        return operation switch
        {
            "activities" => async () => await client.GetLastActivitiesAsync(AccessToken, CancellationToken.None),
            "watchlist" => async () => await client.GetWatchlistAsync(AccessToken, CancellationToken.None),
            "progress" => async () => await client.GetWatchedProgressAsync(AccessToken, CancellationToken.None),
            "detailed" => async () => await client.GetDetailedProgressAsync(AccessToken, 42, CancellationToken.None),
            "metadata" => async () => await client.GetShowMetadataAsync(AccessToken, 42, CancellationToken.None),
            "season" => async () => await client.GetSeasonAsync(AccessToken, 42, 0, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
    }

    private static void AssertSanitizedParseException(TraktParseException exception)
    {
        exception.Message.Should().Be("The Trakt response could not be parsed.");
        exception.InnerException.Should().BeNull();
    }

    private static TraktTvClient CreateClient(RecordingHandler handler, int pageSize = 100)
    {
        RecordingHttpClientFactory factory = new(handler);
        return new TraktTvClient(factory, Options.Create(CreateOptions(pageSize)));
    }

    private static TraktOptions CreateOptions(int pageSize = 100)
    {
        return new TraktOptions
        {
            BaseUrl = "https://api.trakt.tv",
            ClientId = ClientId,
            PageSize = pageSize
        };
    }

    private static string ValidActivitiesJson()
    {
        return """
            {
              "episodes": { "watched_at": "2026-07-13T21:00:00Z" },
              "shows": { "watchlisted_at": "2026-07-13T20:30:00Z" }
            }
            """;
    }

    private static string ValidDetailedProgressJson()
    {
        return """
            { "aired": 0, "completed": 0, "seasons": [] }
            """;
    }

    private static string ValidMetadataJson(long traktId)
    {
        return $$"""
            {
              "title": "Show",
              "year": 2024,
              "overview": "Overview",
              "status": "returning series",
              "ids": {
                "trakt": {{traktId}},
                "tvdb": 100,
                "tmdb": 200,
                "imdb": "tt1234567"
              }
            }
            """;
    }

    private static string WatchlistJson(
        long traktId,
        string title,
        string listedAt,
        int? tvdbId,
        int? tmdbId,
        string? imdbId)
    {
        string tvdb = tvdbId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null";
        string tmdb = tmdbId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null";
        string imdb = imdbId is null ? "null" : $"\"{imdbId}\"";
        return $$"""
            [
              {
                "listed_at": "{{listedAt}}",
                "type": "show",
                "show": {
                  "title": "{{title}}",
                  "year": 2020,
                  "ids": {
                    "trakt": {{traktId}},
                    "tvdb": {{tvdb}},
                    "tmdb": {{tmdb}},
                    "imdb": {{imdb}}
                  }
                }
              }
            ]
            """;
    }

    private static string WatchedProgressJson(long traktId, int aired, int completed)
    {
        return $$"""
            [
              {
                "show": {
                  "title": "Show {{traktId}}",
                  "year": 2020,
                  "ids": {
                    "trakt": {{traktId}},
                    "tvdb": {{traktId + 100}},
                    "tmdb": {{traktId + 1000}},
                    "imdb": "TT{{traktId}}"
                  }
                },
                "progress": {
                  "aired": {{aired}},
                  "completed": {{completed}},
                  "next_episode": {
                    "season": 2,
                    "number": 1,
                    "title": "Next",
                    "first_aired": "2026-08-01T20:00:00Z",
                    "ids": { "trakt": 10101, "tvdb": 20101 }
                  },
                  "last_episode": {
                    "season": 1,
                    "number": 10,
                    "title": "Last",
                    "first_aired": "2026-07-01T20:00:00Z",
                    "ids": { "trakt": 10100, "tvdb": 20100 }
                  }
                }
              }
            ]
            """;
    }

    private static string SeasonJson(long traktEpisodeId, int seasonNumber, int episodeNumber)
    {
        return $$"""
            [
              {
                "season": {{seasonNumber}},
                "number": {{episodeNumber}},
                "title": "Episode",
                "first_aired": null,
                "ids": { "trakt": {{traktEpisodeId}}, "tvdb": 2001 }
              }
            ]
            """;
    }

    private static HttpResponseMessage PaginatedJsonResponse(string json, int pageCount)
    {
        return Response(
            HttpStatusCode.OK,
            json,
            response => response.Headers.TryAddWithoutValidation(
                "X-Pagination-Page-Count",
                pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return Response(HttpStatusCode.OK, json);
    }

    private static HttpResponseMessage Response(
        HttpStatusCode statusCode,
        string body,
        Action<HttpResponseMessage>? configure = null)
    {
        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        configure?.Invoke(response);
        return response;
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string PathAndQuery,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? ApiVersion,
        string? ApiKey,
        string UserAgent);

    private sealed class RecordingHandler(Func<RecordedRequest, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues("trakt-api-version", out IEnumerable<string>? apiVersionValues);
            request.Headers.TryGetValues("trakt-api-key", out IEnumerable<string>? apiKeyValues);
            RecordedRequest recordedRequest = new(
                request.Method,
                request.RequestUri!.PathAndQuery,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                apiVersionValues?.SingleOrDefault(),
                apiKeyValues?.SingleOrDefault(),
                request.Headers.UserAgent.ToString());
            Requests.Add(recordedRequest);
            return Task.FromResult(responseFactory(recordedRequest));
        }
    }

    private sealed class RecordingHttpClientFactory(
        HttpMessageHandler handler,
        TimeSpan? timeout = null)
        : IHttpClientFactory
    {
        public List<string> RequestedNames { get; } = [];

        public List<TrackingHttpClient> CreatedClients { get; } = [];

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            TrackingHttpClient client = new(handler)
            {
                BaseAddress = new Uri("https://api.trakt.tv"),
                Timeout = timeout ?? TimeSpan.FromSeconds(100)
            };
            CreatedClients.Add(client);
            return client;
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class ThrowingContent(Exception exception) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            return Task.FromException(exception);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class BlockingContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            return BlockAsync(CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            await BlockAsync(cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        private async Task BlockAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            using CancellationTokenSource fallback = new(TimeSpan.FromSeconds(1));
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                fallback.Token);
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
        }
    }

    private sealed class ThrowingReadStream(Exception exception) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw exception;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return Task.FromException<int>(exception);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<int>(exception);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TrackingHttpClient(HttpMessageHandler handler)
        : HttpClient(handler, disposeHandler: false)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
