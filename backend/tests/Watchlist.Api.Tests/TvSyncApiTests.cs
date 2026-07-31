using System.Net;
using System.Text.Json;
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
    [InlineData("not_connected", HttpStatusCode.Conflict, "trakt_not_connected",
        "Trakt is not connected.")]
    [InlineData("snapshot", HttpStatusCode.BadGateway, "tv_snapshot_rejected",
        "The TV source snapshot was rejected.")]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable, "trakt_unavailable",
        "Trakt is temporarily unavailable.")]
    [InlineData("rate_limited", HttpStatusCode.ServiceUnavailable, "trakt_rate_limited",
        "Trakt temporarily rate limited the sync.")]
    [InlineData("retention", HttpStatusCode.ServiceUnavailable,
        "tv_generation_retention_failed",
        "TV generation retention is temporarily unavailable.")]
    public async Task SyncTv_MapsTypedFailuresWithoutLeakingDetails(
        string failure,
        HttpStatusCode expectedStatus,
        string expectedCode,
        string expectedDetail)
    {
        const string secret = "secret-retention-inner-message";
        Exception exception = failure switch
        {
            "not_connected" => new Watchlist.Application.TraktNotConnectedException(),
            "snapshot" => new Watchlist.Application.TvSourceSnapshotRejectedException("secret-source-body"),
            "rate_limited" => new Watchlist.Application.TraktRateLimitedException(TimeSpan.FromSeconds(42)),
            "retention" => new Watchlist.Application.TvGenerationRetentionException(
                new InvalidOperationException(secret)),
            _ => new Watchlist.Application.TraktUnavailableException()
        };
        using SeededApiFactory factory = new(tvSyncException: exception);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/tv", null);
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        document.RootElement.GetProperty("code").GetString().Should().Be(expectedCode);
        document.RootElement.GetProperty("error").GetString().Should().Be(expectedDetail);
        body.Should().NotContain("secret-source-body");
        body.Should().NotContain(secret);
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
        body.Should().Contain("\"schemaVersion\":\"2\"");
        body.Should().Contain("\"generationId\":\"seeded-tv-generation\"");
        body.Should().Contain("\"mutationCapable\":false");
        body.Should().Contain("\"destinationSync\":{\"capable\":true");
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement season = document.RootElement.GetProperty("shows")[0].GetProperty("seasons")[0];
        season.GetProperty("polandAvailability").GetProperty("state").GetString().Should().Be("available");
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
