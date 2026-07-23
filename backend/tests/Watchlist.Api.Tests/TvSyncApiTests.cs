using System.Net;
using FluentAssertions;

namespace Watchlist.Api.Tests;

public sealed class TvSyncApiTests
{
    [Fact]
    public async Task SyncTv_WithoutConfiguredKey_UsesLocalCompatibility()
    {
        using SeededApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/tv", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task SyncTv_WithConfiguredKey_RejectsMissingOrWrongKey(string? suppliedKey)
    {
        using SeededApiFactory factory = new(syncApiKey: "correct");
        using HttpClient client = factory.CreateClient();
        if (suppliedKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Watchlist-Sync-Key", suppliedKey);
        }

        HttpResponseMessage response = await client.PostAsync("/api/sync/tv", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SyncTv_WithCorrectKey_ReturnsPublishedGeneration()
    {
        using SeededApiFactory factory = new(syncApiKey: "correct");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Watchlist-Sync-Key", "correct");

        HttpResponseMessage response = await client.PostAsync("/api/sync/tv", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("seeded-tv-generation");
    }

    [Theory]
    [InlineData("not_connected", HttpStatusCode.Conflict, "trakt_not_connected")]
    [InlineData("snapshot", HttpStatusCode.BadGateway, "tv_snapshot_rejected")]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable, "trakt_unavailable")]
    [InlineData("rate_limited", HttpStatusCode.ServiceUnavailable, "trakt_rate_limited")]
    public async Task SyncTv_MapsTypedFailuresWithoutLeakingDetails(
        string failure,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Exception exception = failure switch
        {
            "not_connected" => new Watchlist.Application.TraktNotConnectedException(),
            "snapshot" => new Watchlist.Application.TvSourceSnapshotRejectedException("secret-source-body"),
            "rate_limited" => new Watchlist.Application.TraktRateLimitedException(TimeSpan.FromSeconds(42)),
            _ => new Watchlist.Application.TraktUnavailableException()
        };
        using SeededApiFactory factory = new(tvSyncException: exception);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/tv", null);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(expectedStatus);
        body.Should().Contain(expectedCode);
        body.Should().NotContain("secret-source-body");
    }

    [Fact]
    public async Task SyncTv_WhenSnapshotReasonIsUnexpected_LogsOnlyAnUnknownReason()
    {
        List<string> logs = [];
        using SeededApiFactory factory = new(
            tvSyncException: new Watchlist.Application.TvSourceSnapshotRejectedException(
                "secret-source-body"),
            capturedLogs: logs);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/tv", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        logs.Should().Contain(entry => entry.Contains(
            "TV source snapshot rejected: unknown",
            StringComparison.Ordinal));
        logs.Should().NotContain(entry => entry.Contains("secret-source-body", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SyncTv_WhenTraktRateLimited_ReturnsRetryAfterWithoutLeakingDetails()
    {
        using SeededApiFactory factory = new(
            tvSyncException: new Watchlist.Application.TraktRateLimitedException(
                TimeSpan.FromSeconds(42)));
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/tv", null);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(42));
        body.Should().Contain("trakt_rate_limited");
    }

    [Fact]
    public async Task SyncTv_WhenUnexpectedFailureOccurs_LogsOnlyItsExceptionType()
    {
        List<string> logs = [];
        using SeededApiFactory factory = new(
            tvSyncException: new InvalidOperationException("secret-source-body"),
            capturedLogs: logs);
        using HttpClient client = factory.CreateClient();

        await client.PostAsync("/api/sync/tv", null);

        logs.Should().Contain(entry => entry.Contains(
            "Unhandled backend exception type: InvalidOperationException",
            StringComparison.Ordinal));
        logs.Should().NotContain(entry => entry.Contains("secret-source-body", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SyncTv_WhenServiceFails_LogsOnlyTheServiceExceptionType()
    {
        List<string> logs = [];
        using SeededApiFactory factory = new(
            tvSyncException: new InvalidOperationException("secret-source-body"),
            capturedLogs: logs);
        using HttpClient client = factory.CreateClient();

        await client.PostAsync("/api/sync/tv", null);

        logs.Should().Contain(entry => entry.Contains(
            "TV sync operation failed: InvalidOperationException",
            StringComparison.Ordinal));
        logs.Should().NotContain(entry => entry.Contains("secret-source-body", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SonarrCompatibilityExport_IsEmptyAndCarriesCompatibilityHeader()
    {
        using SeededApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/export/sonarr/tv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Watchlist-Contract").Should().ContainSingle()
            .Which.Should().Be("compatibility-only");
        (await response.Content.ReadAsStringAsync()).Should().Be("[]");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task LegacyTmdbTvSync_WithMissingOrWrongKey_ReturnsUnauthorized(string? suppliedKey)
    {
        using SeededApiFactory factory = new(syncApiKey: "correct");
        using HttpClient client = factory.CreateClient();
        if (suppliedKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Watchlist-Sync-Key", suppliedKey);
        }

        HttpResponseMessage response = await client.PostAsync("/api/sync/tmdb/tv", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LegacyTmdbTvSync_WithCorrectKey_ReturnsGoneWithoutInvokingTheLegacyService()
    {
        int invoked = 0;
        using SeededApiFactory factory = new(
            syncApiKey: "correct",
            tmdbTvSyncInvoked: () => invoked++);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Watchlist-Sync-Key", "correct");

        HttpResponseMessage response = await client.PostAsync("/api/sync/tmdb/tv", null);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        body.Should().Contain("legacy_tv_sync_disabled");
        invoked.Should().Be(0);
    }

    [Fact]
    public async Task TvWorkerExport_ReturnsOnlyPublishedReadOnlySnapshot()
    {
        using SeededApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/export/tv/sync-state");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"schemaVersion\":\"1\"");
        body.Should().Contain("\"generationId\":\"seeded-tv-generation\"");
        body.Should().Contain("\"mutationCapable\":false");
        body.Should().Contain("phase_1_read_only");
        body.Should().NotContain("seeded-access-token");
    }

    [Fact]
    public async Task SyncStatus_PreservesMovieStatusAndAddsTvState()
    {
        using SeededApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/sync/status");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"status\":\"seeded\"");
        body.Should().Contain("\"tv\":");
        body.Should().Contain("\"connectionStatus\":\"connected\"");
        body.Should().Contain("\"mutationCapable\":false");
    }
}
