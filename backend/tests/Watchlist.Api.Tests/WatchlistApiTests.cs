using System.Net;
using System.Text.Json;
using FluentAssertions;
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
    public async Task GetWatchlist_WhenMongoUnavailable_ReturnsServiceUnavailable()
    {
        using MongoUnavailableApiFactory factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist?collection=movie");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be("MongoDB is unavailable.");
    }

    private static async Task<JsonDocument> ReadJsonDocumentAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }
}
