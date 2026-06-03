using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class LetterboxdWatchlistClientTests
{
    [Fact]
    public async Task GetMoviesAsync_WhenJsonValid_ParsesMovies()
    {
        const string json = """
        [
          {
            "id": 1418998,
            "imdb_id": "tt35450621",
            "title": "Karma",
            "release_year": "2026",
            "clean_title": "/film/karma-2026/",
            "adult": false
          },
          {
            "id": 1635594,
            "imdb_id": "tt39883390",
            "title": "Ti Amo!",
            "release_year": "",
            "clean_title": "/film/ti-amo-1/",
            "adult": false
          }
        ]
        """;
        LetterboxdWatchlistClient client = CreateClient(HttpStatusCode.OK, json);

        IReadOnlyList<LetterboxdMovieDto> movies = await client.GetMoviesAsync(CancellationToken.None);

        movies.Should().Equal(
            new LetterboxdMovieDto("1418998", "tt35450621", "Karma", 2026, "/film/karma-2026/"),
            new LetterboxdMovieDto("1635594", "tt39883390", "Ti Amo!", null, "/film/ti-amo-1/"));
    }

    [Fact]
    public async Task GetMoviesAsync_WhenOptionalSourceFieldsEmpty_MapsThemToNull()
    {
        const string json = """
        [
          {
            "id": 1635594,
            "imdb_id": "",
            "title": "Ti Amo!",
            "release_year": "",
            "clean_title": "",
            "adult": false
          }
        ]
        """;
        LetterboxdWatchlistClient client = CreateClient(HttpStatusCode.OK, json);

        IReadOnlyList<LetterboxdMovieDto> movies = await client.GetMoviesAsync(CancellationToken.None);

        movies.Should().Equal(new LetterboxdMovieDto("1635594", null, "Ti Amo!", null, null));
    }

    [Fact]
    public async Task GetMoviesAsync_WhenProxyUnavailable_ThrowsLetterboxdUnavailableException()
    {
        LetterboxdWatchlistClient client = CreateClient(HttpStatusCode.ServiceUnavailable, "unavailable");

        Func<Task> action = () => client.GetMoviesAsync(CancellationToken.None);

        await action.Should().ThrowAsync<LetterboxdUnavailableException>();
    }

    [Fact]
    public async Task GetMoviesAsync_WhenJsonMalformed_ThrowsLetterboxdParseException()
    {
        LetterboxdWatchlistClient client = CreateClient(HttpStatusCode.OK, "[");

        Func<Task> action = () => client.GetMoviesAsync(CancellationToken.None);

        await action.Should().ThrowAsync<LetterboxdParseException>();
    }

    private static LetterboxdWatchlistClient CreateClient(HttpStatusCode statusCode, string content)
    {
        HttpClient httpClient = new(new StaticHttpMessageHandler(statusCode, content));
        LetterboxdOptions options = new()
        {
            WatchlistUrl = "https://example.test/example-user/watchlist"
        };

        return new LetterboxdWatchlistClient(httpClient, Options.Create(options));
    }

    private sealed class StaticHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(content)
            };

            return Task.FromResult(response);
        }
    }
}
