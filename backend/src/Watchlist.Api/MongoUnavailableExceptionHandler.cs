using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Api;

public sealed class MongoUnavailableExceptionHandler(
    ILogger<MongoUnavailableExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is TraktNotConnectedException)
        {
            return await WriteTvFailureAsync(httpContext, StatusCodes.Status409Conflict,
                "trakt_not_connected", "Trakt is not connected.", cancellationToken);
        }

        if (exception is TvSourceSnapshotRejectedException rejected)
        {
            logger.LogWarning(
                "TV source snapshot rejected: {Reason}",
                IsStableSnapshotReason(rejected.Message) ? rejected.Message : "unknown");
            return await WriteTvFailureAsync(httpContext, StatusCodes.Status502BadGateway,
                "tv_snapshot_rejected", "The TV source snapshot was rejected.", cancellationToken);
        }

        if (exception is TraktParseException)
        {
            return await WriteTvFailureAsync(httpContext, StatusCodes.Status502BadGateway,
                "trakt_malformed_response", "Trakt returned a malformed response.", cancellationToken);
        }

        if (exception is TmdbParseException)
        {
            return await WriteTvFailureAsync(httpContext, StatusCodes.Status502BadGateway,
                "tmdb_malformed_response", "TMDB returned a malformed response.", cancellationToken);
        }

        if (exception is TraktUnavailableException)
        {
            return await WriteTvFailureAsync(httpContext, StatusCodes.Status503ServiceUnavailable,
                "trakt_unavailable", "Trakt is temporarily unavailable.", cancellationToken);
        }

        if (exception is TraktConnectionUnreadableException)
        {
            return await WriteTvFailureAsync(httpContext, StatusCodes.Status503ServiceUnavailable,
                "trakt_connection_unreadable", "The Trakt connection is unavailable.", cancellationToken);
        }

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

        if (exception is LetterboxdSnapshotRejectedException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
            await httpContext.Response.WriteAsJsonAsync(
                new { error = "Letterboxd watchlist snapshot was rejected." },
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
            logger.LogError(
                "Unhandled backend exception type: {ExceptionType}",
                exception.GetType().Name);
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await httpContext.Response.WriteAsJsonAsync(
            new { error = "MongoDB is unavailable." },
            cancellationToken);

        return true;
    }

    private static async ValueTask<bool> WriteTvFailureAsync(
        HttpContext context,
        int statusCode,
        string code,
        string error,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new { code, error }, cancellationToken);
        return true;
    }

    private static bool IsStableSnapshotReason(string value)
    {
        return value.Length is > 0 and <= 80
            && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
    }
}
