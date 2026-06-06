using System.Net;
using System.Text.Json;
using FluentAssertions;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Api.Tests;

public sealed class WatchlistApiTests
{
    [Fact]
    public async Task GetWatchlist_WhenDefaultQuery_ReturnsAllItemsWithAddedAt()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        JsonElement items = document.RootElement;
        items.GetArrayLength().Should().BeGreaterThan(0);
        items.EnumerateArray().Should().Contain(item => item.GetProperty("mediaType").GetString() == "movie");
        items.EnumerateArray().Should().Contain(item => item.GetProperty("mediaType").GetString() == "tv");
        items[0].TryGetProperty("addedAt", out _).Should().BeTrue();
        items[0].TryGetProperty("runtimeMinutes", out _).Should().BeFalse();
        items[0].TryGetProperty("primaryActionLabel", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetWatchlist_WhenCollectionTv_ReturnsTvShows()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist?collection=tv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        JsonElement items = document.RootElement;
        items.GetArrayLength().Should().BeGreaterThan(0);
        items.EnumerateArray().Should().OnlyContain(item => item.GetProperty("mediaType").GetString() == "tv");
    }

    [Fact]
    public async Task GetWatchlist_WhenAvailabilityPlex_ReturnsOnlyPlexAvailableItems()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist?availability=plex");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        JsonElement items = document.RootElement;
        items.GetArrayLength().Should().BeGreaterThan(0);
        items.EnumerateArray().Should().OnlyContain(item =>
            item.GetProperty("availabilityStatus").GetString() == "available_on_plex");
    }

    [Theory]
    [InlineData("/api/watchlist?collection=music", "Invalid collection.")]
    [InlineData("/api/watchlist?availability=plex,bad", "Invalid availability.")]
    [InlineData("/api/watchlist?availability=", "Invalid availability.")]
    [InlineData("/api/watchlist?sort=random", "Invalid sort.")]
    public async Task GetWatchlist_WhenQueryInvalid_ReturnsBadRequest(string url, string error)
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be(error);
    }

    [Fact]
    public async Task GetWatchlistItem_WhenItemExists_ReturnsItem()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist/movie-dune-part-two");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("title").GetString().Should().Be("Dune: Part Two");
        document.RootElement.GetProperty("posterUrl").GetString().Should().Be(
            "/api/images/tmdb/w500/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg");
        document.RootElement.GetProperty("backdropUrl").GetString().Should().Be(
            "/api/images/tmdb/w1280/xOMo8BRK7PfcJv9JCnx7s5hj0PX.jpg");
        document.RootElement.TryGetProperty("vodReleaseKnown", out JsonElement vodReleaseKnown).Should().BeTrue();
        vodReleaseKnown.GetBoolean().Should().BeFalse();
        document.RootElement.TryGetProperty("releasedOnVod", out JsonElement releasedOnVod).Should().BeTrue();
        releasedOnVod.GetBoolean().Should().BeFalse();
        document.RootElement.TryGetProperty("vodRegions", out JsonElement vodRegions).Should().BeTrue();
        vodRegions.GetArrayLength().Should().Be(0);
        document.RootElement.TryGetProperty("ownedServiceAvailability", out JsonElement providers).Should().BeTrue();
        providers.EnumerateArray().Select(provider => provider.GetString()).Should().Equal("Amazon Prime Video");
        document.RootElement.GetProperty("genres").EnumerateArray()
            .Select(genre => genre.GetString())
            .Should()
            .Equal("Science Fiction", "Adventure");
        document.RootElement.GetProperty("runtimeMinutes").GetInt32().Should().Be(166);
        document.RootElement.GetProperty("originalLanguage").GetString().Should().Be("en");
        document.RootElement.GetProperty("tmdbVoteAverage").GetDouble().Should().Be(8.1);
        document.RootElement.GetProperty("tmdbVoteCount").GetInt32().Should().BeGreaterThan(10);
        document.RootElement.GetProperty("primaryActionLabel").GetString().Should().Be("Open in Plex");
        document.RootElement.GetProperty("primaryActionEnabled").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("primaryActionTarget").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetWatchlistItem_WhenItemMissing_ReturnsNotFound()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist/missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSyncStatus_ReturnsSeededStatus()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/sync/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("seeded");
        document.RootElement.GetProperty("lastSuccessfulSyncAt").GetString().Should().Be("2026-05-25T10:00:00+02:00");
    }

    [Fact]
    public async Task PostLetterboxdSync_ReturnsSyncResult()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/letterboxd", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("completed");
        document.RootElement.GetProperty("itemsFetched").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("itemsUpserted").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task PostLetterboxdSync_WhenLetterboxdUnavailable_ReturnsServiceUnavailable()
    {
        using SeededApiFactory factory = new(new LetterboxdUnavailableException("unavailable"));
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/letterboxd", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be("Letterboxd watchlist is unavailable.");
    }

    [Fact]
    public async Task PostLetterboxdSync_WhenLetterboxdJsonMalformed_ReturnsBadGateway()
    {
        using SeededApiFactory factory = new(new LetterboxdParseException("malformed"));
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/letterboxd", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be(
            "Letterboxd watchlist returned malformed JSON.");
    }

    [Fact]
    public async Task PostTmdbMovieSync_ReturnsBatchResult()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/tmdb/movies", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("completed");
        document.RootElement.GetProperty("itemsMatched").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("itemsEnriched").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task PostTmdbSingleMovieSync_ReturnsSingleResult()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            "/api/sync/tmdb/movies/movie-letterboxd-1297842",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("enriched");
        document.RootElement.GetProperty("tmdbId").GetInt32().Should().Be(1297842);
    }

    [Fact]
    public async Task PostTmdbSingleMovieSync_WhenMissing_ReturnsNotFound()
    {
        using SeededApiFactory factory = new(tmdbSingleMovieReturnsNull: true);
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/tmdb/movies/missing", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostTmdbSingleMovieSync_WhenTmdbUnavailable_ReturnsServiceUnavailable()
    {
        using SeededApiFactory factory = new(
            tmdbSingleMovieSyncException: new TmdbUnavailableException("TMDB returned HTTP 503."));
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            "/api/sync/tmdb/movies/movie-letterboxd-1297842",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be("TMDB is unavailable.");
    }

    [Fact]
    public async Task PostPlexMovieSync_ReturnsSyncResult()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/plex/movies", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("completed");
        document.RootElement.GetProperty("sectionsScanned").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task PostPlexMovieSync_WhenPlexUnavailable_ReturnsServiceUnavailable()
    {
        using SeededApiFactory factory = new(
            plexMovieSyncException: new PlexUnavailableException("Plex token is not configured."));
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/plex/movies", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be("Plex is unavailable.");
    }

    [Fact]
    public async Task PostPlexMovieSync_WhenPlexXmlMalformed_ReturnsBadGateway()
    {
        using SeededApiFactory factory = new(
            plexMovieSyncException: new PlexParseException("Plex returned malformed XML."));
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/plex/movies", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be("Plex returned malformed XML.");
    }

    [Fact]
    public async Task PostAvailabilityRefresh_ReturnsRefreshResult()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/availability/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("completed");
        document.RootElement.GetProperty("ranPlexSync").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("reason").GetString().Should().Be("stale");
        document.RootElement.GetProperty("plex").GetProperty("watchlistItemsMatched").GetInt32().Should().Be(40);
    }

    [Fact]
    public async Task PostAvailabilityRefresh_WhenPlexUnavailable_ReturnsServiceUnavailable()
    {
        using SeededApiFactory factory = new(
            availabilityRefreshException: new PlexUnavailableException("Plex token is not configured."));
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/availability/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be("Plex is unavailable.");
    }

    [Fact]
    public async Task PostSyncAll_ReturnsCombinedResult()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("completed");
        document.RootElement.GetProperty("letterboxd").GetProperty("itemsFetched").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("plexMovies").GetProperty("watchlistItemsMatched").GetInt32().Should().Be(40);
    }

    [Fact]
    public async Task SyncTmdbTv_ReturnsTvSyncResult()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync("/api/sync/tmdb/tv", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("completed");
        document.RootElement.GetProperty("itemsFetched").GetInt32().Should().Be(2);
        document.RootElement.GetProperty("itemsUpserted").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetWatchlist_WhenMongoUnavailable_ReturnsServiceUnavailable()
    {
        using MongoUnavailableApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist?collection=movie");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be("MongoDB is unavailable.");
    }

    [Fact]
    public async Task GetRadarrMovieExport_ReturnsLetterboxdProxyShapeAndExcludesOwnedVodMovies()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/export/radarr/movies");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        JsonElement items = document.RootElement;
        items.GetArrayLength().Should().Be(1);
        JsonElement item = items[0];
        item.GetProperty("id").GetInt32().Should().Be(1297842);
        item.GetProperty("imdb_id").GetString().Should().Be("tt27613895");
        item.GetProperty("title").GetString().Should().Be("GOAT");
        item.GetProperty("release_year").GetString().Should().Be("2026");
        item.GetProperty("clean_title").GetString().Should().Be("/film/goat-2026/");
        item.GetProperty("adult").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetRadarrMovieExport_WhenMongoUnavailable_ReturnsServiceUnavailable()
    {
        using MongoUnavailableApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/export/radarr/movies");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be("MongoDB is unavailable.");
    }

    [Fact]
    public async Task GetSonarrTvExport_ForV1_ReturnsEmptyArray()
    {
        using SeededApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/export/sonarr/tv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetArrayLength().Should().Be(0);
    }

    private static async Task<JsonDocument> ReadJsonDocumentAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }
}
