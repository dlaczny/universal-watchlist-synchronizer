using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

/// <summary>
/// Reads complete authenticated TV source state from Trakt, resuming only explicit rate-limited reads.
/// </summary>
public sealed class TraktTvClient(
    IHttpClientFactory httpClientFactory,
    IOptions<TraktOptions> options,
    TimeProvider? timeProvider = null) : ITraktTvClient
{
    public const string HttpClientName = "TraktTv";

    internal const int MaximumPageSize = 100;

    private const string UserAgent = "WatchlistApp/1.0 (+https://github.com/dlaczny/universal-watchlist-synchronizer)";

    // At the supported page size this still permits a complete 100,000-row source catalog.
    private const int MaximumPageCount = 1_000;

    // Bound a single full read while still allowing a large catalog to resume after Trakt's cooldown.
    private const int MaximumRateLimitRetries = 6;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<TraktActivityCursor> GetLastActivitiesAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpClient httpClient = CreateHttpClient();
        using HttpResponseMessage response = await SendAsync(
            httpClient,
            "/sync/last_activities",
            accessToken,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        ActivitiesResponse result = await ReadJsonAsync<ActivitiesResponse>(
            response,
            cancellationToken).ConfigureAwait(false);

        if (result.Shows?.WatchlistedAt is not DateTimeOffset showWatchlistedAt
            || result.Episodes?.WatchedAt is not DateTimeOffset episodeWatchedAt)
        {
            throw new TraktParseException();
        }

        return new TraktActivityCursor(
            showWatchlistedAt.ToUniversalTime(),
            episodeWatchedAt.ToUniversalTime());
    }

    public async Task<TraktPagedResult<TraktWatchlistShow>> GetWatchlistAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        int pageSize = GetPageSize();
        using HttpClient httpClient = CreateHttpClient();
        return await ReadPaginatedAsync<WatchlistItemResponse, TraktWatchlistShow>(
            httpClient,
            accessToken,
            page => $"/sync/watchlist/shows/added/asc?page={page}&limit={pageSize}",
            MapWatchlistShow,
            item => item.Ids.TraktId,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TraktPagedResult<TraktWatchedShowProgress>> GetWatchedProgressAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        int pageSize = GetPageSize();
        using HttpClient httpClient = CreateHttpClient();
        return await ReadPaginatedAsync<WatchedProgressResponse, TraktWatchedShowProgress>(
            httpClient,
            accessToken,
            page => "/sync/progress/watched" +
                "?hide_completed=false" +
                "&hide_not_completed=false" +
                "&only_rewatching=false" +
                $"&page={page}" +
                $"&limit={pageSize}",
            MapWatchedProgress,
            item => item.Ids.TraktId,
            pageSize,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TraktDetailedShowProgress> GetDetailedProgressAsync(
        string accessToken,
        long traktId,
        CancellationToken cancellationToken)
    {
        EnsurePositiveTraktId(traktId);
        using HttpClient httpClient = CreateHttpClient();
        using HttpResponseMessage response = await SendAsync(
            httpClient,
            $"/shows/{traktId}/progress/watched?hidden=false&specials=false&count_specials=false",
            accessToken,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        DetailedProgressResponse result = await ReadJsonAsync<DetailedProgressResponse>(
            response,
            cancellationToken).ConfigureAwait(false);
        ValidateTotals(result.Aired, result.Completed);
        if (result.Seasons is null)
        {
            throw new TraktParseException();
        }

        HashSet<int> seasonNumbers = [];
        List<TraktDetailedSeasonProgress> seasons = [];
        foreach (DetailedSeasonResponse? sourceSeason in result.Seasons)
        {
            if (sourceSeason is null
                || sourceSeason.Number is not int seasonNumber
                || seasonNumber <= 0
                || !seasonNumbers.Add(seasonNumber))
            {
                throw new TraktParseException();
            }

            ValidateTotals(sourceSeason.Aired, sourceSeason.Completed);
            if (sourceSeason.Episodes is null)
            {
                throw new TraktParseException();
            }

            HashSet<int> episodeNumbers = [];
            List<TraktDetailedEpisodeProgress> episodes = [];
            foreach (DetailedEpisodeResponse? sourceEpisode in sourceSeason.Episodes)
            {
                if (sourceEpisode is null
                    || sourceEpisode.Number is not int episodeNumber
                    || episodeNumber <= 0
                    || sourceEpisode.Completed is not bool completed
                    || !episodeNumbers.Add(episodeNumber))
                {
                    throw new TraktParseException();
                }

                episodes.Add(new TraktDetailedEpisodeProgress(
                    seasonNumber,
                    episodeNumber,
                    completed,
                    sourceEpisode.LastWatchedAt?.ToUniversalTime()));
            }

            seasons.Add(new TraktDetailedSeasonProgress(
                seasonNumber,
                sourceSeason.Aired!.Value,
                sourceSeason.Completed!.Value,
                episodes.OrderBy(episode => episode.EpisodeNumber).ToArray()));
        }

        return new TraktDetailedShowProgress(
            result.Aired!.Value,
            result.Completed!.Value,
            seasons.OrderBy(season => season.SeasonNumber).ToArray());
    }

    public async Task<TraktShowMetadata> GetShowMetadataAsync(
        string accessToken,
        long traktId,
        CancellationToken cancellationToken)
    {
        EnsurePositiveTraktId(traktId);
        using HttpClient httpClient = CreateHttpClient();
        using HttpResponseMessage response = await SendAsync(
            httpClient,
            $"/shows/{traktId}?extended=full",
            accessToken,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        ShowResponse result = await ReadJsonAsync<ShowResponse>(
            response,
            cancellationToken).ConfigureAwait(false);
        TraktShowIds ids = MapShowIds(result.Ids);
        if (ids.TraktId != traktId)
        {
            throw new TraktParseException();
        }

        return new TraktShowMetadata(
            ids,
            NormalizeRequired(result.Title),
            ValidateYear(result.Year),
            NormalizeOptional(result.Overview),
            result.Status);
    }

    public async Task<IReadOnlyList<TraktSeasonEpisode>> GetSeasonAsync(
        string accessToken,
        long traktId,
        int seasonNumber,
        CancellationToken cancellationToken)
    {
        EnsurePositiveTraktId(traktId);
        if (seasonNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seasonNumber));
        }

        using HttpClient httpClient = CreateHttpClient();
        using HttpResponseMessage response = await SendAsync(
            httpClient,
            $"/shows/{traktId}/seasons/{seasonNumber}?extended=full",
            accessToken,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        IReadOnlyList<EpisodeResponse?> result = await ReadJsonAsync<List<EpisodeResponse?>>(
            response,
            cancellationToken).ConfigureAwait(false);

        HashSet<int> episodeNumbers = [];
        HashSet<long> traktEpisodeIds = [];
        List<TraktSeasonEpisode> episodes = [];
        foreach (EpisodeResponse? sourceEpisode in result)
        {
            if (sourceEpisode is null)
            {
                throw new TraktParseException();
            }

            TraktSeasonEpisode episode = MapEpisode(sourceEpisode);
            if (episode.SeasonNumber != seasonNumber
                || !episodeNumbers.Add(episode.EpisodeNumber)
                || !traktEpisodeIds.Add(episode.TraktEpisodeId))
            {
                throw new TraktParseException();
            }

            episodes.Add(episode);
        }

        TraktSeasonEpisode[] orderedEpisodes = episodes
            .OrderBy(episode => episode.EpisodeNumber)
            .ThenBy(episode => episode.TraktEpisodeId)
            .ToArray();
        return Array.AsReadOnly(orderedEpisodes);
    }

    private async Task<TraktPagedResult<TOutput>> ReadPaginatedAsync<TResponse, TOutput>(
        HttpClient httpClient,
        string accessToken,
        Func<int, string> requestPath,
        Func<TResponse, TOutput> map,
        Func<TOutput, long> identity,
        int pageSize,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        List<TOutput> results = [];
        HashSet<long> identities = [];
        int page = 1;
        int? expectedPageCount = null;

        while (expectedPageCount is null || page <= expectedPageCount.Value)
        {
            using HttpResponseMessage response = await SendAsync(
                httpClient,
                requestPath(page),
                accessToken,
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response);
            IReadOnlyList<TResponse?> pageItems = await ReadJsonAsync<List<TResponse?>>(
                response,
                cancellationToken).ConfigureAwait(false);
            int pageCount = ReadPageCount(response, pageItems.Count, page, expectedPageCount);
            if (expectedPageCount is null)
            {
                expectedPageCount = pageCount;
            }
            else if (expectedPageCount.Value != pageCount)
            {
                throw new TraktParseException();
            }

            foreach (TResponse? pageItem in pageItems)
            {
                if (pageItem is null)
                {
                    throw new TraktParseException();
                }

                TOutput mapped = map(pageItem);
                long mappedIdentity = identity(mapped);
                if (mappedIdentity <= 0 || !identities.Add(mappedIdentity))
                {
                    throw new TraktParseException();
                }

                results.Add(mapped);
            }

            try
            {
                page = checked(page + 1);
            }
            catch (OverflowException)
            {
                throw new TraktParseException();
            }
        }

        TOutput[] orderedResults = results.OrderBy(identity).ToArray();
        return new TraktPagedResult<TOutput>(
            expectedPageCount ?? 1,
            pageSize,
            orderedResults);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        string requestPath,
        string accessToken,
        CancellationToken cancellationToken)
    {
        string normalizedAccessToken = NormalizeCredential(accessToken);
        string clientId = NormalizeCredential(options.Value.ClientId);
        for (int retryCount = 0; ; retryCount++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, requestPath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedAccessToken);
            request.Headers.TryAddWithoutValidation("trakt-api-version", "2");
            request.Headers.TryAddWithoutValidation("trakt-api-key", clientId);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                throw new TraktUnavailableException();
            }
            catch (IOException)
            {
                throw new TraktUnavailableException();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TraktUnavailableException();
            }

            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                return response;
            }

            TimeSpan? retryAfter = ReadRetryAfter(response.Headers.RetryAfter);
            response.Dispose();
            if (retryAfter is null || retryCount >= MaximumRateLimitRetries)
            {
                throw new TraktRateLimitedException(retryAfter);
            }

            await Task.Delay(retryAfter.Value, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<TResponse> ReadJsonAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(
                cancellationToken).ConfigureAwait(false);
            TResponse? result = await JsonSerializer.DeserializeAsync<TResponse>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            return result ?? throw new TraktParseException();
        }
        catch (JsonException)
        {
            throw new TraktParseException();
        }
        catch (NotSupportedException)
        {
            throw new TraktParseException();
        }
        catch (HttpRequestException)
        {
            throw new TraktUnavailableException();
        }
        catch (IOException)
        {
            throw new TraktUnavailableException();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TraktUnavailableException();
        }
    }

    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new TraktRateLimitedException(ReadRetryAfter(response.Headers.RetryAfter));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new TraktUnavailableException();
        }
    }

    private TimeSpan? ReadRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is TimeSpan delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter?.Date is not DateTimeOffset date)
        {
            return null;
        }

        TimeSpan delay = date - _timeProvider.GetUtcNow();
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static int ReadPageCount(
        HttpResponseMessage response,
        int itemCount,
        int page,
        int? expectedPageCount)
    {
        if (!response.Headers.TryGetValues("X-Pagination-Page-Count", out IEnumerable<string>? values))
        {
            if (itemCount == 0 && page == 1 && expectedPageCount is null)
            {
                return 1;
            }

            throw new TraktParseException();
        }

        string[] pageCountValues = values.ToArray();
        if (pageCountValues.Length != 1
            || !int.TryParse(
                pageCountValues[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int pageCount)
            || pageCount < 0
            || pageCount > MaximumPageCount
            || (itemCount > 0 && pageCount == 0))
        {
            throw new TraktParseException();
        }

        return pageCount == 0 ? 1 : pageCount;
    }

    private static TraktWatchlistShow MapWatchlistShow(WatchlistItemResponse item)
    {
        if (item.Show is null || item.ListedAt is not DateTimeOffset listedAt)
        {
            throw new TraktParseException();
        }

        return new TraktWatchlistShow(
            MapShowIds(item.Show.Ids),
            NormalizeRequired(item.Show.Title),
            ValidateYear(item.Show.Year),
            listedAt.ToUniversalTime());
    }

    private static TraktWatchedShowProgress MapWatchedProgress(WatchedProgressResponse item)
    {
        if (item.Show is null || item.Progress is null)
        {
            throw new TraktParseException();
        }

        ValidateTotals(item.Progress.Aired, item.Progress.Completed);
        return new TraktWatchedShowProgress(
            MapShowIds(item.Show.Ids),
            NormalizeRequired(item.Show.Title),
            ValidateYear(item.Show.Year),
            item.Progress.Aired!.Value,
            item.Progress.Completed!.Value,
            item.Progress.NextEpisode is null ? null : MapProgressEpisode(item.Progress.NextEpisode),
            item.Progress.LastEpisode is null ? null : MapProgressEpisode(item.Progress.LastEpisode));
    }

    private static TraktShowIds MapShowIds(ShowIdsResponse? ids)
    {
        if (ids?.Trakt is not long traktId || traktId <= 0)
        {
            throw new TraktParseException();
        }

        ValidateOptionalPositive(ids.Tvdb);
        ValidateOptionalPositive(ids.Tmdb);
        return new TraktShowIds(
            traktId,
            ids.Tvdb,
            ids.Tmdb,
            NormalizeOptional(ids.Imdb)?.ToLowerInvariant());
    }

    private static TraktSeasonEpisode MapEpisode(EpisodeResponse episode)
    {
        if (episode.Season is not int seasonNumber
            || seasonNumber < 0
            || episode.Number is not int episodeNumber
            || episodeNumber <= 0
            || episode.Ids?.Trakt is not long traktEpisodeId
            || traktEpisodeId <= 0)
        {
            throw new TraktParseException();
        }

        int? tvdbId = episode.Ids.Tvdb switch
        {
            null or 0 => null,
            > 0 => episode.Ids.Tvdb,
            _ => throw new TraktParseException()
        };
        return new TraktSeasonEpisode(
            traktEpisodeId,
            tvdbId,
            seasonNumber,
            episodeNumber,
            NormalizeOptional(episode.Title),
            episode.FirstAired?.ToUniversalTime());
    }

    private static TraktSeasonEpisode MapProgressEpisode(EpisodeResponse episode)
    {
        TraktSeasonEpisode mapped = MapEpisode(episode);
        if (mapped.SeasonNumber == 0)
        {
            throw new TraktParseException();
        }

        return mapped;
    }

    private static void ValidateTotals(int? aired, int? completed)
    {
        if (aired is not int airedValue
            || completed is not int completedValue
            || airedValue < 0
            || completedValue < 0
            || completedValue > airedValue)
        {
            throw new TraktParseException();
        }
    }

    private static void ValidateOptionalPositive(int? value)
    {
        if (value is <= 0)
        {
            throw new TraktParseException();
        }
    }

    private static int? ValidateYear(int? year)
    {
        if (year is <= 0)
        {
            throw new TraktParseException();
        }

        return year;
    }

    private static string NormalizeRequired(string? value)
    {
        return NormalizeOptional(value) ?? throw new TraktParseException();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeCredential(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new TraktUnavailableException();
        }

        return value.Trim();
    }

    private static void EnsurePositiveTraktId(long traktId)
    {
        if (traktId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(traktId));
        }
    }

    private HttpClient CreateHttpClient()
    {
        return httpClientFactory.CreateClient(HttpClientName);
    }

    private int GetPageSize()
    {
        int pageSize = options.Value.PageSize;
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new TraktUnavailableException();
        }

        return pageSize;
    }

    private sealed record ActivitiesResponse(
        [property: JsonPropertyName("episodes")] EpisodeActivitiesResponse? Episodes,
        [property: JsonPropertyName("shows")] ShowActivitiesResponse? Shows);

    private sealed record EpisodeActivitiesResponse(
        [property: JsonPropertyName("watched_at")] DateTimeOffset? WatchedAt);

    private sealed record ShowActivitiesResponse(
        [property: JsonPropertyName("watchlisted_at")] DateTimeOffset? WatchlistedAt);

    private sealed record WatchlistItemResponse(
        [property: JsonPropertyName("listed_at")] DateTimeOffset? ListedAt,
        [property: JsonPropertyName("show")] ShowResponse? Show);

    private sealed record WatchedProgressResponse(
        [property: JsonPropertyName("show")] ShowResponse? Show,
        [property: JsonPropertyName("progress")] ProgressResponse? Progress);

    private sealed record ProgressResponse(
        [property: JsonPropertyName("aired")] int? Aired,
        [property: JsonPropertyName("completed")] int? Completed,
        [property: JsonPropertyName("next_episode"), JsonRequired] EpisodeResponse? NextEpisode,
        [property: JsonPropertyName("last_episode"), JsonRequired] EpisodeResponse? LastEpisode);

    private sealed record DetailedProgressResponse(
        [property: JsonPropertyName("aired")] int? Aired,
        [property: JsonPropertyName("completed")] int? Completed,
        [property: JsonPropertyName("seasons")] IReadOnlyList<DetailedSeasonResponse?>? Seasons);

    private sealed record DetailedSeasonResponse(
        [property: JsonPropertyName("number")] int? Number,
        [property: JsonPropertyName("aired")] int? Aired,
        [property: JsonPropertyName("completed")] int? Completed,
        [property: JsonPropertyName("episodes")] IReadOnlyList<DetailedEpisodeResponse?>? Episodes);

    private sealed record DetailedEpisodeResponse(
        [property: JsonPropertyName("number")] int? Number,
        [property: JsonPropertyName("completed")] bool? Completed,
        [property: JsonPropertyName("last_watched_at")] DateTimeOffset? LastWatchedAt);

    private sealed record ShowResponse(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("year")] int? Year,
        [property: JsonPropertyName("overview")] string? Overview,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("ids")] ShowIdsResponse? Ids);

    private sealed record ShowIdsResponse(
        [property: JsonPropertyName("trakt")] long? Trakt,
        [property: JsonPropertyName("tvdb")] int? Tvdb,
        [property: JsonPropertyName("tmdb")] int? Tmdb,
        [property: JsonPropertyName("imdb")] string? Imdb);

    private sealed record EpisodeResponse(
        [property: JsonPropertyName("season")] int? Season,
        [property: JsonPropertyName("number")] int? Number,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("first_aired")] DateTimeOffset? FirstAired,
        [property: JsonPropertyName("ids")] EpisodeIdsResponse? Ids);

    private sealed record EpisodeIdsResponse(
        [property: JsonPropertyName("trakt")] long? Trakt,
        [property: JsonPropertyName("tvdb")] int? Tvdb);
}
