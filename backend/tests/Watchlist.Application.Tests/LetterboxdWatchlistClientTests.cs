using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task GetMoviesAsync_WhenProxyInitiallyUnavailable_RetriesAndParsesMovies()
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
          }
        ]
        """;
        SequenceHttpMessageHandler handler = new(new Queue<HttpResponseMessage>([
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("wake up")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            }
        ]));
        HttpClient httpClient = new(handler);
        LetterboxdOptions options = new()
        {
            WatchlistUrl = "https://example.test/example-user/watchlist"
        };
        LetterboxdWatchlistClient client = new(
            httpClient,
            Options.Create(options),
            new ImmediateHttpRetryDelay());

        IReadOnlyList<LetterboxdMovieDto> movies = await client.GetMoviesAsync(CancellationToken.None);

        movies.Should().Equal(new LetterboxdMovieDto("1418998", "tt35450621", "Karma", 2026, "/film/karma-2026/"));
        handler.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task GetMoviesAsync_WhenProxyCannotBeReached_ThrowsLetterboxdUnavailableException()
    {
        HttpClient httpClient = new(new ThrowingHttpMessageHandler(new HttpRequestException("network down")));
        LetterboxdOptions options = new()
        {
            WatchlistUrl = "https://example.test/example-user/watchlist"
        };
        LetterboxdWatchlistClient client = new(httpClient, Options.Create(options), new ImmediateHttpRetryDelay());

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

    [Theory]
    [InlineData("""[{ "id": 0, "title": "Karma", "release_year": "2026" }]""")]
    [InlineData("""[{ "id": 1418998, "title": "", "release_year": "2026" }]""")]
    [InlineData("""[{ "id": 1418998, "release_year": "2026" }]""")]
    public async Task GetMoviesAsync_WhenRequiredSourceFieldInvalid_ThrowsLetterboxdParseException(string json)
    {
        LetterboxdWatchlistClient client = CreateClient(HttpStatusCode.OK, json);

        Func<Task> action = () => client.GetMoviesAsync(CancellationToken.None);

        await action.Should().ThrowAsync<LetterboxdParseException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("watchlist")]
    [InlineData("ftp://example.test/watchlist")]
    public void AddWatchlistInfrastructure_WhenLetterboxdUrlInvalid_RejectsOptions(string watchlistUrl)
    {
        Dictionary<string, string?> values = new()
        {
            ["MongoDb:ConnectionString"] = "mongodb://localhost:27017",
            ["MongoDb:DatabaseName"] = "watchlist",
            ["MongoDb:WatchlistItemsCollectionName"] = "watchlist_items",
            ["MongoDb:SyncRunsCollectionName"] = "sync_runs",
            ["Letterboxd:WatchlistUrl"] = watchlistUrl
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        ServiceCollection services = new();
        services.AddWatchlistInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Action action = () => _ = provider.GetRequiredService<IOptions<LetterboxdOptions>>().Value;

        action.Should().Throw<OptionsValidationException>();
    }

    private static LetterboxdWatchlistClient CreateClient(HttpStatusCode statusCode, string content)
    {
        HttpClient httpClient = new(new StaticHttpMessageHandler(statusCode, content));
        LetterboxdOptions options = new()
        {
            WatchlistUrl = "https://example.test/example-user/watchlist"
        };

        return new LetterboxdWatchlistClient(httpClient, Options.Create(options), new ImmediateHttpRetryDelay());
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

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }

    private sealed class SequenceHttpMessageHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responses.Dequeue());
        }
    }

    private sealed class ImmediateHttpRetryDelay : IHttpRetryDelay
    {
        public Task DelayAsync(int attempt, HttpResponseMessage? response, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
