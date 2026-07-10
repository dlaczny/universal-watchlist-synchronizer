using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class TmdbTvWatchlistClient(
    HttpClient httpClient,
    IOptions<TmdbOptions> options,
    IHttpRetryDelay? retryDelay = null) : ITmdbTvWatchlistClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<TmdbTvWatchlistItemDto>> GetWatchlistAsync(CancellationToken cancellationToken)
    {
        TmdbOptions tmdbOptions = options.Value;
        if (string.IsNullOrWhiteSpace(tmdbOptions.AccessToken))
        {
            throw new TmdbUnavailableException("TMDB access token is not configured.");
        }

        if (tmdbOptions.AccountId is null)
        {
            throw new TmdbUnavailableException("TMDB account ID is not configured.");
        }

        if (string.IsNullOrWhiteSpace(tmdbOptions.SessionId))
        {
            throw new TmdbUnavailableException("TMDB session ID is not configured.");
        }

        List<TmdbTvWatchlistItemDto> allItems = [];
        int page = 1;
        int totalPages = 1;

        while (page <= totalPages)
        {
            string requestUri = $"/account/{tmdbOptions.AccountId}/watchlist/tv" +
                $"?language={tmdbOptions.Language}" +
                $"&page={page}" +
                $"&session_id={tmdbOptions.SessionId}" +
                $"&sort_by=created_at.desc";

            using HttpResponseMessage response = await GetAsync(requestUri, tmdbOptions.AccessToken, cancellationToken);
            EnsureSuccess(response);
            string content = await response.Content.ReadAsStringAsync(cancellationToken);

            try
            {
                TmdbWatchlistResponse? pageResponse = JsonSerializer.Deserialize<TmdbWatchlistResponse>(
                    content,
                    SerializerOptions);

                if (pageResponse?.Results is not null)
                {
                    foreach (TmdbWatchlistItemResponse item in pageResponse.Results)
                    {
                        allItems.Add(new TmdbTvWatchlistItemDto(
                            item.Id,
                            item.Name ?? string.Empty,
                            item.OriginalName ?? string.Empty,
                            NormalizeOptionalString(item.Overview),
                            NormalizeOptionalString(item.FirstAirDate),
                            NormalizeOptionalString(item.PosterPath),
                            NormalizeOptionalString(item.BackdropPath),
                            NormalizeOptionalString(item.OriginalLanguage),
                            item.VoteAverage,
                            item.VoteCount));
                    }
                }

                totalPages = pageResponse?.TotalPages ?? 1;
                page++;
            }
            catch (JsonException exception)
            {
                throw new TmdbParseException("TMDB TV watchlist response returned malformed JSON.", exception);
            }
        }

        return allItems;
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

    private sealed record TmdbWatchlistResponse(
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("total_pages")] int TotalPages,
        [property: JsonPropertyName("results")] IReadOnlyList<TmdbWatchlistItemResponse>? Results);

    private sealed record TmdbWatchlistItemResponse(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("original_name")] string? OriginalName,
        [property: JsonPropertyName("overview")] string? Overview,
        [property: JsonPropertyName("first_air_date")] string? FirstAirDate,
        [property: JsonPropertyName("poster_path")] string? PosterPath,
        [property: JsonPropertyName("backdrop_path")] string? BackdropPath,
        [property: JsonPropertyName("original_language")] string? OriginalLanguage,
        [property: JsonPropertyName("vote_average")] double? VoteAverage,
        [property: JsonPropertyName("vote_count")] int? VoteCount);
}
