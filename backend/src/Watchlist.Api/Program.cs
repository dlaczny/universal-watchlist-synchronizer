using Watchlist.Application;
using Watchlist.Api;
using Watchlist.Domain;
using Watchlist.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddWatchlistInfrastructure(builder.Configuration);
builder.Services.AddScoped<WatchlistQueryService>();
builder.Services.AddExceptionHandler<MongoUnavailableExceptionHandler>();
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/api/watchlist", async (
    string? collection,
    string? availability,
    string? sort,
    WatchlistQueryService queryService,
    CancellationToken cancellationToken) =>
{
    if (!TryParseCollection(collection, out WatchlistCollection parsedCollection))
    {
        return Results.BadRequest(new { error = "Invalid collection." });
    }

    if (!TryParseAvailability(availability, out IReadOnlySet<AvailabilityStatus> parsedAvailability))
    {
        return Results.BadRequest(new { error = "Invalid availability." });
    }

    if (!TryParseSort(sort, out WatchlistSort parsedSort))
    {
        return Results.BadRequest(new { error = "Invalid sort." });
    }

    WatchlistQuery query = new(parsedCollection, parsedAvailability, parsedSort);
    IReadOnlyList<WatchlistItemDto> items = await queryService.GetItemsAsync(query, cancellationToken);

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

app.MapPost("/api/sync/letterboxd", async (
    ILetterboxdMovieSyncService syncService,
    CancellationToken cancellationToken) =>
{
    LetterboxdSyncResultDto result = await syncService.SyncAsync(cancellationToken);

    return Results.Ok(result);
});

app.Run();

static bool TryParseCollection(string? value, out WatchlistCollection collection)
{
    collection = value switch
    {
        null or "all" => WatchlistCollection.All,
        "movie" => WatchlistCollection.Movie,
        "tv" => WatchlistCollection.Tv,
        _ => WatchlistCollection.All
    };

    return value is null or "all" or "movie" or "tv";
}

static bool TryParseAvailability(string? value, out IReadOnlySet<AvailabilityStatus> availability)
{
    if (value is null)
    {
        availability = AllAvailabilityStatuses();
        return true;
    }

    string[] parts = value.Split(',', StringSplitOptions.None);
    if (parts.Length == 0 || parts.Any(string.IsNullOrWhiteSpace))
    {
        availability = new HashSet<AvailabilityStatus>();
        return false;
    }

    HashSet<AvailabilityStatus> parsed = [];
    foreach (string part in parts)
    {
        AvailabilityStatus status = part switch
        {
            "plex" => AvailabilityStatus.AvailableOnPlex,
            "not_on_plex" => AvailabilityStatus.NotOnPlex,
            "unreleased" => AvailabilityStatus.Unreleased,
            "unknown_match" => AvailabilityStatus.UnknownMatch,
            _ => AvailabilityStatus.Unspecified
        };

        if (status == AvailabilityStatus.Unspecified)
        {
            availability = new HashSet<AvailabilityStatus>();
            return false;
        }

        parsed.Add(status);
    }

    availability = parsed;
    return availability.Count > 0;
}

static bool TryParseSort(string? value, out WatchlistSort sort)
{
    sort = value switch
    {
        null or "added_desc" => WatchlistSort.AddedDescending,
        "title_asc" => WatchlistSort.TitleAscending,
        _ => WatchlistSort.AddedDescending
    };

    return value is null or "added_desc" or "title_asc";
}

static IReadOnlySet<AvailabilityStatus> AllAvailabilityStatuses()
{
    return new HashSet<AvailabilityStatus>
    {
        AvailabilityStatus.AvailableOnPlex,
        AvailabilityStatus.NotOnPlex,
        AvailabilityStatus.Unreleased,
        AvailabilityStatus.UnknownMatch
    };
}

public partial class Program;
