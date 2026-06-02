using Microsoft.AspNetCore.Diagnostics;
using MongoDB.Driver;

namespace Watchlist.Api;

public sealed class MongoUnavailableExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not MongoException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await httpContext.Response.WriteAsJsonAsync(
            new { error = "MongoDB is unavailable." },
            cancellationToken);

        return true;
    }
}
