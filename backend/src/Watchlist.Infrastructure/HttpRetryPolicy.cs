using System.Net;

namespace Watchlist.Infrastructure;

public static class HttpRetryPolicy
{
    private const int MaxAttempts = 4;

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        Func<HttpRequestMessage> requestFactory,
        IHttpRetryDelay retryDelay,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                using HttpRequestMessage request = requestFactory();
                response = await httpClient.SendAsync(request, cancellationToken);
                if (!IsTransientStatus(response.StatusCode) || attempt == MaxAttempts)
                {
                    return response;
                }

                await retryDelay.DelayAsync(attempt, response, cancellationToken);
                response.Dispose();
                response = null;
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await retryDelay.DelayAsync(attempt, response, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                await retryDelay.DelayAsync(attempt, response, cancellationToken);
            }
        }

        throw new InvalidOperationException("HTTP retry policy exhausted attempts without returning or throwing.");
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }
}
