using Watchlist.Application;
using Watchlist.Domain;
using Watchlist.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddWatchlistInfrastructure();
builder.Services.AddScoped<WatchlistQueryService>();

WebApplication app = builder.Build();

app.MapGet("/api/watchlist", async (
    string? mediaType,
    string? filter,
    WatchlistQueryService queryService,
    CancellationToken cancellationToken) =>
{
    if (!TryParseMediaType(mediaType, out MediaType parsedMediaType))
    {
        return Results.BadRequest(new { error = "Invalid mediaType." });
    }

    if (!TryParseFilter(filter, out WatchlistFilter parsedFilter))
    {
        return Results.BadRequest(new { error = "Invalid filter." });
    }

    IReadOnlyList<WatchlistItemDto> items = await queryService.GetItemsAsync(
        parsedMediaType,
        parsedFilter,
        cancellationToken);

    return Results.Ok(items);
});

app.MapGet("/api/watchlist/{id}", async (
    string id,
    WatchlistQueryService queryService,
    CancellationToken cancellationToken) =>
{
    WatchlistItemDto? item = await queryService.GetItemAsync(id, cancellationToken);

    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapGet("/api/sync/status", async (
    ISyncStatusReadRepository repository,
    CancellationToken cancellationToken) =>
{
    SyncStatusDto? status = await repository.GetLatestAsync(cancellationToken);

    return status is null ? Results.NotFound() : Results.Ok(status);
});

app.Run();

static bool TryParseMediaType(string? value, out MediaType mediaType)
{
    mediaType = value switch
    {
        "movie" => MediaType.Movie,
        "tv" => MediaType.TvShow,
        _ => MediaType.Unspecified
    };

    return mediaType != MediaType.Unspecified;
}

static bool TryParseFilter(string? value, out WatchlistFilter filter)
{
    filter = value switch
    {
        "all" => WatchlistFilter.All,
        "available" => WatchlistFilter.Available,
        _ => WatchlistFilter.All
    };

    return value is "all" or "available";
}

public partial class Program;
