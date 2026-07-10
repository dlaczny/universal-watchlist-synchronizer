using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class TmdbMovieClient(
    HttpClient httpClient,
    IOptions<TmdbOptions> options,
    IHttpRetryDelay? retryDelay = null) : ITmdbMovieClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TmdbMovieMetadataDto> GetMovieMetadataAsync(
        int candidateTmdbId,
        string? imdbId,
        CancellationToken cancellationToken)
    {
        TmdbOptions tmdbOptions = options.Value;
        if (string.IsNullOrWhiteSpace(tmdbOptions.AccessToken))
        {
            throw new TmdbUnavailableException("TMDB access token is not configured.");
        }

        int tmdbId = await ResolveTmdbIdAsync(candidateTmdbId, imdbId, tmdbOptions.AccessToken, cancellationToken);
        TmdbMovieDetailsDto details = await GetDetailsAsync(tmdbId, tmdbOptions, cancellationToken);
        TmdbMovieProviderDataDto providers = await GetProvidersAsync(tmdbId, tmdbOptions.AccessToken, cancellationToken);

        return new TmdbMovieMetadataDto(details, providers);
    }

    private async Task<int> ResolveTmdbIdAsync(
        int candidateTmdbId,
        string? imdbId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetAsync(
            $"/movie/{candidateTmdbId}",
            accessToken,
            cancellationToken);

        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            EnsureSuccess(response);
            return candidateTmdbId;
        }

        if (string.IsNullOrWhiteSpace(imdbId))
        {
            throw new TmdbMovieNotFoundException($"TMDB movie {candidateTmdbId} was not found.");
        }

        string escapedImdbId = Uri.EscapeDataString(imdbId);
        using HttpResponseMessage findResponse = await GetAsync(
            $"/find/{escapedImdbId}?external_source=imdb_id",
            accessToken,
            cancellationToken);

        if (findResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new TmdbMovieNotFoundException($"TMDB movie {candidateTmdbId} was not found.");
        }

        EnsureSuccess(findResponse);
        string content = await findResponse.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            TmdbFindResponse? find = JsonSerializer.Deserialize<TmdbFindResponse>(content, SerializerOptions);
            TmdbFindMovieResult? movie = find?.MovieResults?.FirstOrDefault();
            if (movie is null)
            {
                throw new TmdbMovieNotFoundException(
                    $"TMDB movie {candidateTmdbId} was not found and IMDb fallback returned no results.");
            }

            return movie.Id;
        }
        catch (JsonException exception)
        {
            throw new TmdbParseException("TMDB find response returned malformed JSON.", exception);
        }
    }

    private async Task<TmdbMovieDetailsDto> GetDetailsAsync(
        int tmdbId,
        TmdbOptions tmdbOptions,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetAsync(
            $"/movie/{tmdbId}",
            tmdbOptions.AccessToken,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new TmdbMovieNotFoundException($"TMDB movie {tmdbId} was not found.");
        }

        EnsureSuccess(response);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            TmdbMovieDetailsResponse? details = JsonSerializer.Deserialize<TmdbMovieDetailsResponse>(
                content,
                SerializerOptions);
            if (details is null
                || details.Id <= 0
                || string.IsNullOrWhiteSpace(details.Title)
                || string.IsNullOrWhiteSpace(details.OriginalTitle)
                || details.Genres is null)
            {
                throw new TmdbParseException("TMDB movie details response returned an invalid movie item.");
            }

            string? posterPath = NormalizeOptionalString(details.PosterPath);
            string? backdropPath = NormalizeOptionalString(details.BackdropPath);

            return new TmdbMovieDetailsDto(
                details.Id,
                NormalizeOptionalString(details.ImdbId),
                details.Title,
                details.OriginalTitle,
                NormalizeOptionalString(details.Overview),
                NormalizeOptionalString(details.ReleaseDate),
                posterPath,
                backdropPath,
                BuildImageUrl(tmdbOptions.ImageBaseUrl, "w500", posterPath),
                BuildImageUrl(tmdbOptions.ImageBaseUrl, "w1280", backdropPath),
                details.Genres
                    .Select(genre => genre.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList(),
                details.Runtime is > 0 ? details.Runtime : null,
                NormalizeOptionalString(details.OriginalLanguage),
                details.VoteAverage,
                details.VoteCount);
        }
        catch (JsonException exception)
        {
            throw new TmdbParseException("TMDB movie details response returned malformed JSON.", exception);
        }
    }

    private async Task<TmdbMovieProviderDataDto> GetProvidersAsync(
        int tmdbId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetAsync(
            $"/movie/{tmdbId}/watch/providers",
            accessToken,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new TmdbMovieNotFoundException($"TMDB movie {tmdbId} provider data was not found.");
        }

        EnsureSuccess(response);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            TmdbWatchProvidersResponse? providers = JsonSerializer.Deserialize<TmdbWatchProvidersResponse>(
                content,
                SerializerOptions);
            Dictionary<string, TmdbRegionWatchProvidersDto> regions = new(StringComparer.OrdinalIgnoreCase);

            IReadOnlyDictionary<string, TmdbRegionProvidersResponse?> sourceRegions =
                providers?.Results ?? new Dictionary<string, TmdbRegionProvidersResponse?>();

            foreach (KeyValuePair<string, TmdbRegionProvidersResponse?> region in sourceRegions)
            {
                TmdbRegionProvidersResponse regionProviders = region.Value ?? new TmdbRegionProvidersResponse();
                regions[region.Key] = new TmdbRegionWatchProvidersDto(
                    ToProviders(regionProviders.Flatrate),
                    ToProviders(regionProviders.Rent),
                    ToProviders(regionProviders.Buy));
            }

            return new TmdbMovieProviderDataDto(regions);
        }
        catch (JsonException exception)
        {
            throw new TmdbParseException("TMDB watch providers response returned malformed JSON.", exception);
        }
    }

    private async Task<HttpResponseMessage> GetAsync(
        string requestUri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        Uri uri = BuildRequestUri(requestUri);

        try
        {
            return await HttpRetryPolicy.SendAsync(
                httpClient,
                () =>
                {
                    HttpRequestMessage request = new(HttpMethod.Get, uri);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    return request;
                },
                retryDelay ?? new DefaultHttpRetryDelay(TimeProvider.System),
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new TmdbUnavailableException("TMDB could not be reached.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TmdbUnavailableException("TMDB request timed out.", exception);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.TooManyRequests)
        {
            throw new TmdbUnavailableException($"TMDB returned HTTP {(int)response.StatusCode}.");
        }

        if ((int)response.StatusCode >= 500)
        {
            throw new TmdbUnavailableException($"TMDB returned HTTP {(int)response.StatusCode}.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new TmdbUnavailableException($"TMDB returned HTTP {(int)response.StatusCode}.");
        }
    }

    private static IReadOnlyList<TmdbWatchProviderDto> ToProviders(
        IReadOnlyList<TmdbWatchProviderResponse>? providers)
    {
        return (providers ?? [])
            .Select(provider => new TmdbWatchProviderDto(
                provider.ProviderId,
                provider.ProviderName,
                NormalizeOptionalString(provider.LogoPath),
                provider.DisplayPriority))
            .ToList();
    }

    private static string? BuildImageUrl(string imageBaseUrl, string size, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return $"{imageBaseUrl.TrimEnd('/')}/{size}{path}";
    }

    private static string? NormalizeOptionalString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private Uri BuildRequestUri(string requestUri)
    {
        string baseUrl = options.Value.BaseUrl.TrimEnd('/');
        string relativePath = requestUri.TrimStart('/');
        return new Uri($"{baseUrl}/{relativePath}", UriKind.Absolute);
    }

    private sealed record TmdbMovieDetailsResponse(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("imdb_id")] string? ImdbId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("original_title")] string OriginalTitle,
        [property: JsonPropertyName("overview")] string? Overview,
        [property: JsonPropertyName("release_date")] string? ReleaseDate,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        [property: JsonPropertyName("backdrop_path")] string? BackdropPath,
        [property: JsonPropertyName("runtime")] int? Runtime,
        [property: JsonPropertyName("original_language")] string? OriginalLanguage,
        [property: JsonPropertyName("vote_average")] double? VoteAverage,
        [property: JsonPropertyName("vote_count")] int? VoteCount,
        [property: JsonPropertyName("genres")] IReadOnlyList<TmdbGenreResponse>? Genres);

    private sealed record TmdbGenreResponse(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record TmdbFindResponse(
        [property: JsonPropertyName("movie_results")] IReadOnlyList<TmdbFindMovieResult>? MovieResults);

    private sealed record TmdbFindMovieResult(
        [property: JsonPropertyName("id")] int Id);

    private sealed class TmdbWatchProvidersResponse
    {
        [JsonPropertyName("results")]
        public IReadOnlyDictionary<string, TmdbRegionProvidersResponse?> Results { get; init; }
            = new Dictionary<string, TmdbRegionProvidersResponse?>();
    }

    private sealed class TmdbRegionProvidersResponse
    {
        [JsonPropertyName("flatrate")]
        public IReadOnlyList<TmdbWatchProviderResponse> Flatrate { get; init; } = [];

        [JsonPropertyName("rent")]
        public IReadOnlyList<TmdbWatchProviderResponse> Rent { get; init; } = [];

        [JsonPropertyName("buy")]
        public IReadOnlyList<TmdbWatchProviderResponse> Buy { get; init; } = [];
    }

    private sealed record TmdbWatchProviderResponse(
        [property: JsonPropertyName("provider_id")] int ProviderId,
        [property: JsonPropertyName("provider_name")] string ProviderName,
        [property: JsonPropertyName("logo_path")] string? LogoPath,
        [property: JsonPropertyName("display_priority")] int DisplayPriority);
}
