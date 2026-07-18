using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TmdbTvMetadataClientTests
{
    private static readonly DateTimeOffset FetchedAt =
        DateTimeOffset.Parse("2026-07-18T10:00:00Z");

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
        string sentinel = "network-url-token-body-sentinel";
        TmdbOptions options = CreateOptions("token");
        TmdbTvMetadataClient client = new(
            new CountingHttpClientFactory(
                () => new ThrowingTmdbHandler(new HttpRequestException(sentinel))),
            Options.Create(options),
            new ImmediateHttpRetryDelay(),
            new FixedTimeProvider(FetchedAt));

        Func<Task> action = () => client.GetTvMetadataAsync(1399, CancellationToken.None);

        TmdbUnavailableException exception = (await action.Should()
            .ThrowAsync<TmdbUnavailableException>())
            .Which;
        exception.ToString().Should().NotContain(sentinel);
    }

    [Fact]
    public async Task GetTvMetadataAsync_WhenTimeoutIsNotCallerCancelled_ThrowsTmdbUnavailableException()
    {
        TmdbOptions options = CreateOptions("token");
        TmdbTvMetadataClient client = new(
            new CountingHttpClientFactory(
                () => new ThrowingTmdbHandler(new TaskCanceledException("timeout"))),
            Options.Create(options),
            new ImmediateHttpRetryDelay(),
            new FixedTimeProvider(FetchedAt));

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

    [Fact]
    public async Task GetTvProvidersAsync_PreservesEveryPlOfferCategoryAndExactRequest()
    {
        StaticTmdbHandler handler = new(new Dictionary<string, string>
        {
            ["/3/tv/1399/watch/providers"] = ProviderResponse("PL")
        });
        TmdbTvMetadataClient client = CreateClient(handler);

        TmdbTvProviderDataDto result = await client.GetTvProvidersAsync(
            1399,
            CancellationToken.None);

        handler.RequestedPathAndQueries.Should().Equal("/3/tv/1399/watch/providers");
        result.Region.Should().Be("PL");
        result.RegionPresence.Should().Be(TmdbProviderRegionPresence.Present);
        result.FetchedAt.Should().Be(FetchedAt);
        result.Link.Should().Be("https://www.themoviedb.org/tv/1399/watch?locale=PL");
        result.Offers.Should().Equal(
            new TmdbTvProviderOfferDto(119, "Provider renamed upstream", "flatrate", "/119.jpg"),
            new TmdbTvProviderOfferDto(1899, "Max", "free", "/1899.jpg"),
            new TmdbTvProviderOfferDto(1773, "SkyShowtime", "ads", "/1773.jpg"),
            new TmdbTvProviderOfferDto(8, "Netflix", "rent", "/8-rent.jpg"),
            new TmdbTvProviderOfferDto(8, "Netflix", "buy", "/8-buy.jpg"));
    }

    [Fact]
    public async Task GetSeasonProvidersAsync_UsesExactSeasonPathAndPreservesProviderData()
    {
        StaticTmdbHandler handler = new(new Dictionary<string, string>
        {
            ["/3/tv/1399/season/1/watch/providers"] = ProviderResponse("PL")
        });
        TmdbTvMetadataClient client = CreateClient(handler);

        TmdbTvProviderDataDto result = await client.GetSeasonProvidersAsync(
            1399,
            1,
            CancellationToken.None);

        handler.RequestedPathAndQueries.Should().Equal(
            "/3/tv/1399/season/1/watch/providers");
        result.RegionPresence.Should().Be(TmdbProviderRegionPresence.Present);
        result.Offers.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetSeasonProvidersAsync_DoesNotAssumeTopLevelIdIsTheShowId()
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/tv/1399/season/1/watch/providers"] = ProviderResponse("PL", 3624)
        });

        TmdbTvProviderDataDto result = await client.GetSeasonProvidersAsync(
            1399,
            1,
            CancellationToken.None);

        result.RegionPresence.Should().Be(TmdbProviderRegionPresence.Present);
        result.Offers.Should().HaveCount(5);
    }

    [Theory]
    [InlineData("""{ "results": { "PL": {} } }""")]
    [InlineData("""{ "id": 0, "results": { "PL": {} } }""")]
    [InlineData("""{ "id": -1, "results": { "PL": {} } }""")]
    public async Task GetSeasonProvidersAsync_WhenSeasonResourceIdIsNotPositive_ThrowsParseException(
        string payload)
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/tv/1399/season/1/watch/providers"] = payload
        });

        Func<Task> action = () => client.GetSeasonProvidersAsync(
            1399,
            1,
            CancellationToken.None);

        await action.Should().ThrowAsync<TmdbParseException>();
    }

    [Fact]
    public async Task GetTvProvidersAsync_WhenResponseShowIdDiffers_ThrowsParseException()
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/tv/1399/watch/providers"] = ProviderResponse("PL", 1400)
        });

        Func<Task> action = () => client.GetTvProvidersAsync(1399, CancellationToken.None);

        await action.Should().ThrowAsync<TmdbParseException>();
    }

    [Fact]
    public async Task GetTvProvidersAsync_WhenPlKeyMissing_ReturnsExplicitMissingRegion()
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/tv/1399/watch/providers"] = ProviderResponse("DE")
        });

        TmdbTvProviderDataDto result = await client.GetTvProvidersAsync(
            1399,
            CancellationToken.None);

        result.Should().Be(new TmdbTvProviderDataDto(
            "PL",
            TmdbProviderRegionPresence.Missing,
            FetchedAt,
            null,
            []));
    }

    [Fact]
    public async Task GetTvProvidersAsync_WhenPlObjectPresentButEmpty_ReturnsPresentEmptyRegion()
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/tv/1399/watch/providers"] =
                """{ "id": 1399, "results": { "PL": {} } }"""
        });

        TmdbTvProviderDataDto result = await client.GetTvProvidersAsync(
            1399,
            CancellationToken.None);

        result.RegionPresence.Should().Be(TmdbProviderRegionPresence.Present);
        result.Link.Should().BeNull();
        result.Offers.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "id": 1399, "results": [] }""")]
    [InlineData("""{ "id": 1399, "results": { "PL": { "flatrate": null } } }""")]
    [InlineData("""{ "id": 1399, "results": { "PL": { "flatrate": [{ "provider_id": 0, "provider_name": "Bad" }] } } }""")]
    [InlineData("""{ "id": 1399, "results": { "PL": { "flatrate": [{ "provider_id": 119, "provider_name": "Max" }, { "provider_id": 119, "provider_name": "Duplicate" }] } } }""")]
    public async Task GetTvProvidersAsync_WhenPayloadInvalid_ThrowsFixedParseException(string payload)
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/tv/1399/watch/providers"] = payload
        });

        Func<Task> action = () => client.GetTvProvidersAsync(1399, CancellationToken.None);

        TmdbParseException exception = (await action.Should()
            .ThrowAsync<TmdbParseException>())
            .Which;
        exception.Message.Should().Be("TMDB TV providers response was invalid.");
        exception.ToString().Should().NotContain(payload);
    }

    [Fact]
    public async Task GetProviderCatalogAsync_ParsesCompleteUniqueCatalogAndExactPath()
    {
        StaticTmdbHandler handler = new(new Dictionary<string, string>
        {
            ["/3/watch/providers/tv"] = """
            {
              "results": [
                { "provider_id": 119, "provider_name": "Max", "logo_path": "/max.jpg", "display_priority": 4 },
                { "provider_id": 1899, "provider_name": "Max Amazon Channel", "logo_path": "/channel.jpg", "display_priority": 12 }
              ]
            }
            """
        });
        TmdbTvMetadataClient client = CreateClient(handler);

        TmdbWatchProviderCatalogDto result = await client.GetProviderCatalogAsync(
            CancellationToken.None);

        handler.RequestedPathAndQueries.Should().Equal("/3/watch/providers/tv");
        result.FetchedAt.Should().Be(FetchedAt);
        result.Providers.Should().Equal(
            new TmdbWatchProviderCatalogEntryDto(119, "Max", "/max.jpg", 4),
            new TmdbWatchProviderCatalogEntryDto(
                1899,
                "Max Amazon Channel",
                "/channel.jpg",
                12));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "results": null }""")]
    [InlineData("""{ "results": {} }""")]
    [InlineData("""{ "results": [{ "provider_id": 0, "provider_name": "Bad", "display_priority": 0 }] }""")]
    [InlineData("""{ "results": [{ "provider_id": 119, "provider_name": "Max", "display_priority": 0 }, { "provider_id": 119, "provider_name": "Other", "display_priority": 1 }] }""")]
    public async Task GetProviderCatalogAsync_WhenCatalogInvalid_ThrowsParseException(string payload)
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/watch/providers/tv"] = payload
        });

        Func<Task> action = () => client.GetProviderCatalogAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TmdbParseException>();
    }

    [Fact]
    public async Task GetProviderRegionsAsync_ParsesCaseSensitiveUniqueRegionCodesAndExactPath()
    {
        StaticTmdbHandler handler = new(new Dictionary<string, string>
        {
            ["/3/watch/providers/regions"] = """
            { "results": [{ "iso_3166_1": "PL" }, { "iso_3166_1": "DE" }] }
            """
        });
        TmdbTvMetadataClient client = CreateClient(handler);

        TmdbWatchProviderRegionsDto result = await client.GetProviderRegionsAsync(
            CancellationToken.None);

        handler.RequestedPathAndQueries.Should().Equal("/3/watch/providers/regions");
        result.FetchedAt.Should().Be(FetchedAt);
        result.RegionCodes.Should().Equal("PL", "DE");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "results": null }""")]
    [InlineData("""{ "results": {} }""")]
    [InlineData("""{ "results": [{ "iso_3166_1": "pl" }] }""")]
    [InlineData("""{ "results": [{ "iso_3166_1": "PL" }, { "iso_3166_1": "PL" }] }""")]
    public async Task GetProviderRegionsAsync_WhenRegionsInvalid_ThrowsParseException(string payload)
    {
        TmdbTvMetadataClient client = CreateClient(new Dictionary<string, string>
        {
            ["/3/watch/providers/regions"] = payload
        });

        Func<Task> action = () => client.GetProviderRegionsAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TmdbParseException>();
    }

    [Fact]
    public async Task ProviderMethods_UseFreshNamedClientForEveryOperation()
    {
        CountingHttpClientFactory factory = new(() => new StaticTmdbHandler(
            new Dictionary<string, string>
            {
                ["/3/tv/1399/watch/providers"] =
                    """{ "id": 1399, "results": { "PL": {} } }"""
            }));
        TmdbTvMetadataClient client = new(
            factory,
            Options.Create(CreateOptions("token")),
            new ImmediateHttpRetryDelay(),
            new FixedTimeProvider(FetchedAt));

        await client.GetTvProvidersAsync(1399, CancellationToken.None);
        await client.GetTvProvidersAsync(1399, CancellationToken.None);

        factory.CreatedNames.Should().Equal(
            TmdbTvMetadataClient.HttpClientName,
            TmdbTvMetadataClient.HttpClientName);
    }

    [Fact]
    public async Task PublicMethods_WhenIdentityOrSeasonIsNonPositive_RejectBeforeHttp()
    {
        CountingHttpClientFactory factory = new(() => new StaticTmdbHandler(
            new Dictionary<string, string>()));
        TmdbTvMetadataClient client = new(
            factory,
            Options.Create(CreateOptions("token")),
            new ImmediateHttpRetryDelay(),
            new FixedTimeProvider(FetchedAt));

        Func<Task> metadata = () => client.GetTvMetadataAsync(0, CancellationToken.None);
        Func<Task> providers = () => client.GetTvProvidersAsync(-1, CancellationToken.None);
        Func<Task> season = () => client.GetSeasonProvidersAsync(
            1399,
            0,
            CancellationToken.None);

        await metadata.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await providers.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await season.Should().ThrowAsync<ArgumentOutOfRangeException>();
        factory.CreatedNames.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTvProvidersAsync_WhenCallerCancels_PropagatesCallerCancellation()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        OperationCanceledException cancellation = new(source.Token);
        TmdbTvMetadataClient client = new(
            new CountingHttpClientFactory(() => new ThrowingTmdbHandler(cancellation)),
            Options.Create(CreateOptions("token")),
            new ImmediateHttpRetryDelay(),
            new FixedTimeProvider(FetchedAt));

        Func<Task> action = () => client.GetTvProvidersAsync(1399, source.Token);

        OperationCanceledException thrown = (await action.Should()
            .ThrowAsync<OperationCanceledException>())
            .Which;
        thrown.CancellationToken.Should().Be(source.Token);
    }

    private static TmdbTvMetadataClient CreateClient(
        IReadOnlyDictionary<string, string> responses,
        string accessToken = "token")
    {
        TmdbOptions options = CreateOptions(accessToken);

        return new TmdbTvMetadataClient(
            new CountingHttpClientFactory(() => new StaticTmdbHandler(responses)),
            Options.Create(options),
            new ImmediateHttpRetryDelay(),
            new FixedTimeProvider(FetchedAt));
    }

    private static TmdbTvMetadataClient CreateClient(StaticTmdbHandler handler)
    {
        TmdbOptions options = CreateOptions("token");

        return new TmdbTvMetadataClient(
            new CountingHttpClientFactory(() => handler),
            Options.Create(options),
            new ImmediateHttpRetryDelay(),
            new FixedTimeProvider(FetchedAt));
    }

    private static string ProviderResponse(string region, int responseId = 1399)
    {
        return $$"""
        {
          "id": {{responseId}},
          "results": {
            "{{region}}": {
              "link": "https://www.themoviedb.org/tv/1399/watch?locale={{region}}",
              "flatrate": [{ "provider_id": 119, "provider_name": "Provider renamed upstream", "logo_path": "/119.jpg" }],
              "free": [{ "provider_id": 1899, "provider_name": "Max", "logo_path": "/1899.jpg" }],
              "ads": [{ "provider_id": 1773, "provider_name": "SkyShowtime", "logo_path": "/1773.jpg" }],
              "rent": [{ "provider_id": 8, "provider_name": "Netflix", "logo_path": "/8-rent.jpg" }],
              "buy": [{ "provider_id": 8, "provider_name": "Netflix", "logo_path": "/8-buy.jpg" }]
            }
          }
        }
        """;
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

    private sealed class ImmediateHttpRetryDelay : IHttpRetryDelay
    {
        public Task DelayAsync(int attempt, HttpResponseMessage? response, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CountingHttpClientFactory(Func<HttpMessageHandler> handlerFactory)
        : IHttpClientFactory
    {
        public List<string> CreatedNames { get; } = [];

        public HttpClient CreateClient(string name)
        {
            CreatedNames.Add(name);
            return new HttpClient(handlerFactory(), disposeHandler: true)
            {
                BaseAddress = new Uri("https://api.themoviedb.org/3")
            };
        }
    }
}
