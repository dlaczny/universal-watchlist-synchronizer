using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Watchlist.Api.Tests;

public sealed class TvSyncApiTests
{
    [Fact]
    public async Task PostLegacyTmdbTvSync_WithCorrectKey_ReturnsGoneWithoutInvokingService()
    {
        int invocationCount = 0;
        using SeededApiFactory factory = new(
            syncApiKey: "test-sync-key",
            tmdbTvSyncInvoked: () => invocationCount++);
        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/sync/tmdb/tv");
        request.Headers.Add("X-Watchlist-Sync-Key", "test-sync-key");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString()
            .Should().Be("legacy_tv_sync_disabled");
        document.RootElement.GetProperty("error").GetString()
            .Should().Be("The legacy TMDB TV sync is disabled.");
        invocationCount.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task PostLegacyTmdbTvSync_WithInvalidKey_ReturnsUnauthorizedWithoutInvokingService(
        string? suppliedKey)
    {
        int invocationCount = 0;
        using SeededApiFactory factory = new(
            syncApiKey: "test-sync-key",
            tmdbTvSyncInvoked: () => invocationCount++);
        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/sync/tmdb/tv");
        if (suppliedKey is not null)
        {
            request.Headers.Add("X-Watchlist-Sync-Key", suppliedKey);
        }

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        invocationCount.Should().Be(0);
    }

    [Fact]
    public async Task PostSyncAll_ReportsDisabledCompatibilityStageWithoutInvokingLegacyTvService()
    {
        int invocationCount = 0;
        using SeededApiFactory factory = new(
            tmdbTvSyncInvoked: () => invocationCount++);
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetString().Should().Be("partial");
        document.RootElement.GetProperty("tmdbTv").GetProperty("status").GetString()
            .Should().Be("disabled");
        document.RootElement.GetProperty("tmdbTv").GetProperty("itemsFetched").GetInt32()
            .Should().Be(0);
        invocationCount.Should().Be(0);
    }
}
