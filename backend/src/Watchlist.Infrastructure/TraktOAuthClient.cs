using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

/// <summary>
/// Performs the Trakt device and refresh-token OAuth exchanges.
/// </summary>
public sealed class TraktOAuthClient(
    HttpClient httpClient,
    IOptions<TraktOptions> options) : ITraktOAuthClient
{
    public async Task<TraktDeviceCode> StartDeviceAsync(CancellationToken cancellationToken)
    {
        DeviceCodeRequest payload = new(options.Value.ClientId);
        using HttpResponseMessage response = await PostAsync(
            "/oauth/device/code",
            payload,
            cancellationToken);
        EnsureAvailable(response);
        DeviceCodeResponse result = await ReadJsonAsync<DeviceCodeResponse>(
            response,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(result.DeviceCode)
            || string.IsNullOrWhiteSpace(result.UserCode)
            || string.IsNullOrWhiteSpace(result.VerificationUrl)
            || result.ExpiresIn <= 0
            || result.Interval <= 0)
        {
            throw new TraktParseException();
        }

        return new TraktDeviceCode(
            result.DeviceCode,
            result.UserCode,
            result.VerificationUrl,
            TimeSpan.FromSeconds(result.ExpiresIn),
            TimeSpan.FromSeconds(result.Interval));
    }

    public async Task<TraktTokenGrant?> PollDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken)
    {
        DeviceTokenRequest payload = new(
            deviceCode,
            options.Value.ClientId,
            options.Value.ClientSecret);
        using HttpResponseMessage response = await PostAsync(
            "/oauth/device/token",
            payload,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return null;
        }

        string? outcomeCode = response.StatusCode switch
        {
            HttpStatusCode.NotFound => "invalid",
            HttpStatusCode.Conflict => "already_used",
            HttpStatusCode.Gone => "expired",
            (HttpStatusCode)418 => "denied",
            HttpStatusCode.TooManyRequests => "slow_down",
            _ => null
        };
        if (outcomeCode is not null)
        {
            throw new TraktDeviceAuthorizationException(outcomeCode);
        }

        EnsureAvailable(response);
        return await ReadGrantAsync(response, cancellationToken);
    }

    public async Task<TraktTokenGrant> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        RefreshTokenRequest payload = new(
            refreshToken,
            options.Value.ClientId,
            options.Value.ClientSecret,
            options.Value.RedirectUri,
            "refresh_token");
        using HttpResponseMessage response = await PostAsync(
            "/oauth/token",
            payload,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new TraktRefreshRejectedException();
        }

        EnsureAvailable(response);
        return await ReadGrantAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> PostAsync<TPayload>(
        string path,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        using JsonContent content = JsonContent.Create(payload);
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = content
        };
        try
        {
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new TraktUnavailableException();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TraktUnavailableException();
        }
    }

    private static void EnsureAvailable(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new TraktUnavailableException();
        }
    }

    private static async Task<TraktTokenGrant> ReadGrantAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        TokenResponse result = await ReadJsonAsync<TokenResponse>(response, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.AccessToken)
            || string.IsNullOrWhiteSpace(result.RefreshToken)
            || result.ExpiresIn <= 0
            || result.CreatedAt <= 0)
        {
            throw new TraktParseException();
        }

        DateTimeOffset createdAt;
        try
        {
            createdAt = DateTimeOffset.FromUnixTimeSeconds(result.CreatedAt);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new TraktParseException();
        }

        return new TraktTokenGrant(
            result.AccessToken,
            result.RefreshToken,
            TimeSpan.FromSeconds(result.ExpiresIn),
            createdAt);
    }

    private static async Task<TResponse> ReadJsonAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            TResponse? result = await response.Content.ReadFromJsonAsync<TResponse>(
                cancellationToken: cancellationToken);
            return result ?? throw new TraktParseException();
        }
        catch (JsonException)
        {
            throw new TraktParseException();
        }
        catch (NotSupportedException)
        {
            throw new TraktParseException();
        }
    }

    private sealed record DeviceCodeRequest(
        [property: JsonPropertyName("client_id")] string ClientId);

    private sealed record DeviceTokenRequest(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("client_secret")] string ClientSecret);

    private sealed record RefreshTokenRequest(
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("client_secret")] string ClientSecret,
        [property: JsonPropertyName("redirect_uri")] string RedirectUri,
        [property: JsonPropertyName("grant_type")] string GrantType);

    private sealed record DeviceCodeResponse(
        [property: JsonPropertyName("device_code")] string? DeviceCode,
        [property: JsonPropertyName("user_code")] string? UserCode,
        [property: JsonPropertyName("verification_url")] string? VerificationUrl,
        [property: JsonPropertyName("expires_in")] long ExpiresIn,
        [property: JsonPropertyName("interval")] long Interval);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn,
        [property: JsonPropertyName("created_at")] long CreatedAt);
}
