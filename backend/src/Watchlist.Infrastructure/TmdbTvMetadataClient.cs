using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class TmdbTvMetadataClient(
    IHttpClientFactory httpClientFactory,
    IOptions<TmdbOptions> options,
    IHttpRetryDelay? retryDelay = null,
    TimeProvider? timeProvider = null) : ITmdbTvMetadataClient
{
    public const string HttpClientName = "TmdbTvMetadata";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TmdbTvMetadataDto> GetTvMetadataAsync(int tmdbId, CancellationToken cancellationToken)
    {
        TmdbOptions tmdbOptions = options.Value;
        EnsureConfigured(tmdbOptions);
        EnsurePositive(tmdbId, nameof(tmdbId));
        using HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);

        TmdbTvDetailsResponse details = await GetDetailsAsync(
            httpClient,
            tmdbId,
            tmdbOptions,
            cancellationToken);
        TmdbTvExternalIdsResponse externalIds = await GetExternalIdsAsync(
            httpClient,
            tmdbId,
            tmdbOptions.AccessToken,
            cancellationToken);

        string? posterPath = NormalizeOptionalString(details.PosterPath);
        string? backdropPath = NormalizeOptionalString(details.BackdropPath);

        return new TmdbTvMetadataDto(
            details.Id,
            details.Name,
            details.OriginalName,
            NormalizeOptionalString(details.Overview),
            NormalizeOptionalString(details.FirstAirDate),
            NormalizeOptionalString(details.Status),
            posterPath,
            backdropPath,
            BuildImageUrl(tmdbOptions.ImageBaseUrl, "w500", posterPath),
            BuildImageUrl(tmdbOptions.ImageBaseUrl, "w1280", backdropPath),
            details.Genres
                ?.Select(genre => genre.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList() ?? [],
            NormalizeOptionalString(details.OriginalLanguage),
            details.VoteAverage,
            details.VoteCount,
            new TmdbTvExternalIdsDto(
                NormalizeOptionalString(externalIds.ImdbId),
                externalIds.TvdbId is > 0 ? externalIds.TvdbId : null));
    }

    public async Task<TmdbTvProviderDataDto> GetTvProvidersAsync(
        int tmdbId,
        CancellationToken cancellationToken)
    {
        EnsurePositive(tmdbId, nameof(tmdbId));
        TmdbOptions tmdbOptions = options.Value;
        EnsureConfigured(tmdbOptions);
        using HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);
        return await GetProvidersAsync(
            httpClient,
            $"/tv/{tmdbId}/watch/providers",
            tmdbId,
            tmdbOptions.AccessToken,
            cancellationToken);
    }

    public async Task<TmdbTvProviderDataDto> GetSeasonProvidersAsync(
        int tmdbId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        EnsurePositive(tmdbId, nameof(tmdbId));
        EnsurePositive(seasonNumber, nameof(seasonNumber));
        TmdbOptions tmdbOptions = options.Value;
        EnsureConfigured(tmdbOptions);
        using HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);
        return await GetProvidersAsync(
            httpClient,
            $"/tv/{tmdbId}/season/{seasonNumber}/watch/providers",
            null,
            tmdbOptions.AccessToken,
            cancellationToken);
    }

    public async Task<TmdbWatchProviderCatalogDto> GetProviderCatalogAsync(
        CancellationToken cancellationToken)
    {
        TmdbOptions tmdbOptions = options.Value;
        EnsureConfigured(tmdbOptions);
        using HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await GetAsync(
            httpClient,
            "/watch/providers/tv",
            tmdbOptions.AccessToken,
            cancellationToken);
        EnsureSuccess(response);

        try
        {
            TmdbCatalogResponse catalog = await ReadJsonAsync<TmdbCatalogResponse>(
                response,
                cancellationToken);
            if (catalog.Results is null)
            {
                throw new TmdbParseException("TMDB provider catalog response was invalid.");
            }

            List<TmdbWatchProviderCatalogEntryDto> providers = catalog.Results
                .Select(provider => provider is null
                    ? throw new TmdbParseException("TMDB provider catalog response was invalid.")
                    : new TmdbWatchProviderCatalogEntryDto(
                        provider.ProviderId,
                        provider.ProviderName ?? string.Empty,
                        NormalizeOptionalString(provider.LogoPath),
                        provider.DisplayPriority))
                .ToList();
            return new TmdbWatchProviderCatalogDto(GetUtcNow(), providers);
        }
        catch (Exception exception) when (IsSourceShapeException(exception))
        {
            throw new TmdbParseException("TMDB provider catalog response was invalid.");
        }
    }

    public async Task<TmdbWatchProviderRegionsDto> GetProviderRegionsAsync(
        CancellationToken cancellationToken)
    {
        TmdbOptions tmdbOptions = options.Value;
        EnsureConfigured(tmdbOptions);
        using HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await GetAsync(
            httpClient,
            "/watch/providers/regions",
            tmdbOptions.AccessToken,
            cancellationToken);
        EnsureSuccess(response);

        try
        {
            TmdbRegionsResponse regions = await ReadJsonAsync<TmdbRegionsResponse>(
                response,
                cancellationToken);
            if (regions.Results is null)
            {
                throw new TmdbParseException("TMDB provider regions response was invalid.");
            }

            List<string> regionCodes = regions.Results
                .Select(region => region?.Code
                    ?? throw new TmdbParseException("TMDB provider regions response was invalid."))
                .ToList();
            return new TmdbWatchProviderRegionsDto(GetUtcNow(), regionCodes);
        }
        catch (Exception exception) when (IsSourceShapeException(exception))
        {
            throw new TmdbParseException("TMDB provider regions response was invalid.");
        }
    }

    private async Task<TmdbTvDetailsResponse> GetDetailsAsync(
        HttpClient httpClient,
        int tmdbId,
        TmdbOptions tmdbOptions,
        CancellationToken cancellationToken)
    {
        string requestUri = $"/tv/{tmdbId}?language={tmdbOptions.Language}";
        using HttpResponseMessage response = await GetAsync(
            httpClient,
            requestUri,
            tmdbOptions.AccessToken,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new TmdbTvNotFoundException($"TMDB TV show {tmdbId} was not found.");
        }

        EnsureSuccess(response);
        TmdbTvDetailsResponse details = await ReadJsonAsync<TmdbTvDetailsResponse>(
            response,
            cancellationToken);
        if (details.Id != tmdbId
            || string.IsNullOrWhiteSpace(details.Name)
            || string.IsNullOrWhiteSpace(details.OriginalName)
            || details.Genres is null)
        {
            throw new TmdbParseException("TMDB TV details response returned an invalid item.");
        }

        return details;
    }

    private async Task<TmdbTvExternalIdsResponse> GetExternalIdsAsync(
        HttpClient httpClient,
        int tmdbId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        string requestUri = $"/tv/{tmdbId}/external_ids";
        using HttpResponseMessage response = await GetAsync(
            httpClient,
            requestUri,
            accessToken,
            cancellationToken);

        EnsureSuccess(response);
        TmdbTvExternalIdsResponse ids = await ReadJsonAsync<TmdbTvExternalIdsResponse>(
            response,
            cancellationToken);
        if (ids.Id != tmdbId || ids.TvdbId is <= 0)
        {
            throw new TmdbParseException("TMDB TV external IDs response returned an invalid item.");
        }

        return ids;
    }

    private async Task<TmdbTvProviderDataDto> GetProvidersAsync(
        HttpClient httpClient,
        string requestUri,
        int? expectedResponseId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetAsync(
            httpClient,
            requestUri,
            accessToken,
            cancellationToken);
        EnsureSuccess(response);

        try
        {
            TmdbProviderResponse providerResponse = await ReadJsonAsync<TmdbProviderResponse>(
                response,
                cancellationToken);
            if (providerResponse.Id is not > 0
                || providerResponse.Results is null
                || (expectedResponseId is int expectedId && providerResponse.Id != expectedId))
            {
                throw new TmdbParseException("TMDB TV providers response was invalid.");
            }

            if (!providerResponse.Results.TryGetValue(
                    "PL",
                    out TmdbRegionProvidersResponse? poland))
            {
                return new TmdbTvProviderDataDto(
                    "PL",
                    TmdbProviderRegionPresence.Missing,
                    GetUtcNow(),
                    null,
                    []);
            }

            if (poland is null
                || poland.Flatrate is null
                || poland.Free is null
                || poland.Ads is null
                || poland.Rent is null
                || poland.Buy is null)
            {
                throw new TmdbParseException("TMDB TV providers response was invalid.");
            }

            List<TmdbTvProviderOfferDto> offers = [];
            AddOffers(offers, poland.Flatrate, "flatrate");
            AddOffers(offers, poland.Free, "free");
            AddOffers(offers, poland.Ads, "ads");
            AddOffers(offers, poland.Rent, "rent");
            AddOffers(offers, poland.Buy, "buy");
            return new TmdbTvProviderDataDto(
                "PL",
                TmdbProviderRegionPresence.Present,
                GetUtcNow(),
                NormalizeOptionalString(poland.Link),
                offers);
        }
        catch (Exception exception) when (IsSourceShapeException(exception))
        {
            throw new TmdbParseException("TMDB TV providers response was invalid.");
        }
    }

    private static void AddOffers(
        List<TmdbTvProviderOfferDto> destination,
        IReadOnlyList<TmdbProviderOfferResponse?> source,
        string category)
    {
        foreach (TmdbProviderOfferResponse? provider in source)
        {
            if (provider is null)
            {
                throw new TmdbParseException("TMDB TV providers response was invalid.");
            }

            destination.Add(new TmdbTvProviderOfferDto(
                provider.ProviderId,
                provider.ProviderName ?? string.Empty,
                category,
                NormalizeOptionalString(provider.LogoPath)));
        }
    }

    private async Task<HttpResponseMessage> GetAsync(
        HttpClient httpClient,
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
        catch (HttpRequestException)
        {
            throw new TmdbUnavailableException("TMDB could not be reached.");
        }
        catch (IOException)
        {
            throw new TmdbUnavailableException("TMDB could not be reached.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TmdbUnavailableException("TMDB request timed out.");
        }
    }

    private static async Task<TResponse> ReadJsonAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            TResponse? result = await JsonSerializer.DeserializeAsync<TResponse>(
                stream,
                SerializerOptions,
                cancellationToken);
            return result ?? throw new TmdbParseException("TMDB response was invalid.");
        }
        catch (JsonException)
        {
            throw new TmdbParseException("TMDB response was invalid.");
        }
        catch (NotSupportedException)
        {
            throw new TmdbParseException("TMDB response was invalid.");
        }
        catch (HttpRequestException)
        {
            throw new TmdbUnavailableException("TMDB could not be reached.");
        }
        catch (IOException)
        {
            throw new TmdbUnavailableException("TMDB could not be reached.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TmdbUnavailableException("TMDB request timed out.");
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.TooManyRequests)
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

    private static void EnsureConfigured(TmdbOptions tmdbOptions)
    {
        if (string.IsNullOrWhiteSpace(tmdbOptions.AccessToken))
        {
            throw new TmdbUnavailableException("TMDB access token is not configured.");
        }
    }

    private static void EnsurePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private DateTimeOffset GetUtcNow()
    {
        return (timeProvider ?? TimeProvider.System).GetUtcNow().ToUniversalTime();
    }

    private static bool IsSourceShapeException(Exception exception)
    {
        return exception is TmdbParseException
            or ArgumentException
            or InvalidOperationException;
    }

    private sealed record TmdbTvDetailsResponse(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("original_name")] string OriginalName,
        [property: JsonPropertyName("overview")] string? Overview,
        [property: JsonPropertyName("first_air_date")] string? FirstAirDate,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        [property: JsonPropertyName("backdrop_path")] string? BackdropPath,
        [property: JsonPropertyName("original_language")] string? OriginalLanguage,
        [property: JsonPropertyName("vote_average")] double? VoteAverage,
        [property: JsonPropertyName("vote_count")] int? VoteCount,
        [property: JsonPropertyName("genres")] IReadOnlyList<TmdbGenreResponse>? Genres);

    private sealed record TmdbGenreResponse(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record TmdbTvExternalIdsResponse(
        int Id,
        [property: JsonPropertyName("imdb_id")] string? ImdbId,
        [property: JsonPropertyName("tvdb_id")] int? TvdbId);

    private sealed record TmdbProviderResponse(
        [property: JsonPropertyName("id")] int? Id,
        [property: JsonPropertyName("results"), JsonRequired]
        IReadOnlyDictionary<string, TmdbRegionProvidersResponse?>? Results);

    private sealed class TmdbRegionProvidersResponse
    {
        [JsonPropertyName("link")]
        public string? Link { get; init; }

        [JsonPropertyName("flatrate")]
        public IReadOnlyList<TmdbProviderOfferResponse?>? Flatrate { get; init; } = [];

        [JsonPropertyName("free")]
        public IReadOnlyList<TmdbProviderOfferResponse?>? Free { get; init; } = [];

        [JsonPropertyName("ads")]
        public IReadOnlyList<TmdbProviderOfferResponse?>? Ads { get; init; } = [];

        [JsonPropertyName("rent")]
        public IReadOnlyList<TmdbProviderOfferResponse?>? Rent { get; init; } = [];

        [JsonPropertyName("buy")]
        public IReadOnlyList<TmdbProviderOfferResponse?>? Buy { get; init; } = [];
    }

    private sealed record TmdbProviderOfferResponse(
        [property: JsonPropertyName("provider_id")] int ProviderId,
        [property: JsonPropertyName("provider_name")] string? ProviderName,
        [property: JsonPropertyName("logo_path")] string? LogoPath);

    private sealed record TmdbCatalogResponse(
        [property: JsonPropertyName("results"), JsonRequired]
        IReadOnlyList<TmdbCatalogEntryResponse?>? Results);

    private sealed record TmdbCatalogEntryResponse(
        [property: JsonPropertyName("provider_id")] int ProviderId,
        [property: JsonPropertyName("provider_name")] string? ProviderName,
        [property: JsonPropertyName("logo_path")] string? LogoPath,
        [property: JsonPropertyName("display_priority")] int DisplayPriority);

    private sealed record TmdbRegionsResponse(
        [property: JsonPropertyName("results"), JsonRequired]
        IReadOnlyList<TmdbRegionResponse?>? Results);

    private sealed record TmdbRegionResponse(
        [property: JsonPropertyName("iso_3166_1")] string? Code);
}
