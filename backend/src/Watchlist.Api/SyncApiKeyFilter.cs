using System.Security.Cryptography;
using System.Text;

namespace Watchlist.Api;

public sealed class SyncApiKeyFilter(IConfiguration configuration) : IEndpointFilter
{
    private const string HeaderName = "X-Watchlist-Sync-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        string? configuredKey = configuration["Sync:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return await next(context);
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var suppliedValues)
            || suppliedValues.Count != 1
            || !KeysMatch(configuredKey, suppliedValues[0]))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }

    private static bool KeysMatch(string configuredKey, string? suppliedKey)
    {
        if (suppliedKey is null)
        {
            return false;
        }

        byte[] configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);

        return configuredBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(configuredBytes, suppliedBytes);
    }
}
