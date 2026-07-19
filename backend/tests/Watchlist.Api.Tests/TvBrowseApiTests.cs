using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Watchlist.Api.Tests;

public sealed class TvBrowseApiTests
{
    [Theory]
    [InlineData("/api/watchlist?collection=tv", "active")]
    [InlineData("/api/watchlist?collection=tv&state=active", "active")]
    [InlineData("/api/watchlist?collection=tv&state=caught_up", "caught_up")]
    [InlineData("/api/watchlist?collection=tv&state=retired", "retired_terminal")]
    public async Task BrowseTv_ReturnsPublishedTvState(string path, string expectedLifecycle)
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement item = body.RootElement.EnumerateArray().Single();
        item.GetProperty("source").GetString().Should().Be("trakt");
        item.GetProperty("availabilityStatus").GetString().Should().Be("unknown_match");
        item.GetProperty("tv").GetProperty("lifecycleState").GetString().Should().Be(expectedLifecycle);
        if (expectedLifecycle == "active")
        {
            item.GetProperty("id").GetString().Should().Be("tv-trakt-12345");
        }
        item.GetProperty("posterUrl").GetString().Should().Be("/api/images/tmdb/w500/poster.png");
        item.GetProperty("tv").GetProperty("availability").GetProperty("offers")[0]
            .GetProperty("logoUrl").GetString().Should().Be("/api/images/tmdb/w500/logo%2Fprivate.png");
    }

    [Fact]
    public async Task BrowseAll_ReturnsMoviesAndActiveTv_AndMovieOmitsTvObject()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist?collection=all");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.EnumerateArray().Should().Contain(item =>
            item.GetProperty("id").GetString() == "tv-trakt-12345");
        JsonElement movie = body.RootElement.EnumerateArray().First(item =>
            item.GetProperty("mediaType").GetString() == "movie");
        movie.TryGetProperty("tv", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/watchlist?collection=movie&state=active")]
    [InlineData("/api/watchlist?collection=all&state=active")]
    [InlineData("/api/watchlist?collection=tv&state=source_removed")]
    public async Task Browse_WhenTvStateIsInvalid_ReturnsBadRequest(string path)
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid TV state.");
    }

    [Theory]
    [InlineData("/api/watchlist/tv-trakt-12345", HttpStatusCode.OK)]
    [InlineData("/api/watchlist/tv-trakt-missing", HttpStatusCode.NotFound)]
    [InlineData("/api/watchlist/legacy-tv", HttpStatusCode.NotFound)]
    public async Task GetTvDetail_OnlyReadsPublishedTvGeneration(string path, HttpStatusCode expectedStatus)
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        response.StatusCode.Should().Be(expectedStatus);
        if (expectedStatus == HttpStatusCode.OK)
        {
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            body.RootElement.GetProperty("tv").GetProperty("destinations")
                .GetProperty("sonarrState").GetString().Should().Be("unknown");
        }
    }
}
