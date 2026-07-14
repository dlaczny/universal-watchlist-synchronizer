using Watchlist.Application;

namespace Watchlist.Api;

/// <summary>
/// Maps protected TV integration management endpoints.
/// </summary>
public static class TvEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Adds the protected Trakt integration routes to the endpoint builder.
    /// </summary>
    public static IEndpointRouteBuilder MapTvEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder integrations = endpoints.MapGroup("/api/integrations")
            .AddEndpointFilter<SyncApiKeyFilter>();

        integrations.MapPost("/trakt/device/start", async (
            ITraktConnectionService connectionService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                TraktDeviceStartDto result = await connectionService.StartDeviceAsync(
                    cancellationToken);
                return Results.Ok(result);
            }
            catch (TraktConnectionPendingException)
            {
                return Results.Json(
                    new
                    {
                        code = "trakt_connection_pending",
                        error = "A Trakt device authorization is already pending."
                    },
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (TraktUnavailableException)
            {
                return Results.Json(
                    new
                    {
                        code = "trakt_unavailable",
                        error = "Trakt is temporarily unavailable."
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        integrations.MapGet("/trakt/status", async (
            ITraktConnectionService connectionService,
            CancellationToken cancellationToken) =>
        {
            TraktConnectionStatusDto result = await connectionService.GetStatusAsync(
                cancellationToken);
            return Results.Ok(result);
        });

        integrations.MapDelete("/trakt/connection", async (
            ITraktConnectionService connectionService,
            CancellationToken cancellationToken) =>
        {
            TraktConnectionStatusDto result = await connectionService.DisconnectAsync(
                cancellationToken);
            return Results.Ok(result);
        });

        return endpoints;
    }
}
