using FluentAssertions;
using Microsoft.Extensions.Options;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TmdbMovieClientTests
{
    [Fact]
    public async Task GetMovieMetadataAsync_WhenDetailsAndProvidersExist_ParsesMetadata()
    {
        Dictionary<string, string> responses = new()
        {
            ["/3/movie/1297842"] = """
            {
              "id": 1297842,
              "imdb_id": "tt27613895",
              "title": "GOAT",
              "original_title": "GOAT",
              "overview": "A promising athlete story.",
              "release_date": "2026-02-13",
              "poster_path": "/poster.jpg",
              "backdrop_path": "/backdrop.jpg",
              "runtime": 96,
              "original_language": "en",
              "vote_average": 7.4,
              "vote_count": 812,
              "genres": [{ "id": 18, "name": "Drama" }]
            }
            """,
            ["/3/movie/1297842/watch/providers"] = """
            {
              "results": {
                "PL": {
                  "flatrate": [
                    { "provider_id": 119, "provider_name": "Amazon Prime Video", "logo_path": "/prime.jpg", "display_priority": 1 }
                  ],
                  "rent": [
                    { "provider_id": 10, "provider_name": "Amazon Video", "logo_path": "/amazon.jpg", "display_priority": 2 }
                  ]
                },
                "US": {
                  "buy": [
                    { "provider_id": 2, "provider_name": "Apple TV", "logo_path": "/apple.jpg", "display_priority": 3 }
                  ]
                }
              }
            }
            """
        };
        TmdbMovieClient client = CreateClient(responses);

        TmdbMovieMetadataDto metadata = await client.GetMovieMetadataAsync(
            1297842,
            "tt27613895",
            CancellationToken.None);

        metadata.Details.Should().BeEquivalentTo(new TmdbMovieDetailsDto(
            1297842,
            "tt27613895",
            "GOAT",
            "GOAT",
            "A promising athlete story.",
            "2026-02-13",
            "/poster.jpg",
            "/backdrop.jpg",
            "https://image.tmdb.org/t/p/w500/poster.jpg",
            "https://image.tmdb.org/t/p/w1280/backdrop.jpg",
            ["Drama"],
            96,
            "en",
            7.4,
            812));
        metadata.Providers.Regions.Should().ContainKey("PL");
        metadata.Providers.Regions["PL"].Flatrate.Should().ContainSingle(provider =>
            provider.ProviderName == "Amazon Prime Video"
            && provider.ProviderId == 119);
        metadata.Providers.Regions["US"].Buy.Should().ContainSingle(provider =>
            provider.ProviderName == "Apple TV");
    }

    [Fact]
    public async Task GetMovieMetadataAsync_WhenDirectMovieNotFound_UsesImdbFallback()
    {
        Dictionary<string, string> responses = new()
        {
            ["/3/movie/1"] = "__404__",
            ["/3/find/tt27613895?external_source=imdb_id"] = """
            {
              "movie_results": [
                { "id": 1297842, "title": "GOAT" }
              ]
            }
            """,
            ["/3/movie/1297842"] = """
            {
              "id": 1297842,
              "imdb_id": "tt27613895",
              "title": "GOAT",
              "original_title": "GOAT",
              "overview": "A promising athlete story.",
              "release_date": "2026-02-13",
              "poster_path": "/poster.jpg",
              "backdrop_path": "/backdrop.jpg",
              "genres": []
            }
            """,
            ["/3/movie/1297842/watch/providers"] = """{ "results": {} }"""
        };
        TmdbMovieClient client = CreateClient(responses);

        TmdbMovieMetadataDto metadata = await client.GetMovieMetadataAsync(
            1,
            "tt27613895",
            CancellationToken.None);

        metadata.Details.TmdbId.Should().Be(1297842);
    }

    [Fact]
    public async Task GetMovieMetadataAsync_SendsRequestsUnderConfiguredTmdbApiPath()
    {
        Dictionary<string, string> responses = new()
        {
            ["/3/movie/1"] = "__404__",
            ["/3/find/tt27613895?external_source=imdb_id"] = """
            {
              "movie_results": [
                { "id": 1297842, "title": "GOAT" }
              ]
            }
            """,
            ["/3/movie/1297842"] = """
            {
              "id": 1297842,
              "imdb_id": "tt27613895",
              "title": "GOAT",
              "original_title": "GOAT",
              "overview": "A promising athlete story.",
              "release_date": "2026-02-13",
              "poster_path": "/poster.jpg",
              "backdrop_path": "/backdrop.jpg",
              "genres": []
            }
            """,
            ["/3/movie/1297842/watch/providers"] = """{ "results": {} }"""
        };
        StaticTmdbHandler handler = new(responses);
        TmdbMovieClient client = CreateClient(handler);

        await client.GetMovieMetadataAsync(1, "tt27613895", CancellationToken.None);

        handler.RequestedPathAndQueries.Should().Equal(
            "/3/movie/1",
            "/3/find/tt27613895?external_source=imdb_id",
            "/3/movie/1297842",
            "/3/movie/1297842/watch/providers");
    }


    [Fact]
    public async Task GetMovieMetadataAsync_WhenMovieMissingAndNoFallback_ThrowsTmdbMovieNotFoundException()
    {
        TmdbMovieClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/movie/1"] = "__404__"
        });

        Func<Task> action = () => client.GetMovieMetadataAsync(1, null, CancellationToken.None);

        await action.Should().ThrowAsync<TmdbMovieNotFoundException>();
    }

    [Fact]
    public async Task GetMovieMetadataAsync_WhenFallbackFindsNoMovie_ThrowsTmdbMovieNotFoundException()
    {
        TmdbMovieClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/movie/1"] = "__404__",
            ["/3/find/tt27613895?external_source=imdb_id"] = """{ "movie_results": [] }"""
        });

        Func<Task> action = () => client.GetMovieMetadataAsync(1, "tt27613895", CancellationToken.None);

        await action.Should().ThrowAsync<TmdbMovieNotFoundException>();
    }

    [Theory]
    [InlineData("""{ "movie_results": null }""")]
    [InlineData("""{}""")]
    public async Task GetMovieMetadataAsync_WhenFallbackMovieResultsMissing_ThrowsTmdbMovieNotFoundException(
        string findResponse)
    {
        TmdbMovieClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/movie/1"] = "__404__",
            ["/3/find/tt27613895?external_source=imdb_id"] = findResponse
        });

        Func<Task> action = () => client.GetMovieMetadataAsync(1, "tt27613895", CancellationToken.None);

        await action.Should().ThrowAsync<TmdbMovieNotFoundException>();
    }

    [Theory]
    [InlineData("__401__")]
    [InlineData("__403__")]
    [InlineData("__429__")]
    [InlineData("__500__")]
    public async Task GetMovieMetadataAsync_WhenTmdbDependencyFails_ThrowsTmdbUnavailableException(string responseMarker)
    {
        TmdbMovieClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/movie/1297842"] = responseMarker
        });

        Func<Task> action = () => client.GetMovieMetadataAsync(
            1297842,
            "tt27613895",
            CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetMovieMetadataAsync_WhenAccessTokenMissing_ThrowsTmdbUnavailableException()
    {
        TmdbMovieClient client = CreateClient(
            new Dictionary<string, string>(),
            accessToken: "");

        Func<Task> action = () => client.GetMovieMetadataAsync(
            1297842,
            "tt27613895",
            CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetMovieMetadataAsync_WhenJsonMalformed_ThrowsTmdbParseException()
    {
        TmdbMovieClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/movie/1297842"] = "["
        });

        Func<Task> action = () => client.GetMovieMetadataAsync(
            1297842,
            "tt27613895",
            CancellationToken.None);

        await action.Should().ThrowAsync<TmdbParseException>();
    }

    [Theory]
    [InlineData("""{ "id": 1297842, "title": "GOAT", "original_title": "GOAT", "genres": null }""")]
    [InlineData("""{ "id": 1297842, "title": "GOAT", "original_title": "GOAT" }""")]
    public async Task GetMovieMetadataAsync_WhenDetailsGenresMissing_ThrowsTmdbParseException(string detailsResponse)
    {
        TmdbMovieClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/movie/1297842"] = detailsResponse
        });

        Func<Task> action = () => client.GetMovieMetadataAsync(
            1297842,
            "tt27613895",
            CancellationToken.None);

        await action.Should().ThrowAsync<TmdbParseException>();
    }

    [Fact]
    public async Task GetMovieMetadataAsync_WhenNetworkFails_ThrowsTmdbUnavailableException()
    {
        HttpClient httpClient = new(new ThrowingTmdbHandler(new HttpRequestException("network down")))
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions("token");
        TmdbMovieClient client = new(httpClient, Options.Create(options));

        Func<Task> action = () => client.GetMovieMetadataAsync(
            1297842,
            "tt27613895",
            CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    [Fact]
    public async Task GetMovieMetadataAsync_WhenTimeoutIsNotCallerCancelled_ThrowsTmdbUnavailableException()
    {
        HttpClient httpClient = new(new ThrowingTmdbHandler(new TaskCanceledException("timeout")))
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions("token");
        TmdbMovieClient client = new(httpClient, Options.Create(options));

        Func<Task> action = () => client.GetMovieMetadataAsync(
            1297842,
            "tt27613895",
            CancellationToken.None);

        await action.Should().ThrowAsync<TmdbUnavailableException>();
    }

    private static TmdbMovieClient CreateClient(
        IReadOnlyDictionary<string, string> responses,
        string accessToken = "token")
    {
        HttpClient httpClient = new(new StaticTmdbHandler(responses))
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions(accessToken);

        return new TmdbMovieClient(httpClient, Options.Create(options));
    }

    private static TmdbMovieClient CreateClient(StaticTmdbHandler handler)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.themoviedb.org/3")
        };
        TmdbOptions options = CreateOptions("token");

        return new TmdbMovieClient(httpClient, Options.Create(options));
    }

    private static TmdbOptions CreateOptions(string accessToken)
    {
        return new TmdbOptions
        {
            AccessToken = accessToken,
            BaseUrl = "https://api.themoviedb.org/3",
            ImageBaseUrl = "https://image.tmdb.org/t/p"
        };
    }

}
