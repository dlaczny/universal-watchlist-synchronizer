using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Watchlist.Api.Tests;

public sealed class WatchlistApiTests
{
    [Fact]
    public async Task GetWatchlist_WhenMoviesAll_ReturnsMovies()
    {
        using WebApplicationFactory<Program> factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist?mediaType=movie&filter=all");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        JsonElement items = document.RootElement;
        items.GetArrayLength().Should().BeGreaterThan(0);
        items.EnumerateArray().Should().OnlyContain(item => item.GetProperty("mediaType").GetString() == "movie");
        items.EnumerateArray().Should().NotContain(item => item.GetProperty("mediaType").GetString() == "tv");
    }

    [Fact]
    public async Task GetWatchlist_WhenMoviesAvailable_ReturnsOnlyPlexAvailableMovies()
    {
        using WebApplicationFactory<Program> factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist?mediaType=movie&filter=available");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        JsonElement items = document.RootElement;
        items.GetArrayLength().Should().BeGreaterThan(0);
        items.EnumerateArray().Should().OnlyContain(item =>
            item.GetProperty("mediaType").GetString() == "movie"
            && item.GetProperty("availabilityStatus").GetString() == "available_on_plex");
    }

    [Fact]
    public async Task GetWatchlist_WhenMediaTypeInvalid_ReturnsBadRequest()
    {
        using WebApplicationFactory<Program> factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist?mediaType=music&filter=all");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("error").GetString().Should().Be("Invalid mediaType.");
    }

    [Fact]
    public async Task GetWatchlistItem_WhenItemExists_ReturnsItem()
    {
        using WebApplicationFactory<Program> factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist/movie-dune-part-two");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("title").GetString().Should().Be("Dune: Part Two");
    }

    [Fact]
    public async Task GetWatchlistItem_WhenItemMissing_ReturnsNotFound()
    {
        using WebApplicationFactory<Program> factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watchlist/missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSyncStatus_ReturnsSeededStatus()
    {
        using WebApplicationFactory<Program> factory = new();
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/sync/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument document = await ReadJsonDocumentAsync(response);
        document.RootElement.GetProperty("status").GetString().Should().Be("seeded");
        document.RootElement.GetProperty("lastSuccessfulSyncAt").GetString().Should().Be("2026-05-25T10:00:00+02:00");
    }

    private static async Task<JsonDocument> ReadJsonDocumentAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }
}
