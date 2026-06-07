using System.Net;
using Microsoft.Extensions.Options;
using Watchlist.Application;
using Watchlist.Api;
using Watchlist.Domain;
using Watchlist.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    "appsettings.Development.Local.json",
    optional: true,
    reloadOnChange: true);

builder.Services.AddWatchlistInfrastructure(builder.Configuration);
builder.Services.AddScoped<WatchlistQueryService>();
builder.Services.AddScoped<WatchlistExportService>();
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

    return Results.Ok(items.Select(ToBackendImageUrls).ToList());
});

app.MapGet("/api/watchlist/{id}", async (
    string id,
    WatchlistQueryService queryService,
    CancellationToken cancellationToken) =>
{
    WatchlistItemDetailsDto? item = await queryService.GetItemDetailsAsync(id, cancellationToken);

    return item is null ? Results.NotFound() : Results.Ok(ToBackendDetailImageUrls(item));
});

app.MapGet("/api/export/radarr/movies", async (
    WatchlistExportService exportService,
    CancellationToken cancellationToken) =>
{
    IReadOnlyList<RadarrMovieExportItemDto> items =
        await exportService.GetRadarrMoviesAsync(cancellationToken);

    return Results.Ok(items);
});

app.MapGet("/api/export/sonarr/tv", async (
    WatchlistExportService exportService,
    CancellationToken cancellationToken) =>
{
    IReadOnlyList<object> items = await exportService.GetSonarrTvAsync(cancellationToken);

    return Results.Ok(items);
});

app.MapGet("/api/images/tmdb/{size}/{fileName}", async (
    string size,
    string fileName,
    IHttpClientFactory httpClientFactory,
    IOptions<TmdbOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!IsAllowedTmdbImageSize(size) || string.IsNullOrWhiteSpace(fileName))
    {
        return Results.BadRequest();
    }

    string imageBaseUrl = options.Value.ImageBaseUrl.TrimEnd('/');
    Uri imageUri = new($"{imageBaseUrl}/{Uri.EscapeDataString(size)}/{Uri.EscapeDataString(fileName)}");
    HttpClient httpClient = httpClientFactory.CreateClient();
    HttpResponseMessage response;
    try
    {
        response = await httpClient.GetAsync(imageUri, cancellationToken);
    }
    catch (HttpRequestException)
    {
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    using (response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.NotFound();
        }

        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        string contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
        return Results.File(bytes, contentType);
    }
});

app.MapGet("/api/images/plex/{ratingKey}/{kind}", async (
    string ratingKey,
    string kind,
    IPlexMovieInventoryRepository repository,
    IHttpClientFactory httpClientFactory,
    IOptions<PlexOptions> options,
    CancellationToken cancellationToken) =>
{
    if (kind is not ("poster" or "backdrop"))
    {
        return Results.BadRequest();
    }

    PlexMovieDto? movie = await repository.GetMovieAsync(ratingKey, cancellationToken);
    string? plexPath = kind == "poster" ? movie?.PosterPath : movie?.BackdropPath;
    if (string.IsNullOrWhiteSpace(plexPath))
    {
        return Results.NotFound();
    }

    PlexOptions plexOptions = options.Value;
    if (string.IsNullOrWhiteSpace(plexOptions.BaseUrl)
        || string.IsNullOrWhiteSpace(plexOptions.Token))
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    HttpClient httpClient = httpClientFactory.CreateClient();
    string separator = plexPath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
    Uri imageUri = new(
        $"{plexOptions.BaseUrl.TrimEnd('/')}{plexPath}{separator}X-Plex-Token={Uri.EscapeDataString(plexOptions.Token)}");

    HttpResponseMessage response;
    try
    {
        response = await httpClient.GetAsync(imageUri, cancellationToken);
    }
    catch (HttpRequestException)
    {
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    using (response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.NotFound();
        }

        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        string contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
        return Results.File(bytes, contentType);
    }
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

app.MapPost("/api/sync/tmdb/movies", async (
    ITmdbMovieEnrichmentService enrichmentService,
    CancellationToken cancellationToken) =>
{
    TmdbMovieEnrichmentResultDto result = await enrichmentService.SyncMoviesAsync(cancellationToken);

    return Results.Ok(result);
});

app.MapPost("/api/sync/tmdb/movies/{id}", async (
    string id,
    ITmdbMovieEnrichmentService enrichmentService,
    CancellationToken cancellationToken) =>
{
    TmdbSingleMovieEnrichmentResultDto? result = await enrichmentService.SyncMovieAsync(id, cancellationToken);

    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPost("/api/sync/plex/movies", async (
    IPlexMovieSyncService syncService,
    CancellationToken cancellationToken) =>
{
    PlexMovieSyncResultDto result = await syncService.SyncMoviesAsync(cancellationToken);

    return Results.Ok(result);
});

app.MapPost("/api/sync/tmdb/tv", async (
    ITmdbTvWatchlistSyncService syncService,
    CancellationToken cancellationToken) =>
{
    TmdbTvSyncResultDto result = await syncService.SyncAsync(cancellationToken);

    return Results.Ok(result);
});

app.MapPost("/api/sync/availability/refresh", async (
    IAvailabilityRefreshService refreshService,
    CancellationToken cancellationToken) =>
{
    AvailabilityRefreshResultDto result = await refreshService.RefreshAsync(cancellationToken);

    return Results.Ok(result);
});

app.MapPost("/api/sync/all", async (
    ICombinedSyncService syncService,
    CancellationToken cancellationToken) =>
{
    CombinedSyncResultDto result = await syncService.SyncAllAsync(cancellationToken);

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

static WatchlistItemDto ToBackendImageUrls(WatchlistItemDto item)
{
    return item with
    {
        PosterUrl = ToBackendTmdbImageUrl(item.PosterUrl),
        BackdropUrl = ToBackendTmdbImageUrl(item.BackdropUrl)
    };
}

static WatchlistItemDetailsDto ToBackendDetailImageUrls(WatchlistItemDetailsDto item)
{
    return item with
    {
        PosterUrl = ToBackendTmdbImageUrl(item.PosterUrl),
        BackdropUrl = ToBackendTmdbImageUrl(item.BackdropUrl)
    };
}

static string? ToBackendTmdbImageUrl(string? imageUrl)
{
    if (string.IsNullOrWhiteSpace(imageUrl)
        || !Uri.TryCreate(imageUrl, UriKind.Absolute, out Uri? uri)
        || !string.Equals(uri.Host, "image.tmdb.org", StringComparison.OrdinalIgnoreCase))
    {
        return imageUrl;
    }

    string[] segments = uri.AbsolutePath
        .Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length != 4
        || segments[0] != "t"
        || segments[1] != "p"
        || !IsAllowedTmdbImageSize(segments[2]))
    {
        return imageUrl;
    }

    return $"/api/images/tmdb/{segments[2]}/{segments[3]}";
}

static bool IsAllowedTmdbImageSize(string size)
{
    return size is "w500" or "w1280";
}

public partial class Program;
