using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class TmdbTvMetadataClient(
    HttpClient httpClient,
    IOptions<TmdbOptions> options,
    IHttpRetryDelay? retryDelay = null) : ITmdbTvMetadataClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TmdbTvMetadataDto> GetTvMetadataAsync(int tmdbId, CancellationToken cancellationToken)
    {
        TmdbOptions tmdbOptions = options.Value;
        if (string.IsNullOrWhiteSpace(tmdbOptions.AccessToken))
        {
            throw new TmdbUnavailableException("TMDB access token is not configured.");
        }

        TmdbTvDetailsResponse details = await GetDetailsAsync(tmdbId, tmdbOptions, cancellationToken);
        TmdbTvExternalIdsResponse externalIds = await GetExternalIdsAsync(tmdbId, tmdbOptions.AccessToken, cancellationToken);

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

    private async Task<TmdbTvDetailsResponse> GetDetailsAsync(
        int tmdbId,
        TmdbOptions tmdbOptions,
        CancellationToken cancellationToken)
    {
        string requestUri = $"/tv/{tmdbId}?language={tmdbOptions.Language}";
        using HttpResponseMessage response = await GetAsync(requestUri, tmdbOptions.AccessToken, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new TmdbTvNotFoundException($"TMDB TV show {tmdbId} was not found.");
        }

        EnsureSuccess(response);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            TmdbTvDetailsResponse? details = JsonSerializer.Deserialize<TmdbTvDetailsResponse>(
                content,
                SerializerOptions);
            if (details is null
                || details.Id <= 0
                || string.IsNullOrWhiteSpace(details.Name)
                || string.IsNullOrWhiteSpace(details.OriginalName)
                || details.Genres is null)
            {
                throw new TmdbParseException("TMDB TV details response returned an invalid item.");
            }

            return details;
        }
        catch (JsonException exception)
        {
            throw new TmdbParseException("TMDB TV details response returned malformed JSON.", exception);
        }
    }

    private async Task<TmdbTvExternalIdsResponse> GetExternalIdsAsync(
        int tmdbId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        string requestUri = $"/tv/{tmdbId}/external_ids";
        using HttpResponseMessage response = await GetAsync(requestUri, accessToken, cancellationToken);

        EnsureSuccess(response);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            TmdbTvExternalIdsResponse? ids = JsonSerializer.Deserialize<TmdbTvExternalIdsResponse>(
                content,
                SerializerOptions);
            if (ids is null || ids.Id <= 0)
            {
                throw new TmdbParseException("TMDB TV external IDs response returned an invalid item.");
            }

            return ids;
        }
        catch (JsonException exception)
        {
            throw new TmdbParseException("TMDB TV external IDs response returned malformed JSON.", exception);
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
}
