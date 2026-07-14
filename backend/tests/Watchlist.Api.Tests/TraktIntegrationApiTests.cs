using System.Net;
using System.Text.Json;
using FluentAssertions;
using Watchlist.Application;

namespace Watchlist.Api.Tests;

public sealed class TraktIntegrationApiTests
{
    private const string SyncKey = "trakt-api-test-key";

    [Theory]
    [InlineData("POST", "/api/integrations/trakt/device/start", null)]
    [InlineData("POST", "/api/integrations/trakt/device/start", "wrong-key")]
    [InlineData("GET", "/api/integrations/trakt/status", null)]
    [InlineData("GET", "/api/integrations/trakt/status", "wrong-key")]
    [InlineData("DELETE", "/api/integrations/trakt/connection", null)]
    [InlineData("DELETE", "/api/integrations/trakt/connection", "wrong-key")]
    public async Task TraktIntegrationEndpoints_WhenSyncKeyMissingOrWrong_ReturnUnauthorized(
        string method,
        string path,
        string? suppliedKey)
    {
        using SeededApiFactory factory = new(syncApiKey: SyncKey);
        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(new HttpMethod(method), path);
        if (suppliedKey is not null)
        {
            request.Headers.Add("X-Watchlist-Sync-Key", suppliedKey);
        }

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StartDevice_WhenAuthorized_ReturnsDocumentedOneTimeUserCodeDto()
    {
        using SeededApiFactory factory = new(syncApiKey: SyncKey);
        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = AuthorizedRequest(
            HttpMethod.Post,
            "/api/integrations/trakt/device/start");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "userCode",
            "verificationUrl",
            "expiresAt",
            "pollIntervalSeconds");
        root.GetProperty("userCode").GetString().Should().Be("ABCD1234");
        root.GetProperty("verificationUrl").GetString().Should().Be("https://trakt.tv/activate");
        root.GetProperty("expiresAt").GetDateTimeOffset().Should().Be(
            DateTimeOffset.Parse("2026-07-14T10:10:00Z"));
        root.GetProperty("pollIntervalSeconds").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task GetStatus_WhenAuthorized_ContainsOnlyPublicRedactedConnectionFields()
    {
        using SeededApiFactory factory = new(syncApiKey: SyncKey);
        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = AuthorizedRequest(
            HttpMethod.Get,
            "/api/integrations/trakt/status");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("connected");
        IReadOnlyList<string> propertyNames = CollectPropertyNames(document.RootElement);
        propertyNames.Should().NotContain(name => SensitivePropertyNames.Contains(name));
        propertyNames.Should().NotContain(name => name.Contains("protected", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain("userCode");
    }

    [Fact]
    public async Task Disconnect_WhenAuthorized_ReturnsDisconnectedWithoutSensitiveFields()
    {
        using SeededApiFactory factory = new(syncApiKey: SyncKey);
        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = AuthorizedRequest(
            HttpMethod.Delete,
            "/api/integrations/trakt/connection");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("disconnected");
        CollectPropertyNames(document.RootElement).Should().NotContain("userCode");
    }

    [Fact]
    public async Task StartDevice_WhenLiveFlowAlreadyPending_ReturnsFixedRedactedConflict()
    {
        using SeededApiFactory factory = new(
            syncApiKey: SyncKey,
            traktStartException: new TraktConnectionPendingException());
        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = AuthorizedRequest(
            HttpMethod.Post,
            "/api/integrations/trakt/device/start");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using JsonDocument document = await ReadJsonAsync(response);
        document.RootElement.GetProperty("code").GetString().Should().Be("trakt_connection_pending");
        document.RootElement.GetProperty("error").GetString().Should().Be(
            "A Trakt device authorization is already pending.");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("device-code");
    }

    [Fact]
    public async Task StartDevice_WhenTraktUnavailable_ReturnsFixedRedactedServiceUnavailable()
    {
        using SeededApiFactory factory = new(
            syncApiKey: SyncKey,
            traktStartException: new TraktUnavailableException());
        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = AuthorizedRequest(
            HttpMethod.Post,
            "/api/integrations/trakt/device/start");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using JsonDocument document = await ReadJsonAsync(response);
        document.RootElement.GetProperty("code").GetString().Should().Be("trakt_unavailable");
        document.RootElement.GetProperty("error").GetString().Should().Be(
            "Trakt is temporarily unavailable.");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("client-secret");
    }

    [Fact]
    public async Task StartDevice_WhenPersistenceUnavailable_ReturnsFixedRedactedServiceUnavailable()
    {
        using SeededApiFactory factory = new(
            syncApiKey: SyncKey,
            traktStartException: new TraktPersistenceUnavailableException());
        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = AuthorizedRequest(
            HttpMethod.Post,
            "/api/integrations/trakt/device/start");

        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using JsonDocument document = await ReadJsonAsync(response);
        document.RootElement.GetProperty("code").GetString().Should().Be(
            "trakt_persistence_unavailable");
        document.RootElement.GetProperty("error").GetString().Should().Be(
            "Trakt connection persistence is temporarily unavailable.");
        string body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("device-code");
        body.Should().NotContain("user-code");
    }

    private static readonly HashSet<string> SensitivePropertyNames = new(
        [
            "deviceCode",
            "userCode",
            "accessToken",
            "refreshToken",
            "clientSecret",
            "protectedDeviceCode",
            "protectedAccessToken",
            "protectedRefreshToken"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Add("X-Watchlist-Sync-Key", SyncKey);
        return request;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Stream stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static IReadOnlyList<string> CollectPropertyNames(JsonElement element)
    {
        List<string> names = [];
        CollectPropertyNames(element, names);
        return names;
    }

    private static void CollectPropertyNames(JsonElement element, ICollection<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                names.Add(property.Name);
                CollectPropertyNames(property.Value, names);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                CollectPropertyNames(item, names);
            }
        }
    }
}
