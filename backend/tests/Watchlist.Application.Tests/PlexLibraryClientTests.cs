using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class PlexLibraryClientTests
{
    [Fact]
    public async Task GetSectionsAsync_WhenXmlValid_ParsesLibrarySections()
    {
        PlexLibraryClient client = CreateClient(new Dictionary<string, string>
        {
            ["/library/sections"] = """
            <MediaContainer size="2">
              <Directory key="1" type="movie" title="Filmy" />
              <Directory key="2" type="show" title="Seriale" />
            </MediaContainer>
            """
        });

        IReadOnlyList<PlexLibrarySectionDto> sections = await client.GetSectionsAsync(CancellationToken.None);

        sections.Should().Equal(
            new PlexLibrarySectionDto("1", "movie", "Filmy"),
            new PlexLibrarySectionDto("2", "show", "Seriale"));
    }

    [Fact]
    public async Task GetMoviesAsync_WhenXmlValid_ParsesMovieAndNestedGuids()
    {
        PlexLibrarySectionDto section = new("1", "movie", "Filmy");
        PlexLibraryClient client = CreateClient(new Dictionary<string, string>
        {
            ["/library/sections/1/all?type=1"] = """
            <MediaContainer size="1">
              <Video ratingKey="8058" title="10 Things I Hate About You" year="1999" guid="plex://movie/local" />
            </MediaContainer>
            """,
            ["/library/metadata/8058"] = """
            <MediaContainer size="1">
              <Video ratingKey="8058" title="10 Things I Hate About You" year="1999" guid="plex://movie/local">
                <Guid id="imdb://tt0147800" />
                <Guid id="tmdb://4951" />
                <Guid id="tvdb://836" />
              </Video>
            </MediaContainer>
            """
        });

        IReadOnlyList<PlexMovieDto> movies = await client.GetMoviesAsync(section, CancellationToken.None);

        movies.Should().ContainSingle().Which.Should().BeEquivalentTo(new PlexMovieDto(
            "8058",
            "10 Things I Hate About You",
            1999,
            "1",
            "Filmy",
            "plex://movie/local",
            "tt0147800",
            4951,
            836));
    }

    [Fact]
    public async Task GetSectionsAsync_WhenTokenMissing_ThrowsPlexUnavailable()
    {
        PlexLibraryClient client = CreateClient(
            new Dictionary<string, string>(),
            new PlexOptions { BaseUrl = "http://plex.local:32400", Token = "" });

        Func<Task> action = () => client.GetSectionsAsync(CancellationToken.None);

        await action.Should().ThrowAsync<PlexUnavailableException>()
            .WithMessage("Plex token is not configured.");
    }

    [Fact]
    public async Task GetSectionsAsync_WhenXmlMalformed_ThrowsPlexParse()
    {
        PlexLibraryClient client = CreateClient(new Dictionary<string, string>
        {
            ["/library/sections"] = "<MediaContainer>"
        });

        Func<Task> action = () => client.GetSectionsAsync(CancellationToken.None);

        await action.Should().ThrowAsync<PlexParseException>();
    }

    private static PlexLibraryClient CreateClient(
        IReadOnlyDictionary<string, string> responses,
        PlexOptions? options = null)
    {
        HttpClient httpClient = new(new StubHttpMessageHandler(responses))
        {
            BaseAddress = new Uri("http://plex.local:32400")
        };

        return new PlexLibraryClient(
            httpClient,
            Options.Create(options ?? new PlexOptions
            {
                BaseUrl = "http://plex.local:32400",
                Token = "token"
            }));
    }

    private sealed class StubHttpMessageHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.PathAndQuery
                .Replace("&X-Plex-Token=token", string.Empty, StringComparison.Ordinal)
                .Replace("?X-Plex-Token=token", string.Empty, StringComparison.Ordinal);

            if (!responses.TryGetValue(path, out string? content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
