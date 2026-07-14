using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class TraktOAuthClientTests
{
    [Fact]
    public async Task StartDeviceAsync_SendsExactJsonRequestAndParsesDeviceCode()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {
              "device_code": "device-code",
              "user_code": "ABCD1234",
              "verification_url": "https://trakt.tv/activate",
              "expires_in": 600,
              "interval": 5
            }
            """));
        TraktOAuthClient client = CreateClient(handler);

        TraktDeviceCode result = await client.StartDeviceAsync(CancellationToken.None);

        RecordedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/oauth/device/code");
        request.ContentType.Should().Be("application/json");
        AssertJson(request.Body, """{"client_id":"client-id"}""");
        result.Should().Be(new TraktDeviceCode(
            "device-code",
            "ABCD1234",
            "https://trakt.tv/activate",
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PollDeviceAsync_SendsExactJsonRequestAndParsesGrant()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {
              "access_token": "new-access-token",
              "refresh_token": "new-refresh-token",
              "expires_in": 7200,
              "created_at": 1784023200
            }
            """));
        TraktOAuthClient client = CreateClient(handler);

        TraktTokenGrant? result = await client.PollDeviceAsync(
            "device-code",
            CancellationToken.None);

        RecordedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/oauth/device/token");
        request.ContentType.Should().Be("application/json");
        AssertJson(
            request.Body,
            """{"code":"device-code","client_id":"client-id","client_secret":"client-secret"}""");
        result.Should().Be(new TraktTokenGrant(
            "new-access-token",
            "new-refresh-token",
            TimeSpan.FromHours(2),
            DateTimeOffset.FromUnixTimeSeconds(1784023200)));
    }

    [Fact]
    public async Task RefreshAsync_SendsExactJsonRequestAndParsesGrant()
    {
        RecordingHandler handler = new(_ => JsonResponse("""
            {
              "access_token": "rotated-access-token",
              "refresh_token": "rotated-refresh-token",
              "expires_in": 3600,
              "created_at": 1784023200
            }
            """));
        TraktOAuthClient client = CreateClient(handler);

        TraktTokenGrant result = await client.RefreshAsync(
            "refresh-token",
            CancellationToken.None);

        RecordedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/oauth/token");
        request.ContentType.Should().Be("application/json");
        AssertJson(
            request.Body,
            """{"refresh_token":"refresh-token","client_id":"client-id","client_secret":"client-secret","redirect_uri":"urn:ietf:wg:oauth:2.0:oob","grant_type":"refresh_token"}""");
        result.Should().Be(new TraktTokenGrant(
            "rotated-access-token",
            "rotated-refresh-token",
            TimeSpan.FromHours(1),
            DateTimeOffset.FromUnixTimeSeconds(1784023200)));
    }

    [Fact]
    public async Task PollDeviceAsync_WhenPending_ReturnsNull()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        TraktOAuthClient client = CreateClient(handler);

        TraktTokenGrant? result = await client.PollDeviceAsync(
            "device-code",
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "invalid")]
    [InlineData(HttpStatusCode.Conflict, "already_used")]
    [InlineData(HttpStatusCode.Gone, "expired")]
    [InlineData((HttpStatusCode)418, "denied")]
    [InlineData(HttpStatusCode.TooManyRequests, "slow_down")]
    public async Task PollDeviceAsync_WhenAuthorizationHasStableOutcome_ThrowsTypedCode(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        string secretBody = "response-body-secret-sentinel-622ebc";
        RecordingHandler handler = new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(secretBody)
        });
        TraktOAuthClient client = CreateClient(handler);

        Func<Task> action = async () => await client.PollDeviceAsync(
            "device-code",
            CancellationToken.None);

        TraktDeviceAuthorizationException exception = (await action.Should()
            .ThrowAsync<TraktDeviceAuthorizationException>())
            .Which;
        exception.Code.Should().Be(expectedCode);
        exception.ToString().Should().NotContain(secretBody);
        exception.ToString().Should().NotContain("device-code");
        exception.ToString().Should().NotContain("client-secret");
    }

    [Fact]
    public async Task PollDeviceAsync_WhenSuccessfulJsonIsMalformed_ThrowsSanitizedParseException()
    {
        string malformedBody = "malformed-secret-sentinel-83fa10";
        RecordingHandler handler = new(_ => JsonResponse(malformedBody));
        TraktOAuthClient client = CreateClient(handler);

        Func<Task> action = async () => await client.PollDeviceAsync(
            "device-code",
            CancellationToken.None);

        TraktParseException exception = (await action.Should()
            .ThrowAsync<TraktParseException>())
            .Which;
        exception.Message.Should().Be("The Trakt response could not be parsed.");
        exception.ToString().Should().NotContain(malformedBody);
        exception.ToString().Should().NotContain("device-code");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task PollDeviceAsync_WhenDependencyUnavailable_ThrowsFixedSanitizedException(
        HttpStatusCode statusCode)
    {
        string secretBody = "upstream-secret-sentinel-b82524";
        RecordingHandler handler = new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(secretBody)
        });
        TraktOAuthClient client = CreateClient(handler);

        Func<Task> action = async () => await client.PollDeviceAsync(
            "device-code",
            CancellationToken.None);

        TraktUnavailableException exception = (await action.Should()
            .ThrowAsync<TraktUnavailableException>())
            .Which;
        exception.Message.Should().Be("Trakt is temporarily unavailable.");
        exception.ToString().Should().NotContain(secretBody);
        exception.ToString().Should().NotContain("device-code");
    }

    [Fact]
    public async Task StartDeviceAsync_WhenTransportFails_ThrowsFixedSanitizedException()
    {
        string transportSecret = "transport-secret-sentinel-e2d799";
        ThrowingHandler handler = new(new HttpRequestException(transportSecret));
        TraktOAuthClient client = CreateClient(handler);

        Func<Task> action = () => client.StartDeviceAsync(CancellationToken.None);

        TraktUnavailableException exception = (await action.Should()
            .ThrowAsync<TraktUnavailableException>())
            .Which;
        exception.Message.Should().Be("Trakt is temporarily unavailable.");
        exception.ToString().Should().NotContain(transportSecret);
    }

    [Fact]
    public async Task StartDeviceAsync_WhenNonCallerTimeoutOccurs_ThrowsUnavailable()
    {
        ThrowingHandler handler = new(new TaskCanceledException("timeout-secret-sentinel-c21ce7"));
        TraktOAuthClient client = CreateClient(handler);

        Func<Task> action = () => client.StartDeviceAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TraktUnavailableException>();
    }

    [Fact]
    public async Task StartDeviceAsync_WhenCallerCancels_PreservesCancellation()
    {
        BlockingHandler handler = new();
        TraktOAuthClient client = CreateClient(handler);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> action = () => client.StartDeviceAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        await action.Should().NotThrowAsync<TraktUnavailableException>();
    }

    [Fact]
    public async Task RefreshAsync_WhenRefreshTokenDefinitelyRejected_ThrowsTypedRejection()
    {
        string secretBody = "refresh-rejection-secret-sentinel-7b35ca";
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(secretBody)
        });
        TraktOAuthClient client = CreateClient(handler);

        Func<Task> action = () => client.RefreshAsync(
            "refresh-token",
            CancellationToken.None);

        TraktRefreshRejectedException exception = (await action.Should()
            .ThrowAsync<TraktRefreshRejectedException>())
            .Which;
        exception.Message.Should().Be("Trakt rejected the stored refresh token.");
        exception.ToString().Should().NotContain(secretBody);
        exception.ToString().Should().NotContain("refresh-token");
    }

    private static TraktOAuthClient CreateClient(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://api.trakt.tv")
        };
        TraktOptions options = new()
        {
            BaseUrl = "https://api.trakt.tv",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "urn:ietf:wg:oauth:2.0:oob"
        };
        return new TraktOAuthClient(httpClient, Options.Create(options));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static void AssertJson(string actual, string expected)
    {
        using JsonDocument actualDocument = JsonDocument.Parse(actual);
        using JsonDocument expectedDocument = JsonDocument.Parse(expected);
        actualDocument.RootElement.ToString().Should().Be(expectedDocument.RootElement.ToString());
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string PathAndQuery,
        string? ContentType,
        string Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.PathAndQuery,
                request.Content?.Headers.ContentType?.MediaType,
                body));
            return responseFactory(request);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
