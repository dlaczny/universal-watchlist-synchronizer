using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TmdbTvMetadataClientTests
{
    [Fact]
    public async Task GetTvMetadataAsync_ParsesDetailsAndExternalIds()
    {
        Dictionary<string, string> responses = new()
        {
            ["/3/tv/1399?language=en-US"] = """
            {
              "id": 1399,
              "name": "Game of Thrones",
              "original_name": "Game of Thrones",
              "overview": "Nine noble families fight for control.",
              "first_air_date": "2011-04-17",
              "status": "Ended",
              "poster_path": "/poster.jpg",
              "backdrop_path": "/backdrop.jpg",
              "original_language": "en",
              "vote_average": 8.5,
              "vote_count": 25000,
              "genres": [{ "id": 18, "name": "Drama" }]
            }
            """,
            ["/3/tv/1399/external_ids"] = """
            {
              "id": 1399,
              "imdb_id": "tt0944947",
              "tvdb_id": 121361
            }
            """
        };
        TmdbTvMetadataClient client = CreateClient(responses);

        TmdbTvMetadataDto result = await client.GetTvMetadataAsync(1399, CancellationToken.None);

        result.Should().BeEquivalentTo(new TmdbTvMetadataDto(
            1399,
            "Game of Thrones",
            "Game of Thrones",
            "Nine noble families fight for control.",
            "2011-04-17",
            "Ended",
            "/poster.jpg",
            "/backdrop.jpg",
            "https://image.tmdb.org/t/p/w500/poster.jpg",
            "https://image.tmdb.org/t/p/w1280/backdrop.jpg",
            ["Drama"],
            "en",
            8.5,
            25000,
            new TmdbTvExternalIdsDto("tt0944947", 121361)));
    }

    [Fact]
    public async Task GetTvMetadataAsync_WhenNotFound_ThrowsTmdbTvNotFoundException()
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/tv/999999?language=en-US"] = "__404__"
        });

        Func<Task> action = () => client.GetTvMetadataAsync(999999, CancellationToken.None);

        await action.Should().ThrowAsync<TmdbTvNotFoundException>();
    }

    [Fact]
    public async Task GetTvMetadataAsync_WhenAccessTokenMissing_ThrowsTmdbUnavailableException()
    {
        TmdbTvMetadataClient client = CreateClient(
            new Dictionary<string, string>(),
            accessToken: "");

        Func<Task> action = () => client.GetTvMetadataAsync(1399, CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Theory]
    [InlineData("__401__")]
    [InlineData("__403__")]
    [InlineData("__429__")]
    [InlineData("__500__")]
    public async Task GetTvMetadataAsync_WhenTmdbDependencyFails_ThrowsTmdbUnavailableException(string responseMarker)
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/tv/1399?language=en-US"] = responseMarker
        });

        Func<Task> action = () => client.GetTvMetadataAsync(1399, CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetTvMetadataAsync_WhenJsonMalformed_ThrowsTmdbParseException()
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/tv/1399?language=en-US"] = "["
        });

        Func<Task> action = () => client.GetTvMetadataAsync(1399, CancellationToken.None);

        await action.Should().ThrowAsync<TmdbParseException>();
    }

    [Fact]
    public async Task GetTvMetadataAsync_WhenNetworkFails_ThrowsTmdbUnavailableException()
    {
        HttpClient httpClient = new(new ThrowingTmdbHandler(new HttpRequestException("network down")))
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions("token");
        TmdbTvMetadataClient client = new(httpClient, Options.Create(options));

        Func<Task> action = () => client.GetTvMetadataAsync(1399, CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetTvMetadataAsync_WhenTimeoutIsNotCallerCancelled_ThrowsTmdbUnavailableException()
    {
        HttpClient httpClient = new(new ThrowingTmdbHandler(new TaskCanceledException("timeout")))
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions("token");
        TmdbTvMetadataClient client = new(httpClient, Options.Create(options));

        Func<Task> action = () => client.GetTvMetadataAsync(1399, CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetTvMetadataAsync_SendsRequestsUnderConfiguredTmdbApiPath()
    {
        Dictionary<string, string> responses = new()
        {
            ["/3/tv/1399?language=en-US"] = """
            {
              "id": 1399,
              "name": "GOT",
              "original_name": "GOT",
              "genres": [{ "id": 18, "name": "Drama" }]
            }
            """,
            ["/3/tv/1399/external_ids"] = """{ "id": 1399 }"""
        };
        StaticTmdbHandler handler = new(responses);
        TmdbTvMetadataClient client = CreateClient(handler);

        await client.GetTvMetadataAsync(1399, CancellationToken.None);

        handler.RequestedPathAndQueries.Should().Equal(
            "/3/tv/1399?language=en-US",
            "/3/tv/1399/external_ids");
    }

    [Theory]
    [InlineData("""{ "id": 0, "name": "GOT", "original_name": "GOT", "genres": [] }""")]
    [InlineData("""{ "id": 1399, "name": "", "original_name": "GOT", "genres": [] }""")]
    [InlineData("""{ "id": 1399, "name": "GOT", "original_name": "", "genres": [] }""")]
    [InlineData("""{ "id": 1399, "name": "GOT", "original_name": "GOT" }""")]
    public async Task GetTvMetadataAsync_WhenDetailsInvalid_ThrowsTmdbParseException(string detailsResponse)
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/tv/1399?language=en-US"] = detailsResponse
        });

        Func<Task> action = () => client.GetTvMetadataAsync(1399, CancellationToken.None);

        await action.Should().ThrowAsync<TmdbParseException>();
    }

    private static TmdbTvMetadataClient CreateClient(
        IReadOnlyDictionary<string, string> responses,
        string accessToken = "token")
    {
        HttpClient httpClient = new(new StaticTmdbHandler(responses))
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions(accessToken);

        return new TmdbTvMetadataClient(httpClient, Options.Create(options));
    }

    private static TmdbTvMetadataClient CreateClient(StaticTmdbHandler handler)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions("token");

        return new TmdbTvMetadataClient(httpClient, Options.Create(options));
    }

    private static TmdbOptions CreateOptions(string accessToken)
    {
        return new TmdbOptions
        {
            AccessToken = accessToken,
            BaseUrl = "https://api.themoviedb.org/3",
            ImageBaseUrl = "https://image.tmdb.org/t/p",
            AccountId = 123,
            SessionId = "session",
            Language = "en-US"
        };
    }
}
