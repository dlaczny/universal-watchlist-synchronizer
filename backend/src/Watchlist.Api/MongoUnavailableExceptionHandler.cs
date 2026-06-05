using Microsoft.AspNetCore.Diagnostics;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Api;

public sealed class MongoUnavailableExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is LetterboxdUnavailableException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await httpContext.Response.WriteAsJsonAsync(
                new { error = "Letterboxd watchlist is unavailable." },
                cancellationToken);

            return true;
        }

        if (exception is LetterboxdParseException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
            await httpContext.Response.WriteAsJsonAsync(
                new { error = "Letterboxd watchlist returned malformed JSON." },
                cancellationToken);

            return true;
        }

        if (exception is TmdbUnavailableException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await httpContext.Response.WriteAsJsonAsync(
                new { error = "TMDB is unavailable." },
                cancellationToken);

            return true;
        }

        if (exception is PlexUnavailableException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await httpContext.Response.WriteAsJsonAsync(
                new { error = "Plex is unavailable." },
                cancellationToken);

            return true;
        }

        if (exception is PlexParseException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
            await httpContext.Response.WriteAsJsonAsync(
                new { error = "Plex returned malformed XML." },
                cancellationToken);

            return true;
        }

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
