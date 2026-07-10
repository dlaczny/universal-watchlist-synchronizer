using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TmdbTvWatchlistClientTests
{
    [Fact]
    public async Task GetWatchlistAsync_FetchesAllPagesWithSessionAndSort()
    {
        Dictionary<string, string> responses = new()
        {
            ["/3/account/123/watchlist/tv?language=en-US&page=1&session_id=session&sort_by=created_at.desc"] = """
            {
              "page": 1,
              "total_pages": 2,
              "results": [
                {
                  "id": 1399,
                  "name": "Game of Thrones",
                  "original_name": "Game of Thrones",
                  "overview": "Nine noble families fight for control.",
                  "first_air_date": "2011-04-17",
                  "poster_path": "/poster.jpg",
                  "backdrop_path": "/backdrop.jpg",
                  "original_language": "en",
                  "vote_average": 8.5,
                  "vote_count": 25000
                }
              ]
            }
            """,
            ["/3/account/123/watchlist/tv?language=en-US&page=2&session_id=session&sort_by=created_at.desc"] = """
            {
              "page": 2,
              "total_pages": 2,
              "results": [
                {
                  "id": 1436,
                  "name": "Justified",
                  "original_name": "Justified",
                  "overview": "",
                  "first_air_date": "2010-03-16",
                  "poster_path": null,
                  "backdrop_path": null,
                  "original_language": "en",
                  "vote_average": 7.9,
                  "vote_count": 558
                }
              ]
            }
            """
        };
        StaticTmdbHandler handler = new(responses);
        TmdbTvWatchlistClient client = CreateClient(handler);

        IReadOnlyList<TmdbTvWatchlistItemDto> result = await client.GetWatchlistAsync(CancellationToken.None);

        result.Select(item => item.TmdbId).Should().Equal(1399, 1436);
        handler.RequestedPathAndQueries.Should().Equal(responses.Keys);
    }

    [Fact]
    public async Task GetWatchlistAsync_WhenSinglePage_ReturnsItems()
    {
        Dictionary<string, string> responses = new()
        {
            ["/3/account/123/watchlist/tv?language=en-US&page=1&session_id=session&sort_by=created_at.desc"] = """
            {
              "page": 1,
              "total_pages": 1,
              "results": [
                {
                  "id": 1399,
                  "name": "Game of Thrones",
                  "original_name": "Game of Thrones",
                  "overview": null,
                  "first_air_date": null,
                  "poster_path": null,
                  "backdrop_path": null,
                  "original_language": null,
                  "vote_average": null,
                  "vote_count": null
                }
              ]
            }
            """
        };
        TmdbTvWatchlistClient client = CreateClient(responses);

        IReadOnlyList<TmdbTvWatchlistItemDto> result = await client.GetWatchlistAsync(CancellationToken.None);

        result.Should().ContainSingle();
        result[0].TmdbId.Should().Be(1399);
        result[0].Name.Should().Be("Game of Thrones");
        result[0].Overview.Should().BeNull();
        result[0].FirstAirDate.Should().BeNull();
        result[0].PosterPath.Should().BeNull();
        result[0].BackdropPath.Should().BeNull();
        result[0].OriginalLanguage.Should().BeNull();
        result[0].TmdbVoteAverage.Should().BeNull();
        result[0].TmdbVoteCount.Should().BeNull();
    }

    [Fact]
    public async Task GetWatchlistAsync_WhenAccountIdMissing_ThrowsTmdbUnavailableException()
    {
        TmdbTvWatchlistClient client = CreateClient(
            new Dictionary<string, string>(),
            accountId: null);

        Func<Task> action = () => client.GetWatchlistAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetWatchlistAsync_WhenSessionIdMissing_ThrowsTmdbUnavailableException()
    {
        TmdbTvWatchlistClient client = CreateClient(
            new Dictionary<string, string>(),
            sessionId: "");

        Func<Task> action = () => client.GetWatchlistAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetWatchlistAsync_WhenAccessTokenMissing_ThrowsTmdbUnavailableException()
    {
        TmdbTvWatchlistClient client = CreateClient(
            new Dictionary<string, string>(),
            accessToken: "");

        Func<Task> action = () => client.GetWatchlistAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Theory]
    [InlineData("__401__")]
    [InlineData("__403__")]
    [InlineData("__429__")]
    [InlineData("__500__")]
    public async Task GetWatchlistAsync_WhenTmdbDependencyFails_ThrowsTmdbUnavailableException(string responseMarker)
    {
        TmdbTvWatchlistClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/account/123/watchlist/tv?language=en-US&page=1&session_id=session&sort_by=created_at.desc"] = responseMarker
        });

        Func<Task> action = () => client.GetWatchlistAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetWatchlistAsync_WhenJsonMalformed_ThrowsTmdbParseException()
    {
        TmdbTvWatchlistClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/account/123/watchlist/tv?language=en-US&page=1&session_id=session&sort_by=created_at.desc"] = "["
        });

        Func<Task> action = () => client.GetWatchlistAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TmdbParseException>();
    }

    [Fact]
    public async Task GetWatchlistAsync_WhenNetworkFails_ThrowsTmdbUnavailableException()
    {
        HttpClient httpClient = new(new ThrowingTmdbHandler(new HttpRequestException("network down")))
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions("token", 123, "session");
        TmdbTvWatchlistClient client = new(httpClient, Options.Create(options), new ImmediateHttpRetryDelay());

        Func<Task> action = () => client.GetWatchlistAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetWatchlistAsync_WhenTimeoutIsNotCallerCancelled_ThrowsTmdbUnavailableException()
    {
        HttpClient httpClient = new(new ThrowingTmdbHandler(new TaskCanceledException("timeout")))
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions("token", 123, "session");
        TmdbTvWatchlistClient client = new(httpClient, Options.Create(options), new ImmediateHttpRetryDelay());

        Func<Task> action = () => client.GetWatchlistAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    private static TmdbTvWatchlistClient CreateClient(
        IReadOnlyDictionary<string, string> responses,
        int? accountId = 123,
        string sessionId = "session",
        string accessToken = "token")
    {
        HttpClient httpClient = new(new StaticTmdbHandler(responses))
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions(accessToken, accountId, sessionId);

        return new TmdbTvWatchlistClient(httpClient, Options.Create(options), new ImmediateHttpRetryDelay());
    }

    private static TmdbTvWatchlistClient CreateClient(StaticTmdbHandler handler)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions("token", 123, "session");

        return new TmdbTvWatchlistClient(httpClient, Options.Create(options), new ImmediateHttpRetryDelay());
    }

    private static TmdbOptions CreateOptions(string accessToken, int? accountId, string sessionId)
    {
        return new TmdbOptions
        {
            AccessToken = accessToken,
            BaseUrl = "https://api.themoviedb.org/3",
            ImageBaseUrl = "https://image.tmdb.org/t/p",
            AccountId = accountId,
            SessionId = sessionId,
            Language = "en-US"
        };
    }

    private sealed class ImmediateHttpRetryDelay : IHttpRetryDelay
    {
        public Task DelayAsync(int attempt, HttpResponseMessage? response, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
