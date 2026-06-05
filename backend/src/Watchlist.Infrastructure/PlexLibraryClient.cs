using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class PlexLibraryClient(
    HttpClient httpClient,
    IOptions<PlexOptions> options) : IPlexLibraryClient
{
    public async Task<IReadOnlyList<PlexLibrarySectionDto>> GetSectionsAsync(CancellationToken cancellationToken)
    {
        XDocument document = await GetXmlAsync("/library/sections", cancellationToken);

        return document.Root?
            .Elements("Directory")
            .Select(element => new PlexLibrarySectionDto(
                RequiredAttribute(element, "key"),
                RequiredAttribute(element, "type"),
                RequiredAttribute(element, "title")))
            .ToList()
            ?? throw new PlexParseException("Plex library sections response is missing MediaContainer.");
    }

    public async Task<IReadOnlyList<PlexMovieDto>> GetMoviesAsync(
        PlexLibrarySectionDto section,
        CancellationToken cancellationToken)
    {
        XDocument listing = await GetXmlAsync($"/library/sections/{Uri.EscapeDataString(section.Key)}/all?type=1", cancellationToken);
        List<XElement> videos = listing.Root?.Elements("Video").ToList()
            ?? throw new PlexParseException("Plex movie section response is missing MediaContainer.");

        List<PlexMovieDto> movies = [];
        foreach (XElement video in videos)
        {
            string ratingKey = RequiredAttribute(video, "ratingKey");
            XDocument metadata = await GetXmlAsync($"/library/metadata/{Uri.EscapeDataString(ratingKey)}", cancellationToken);
            XElement metadataVideo = metadata.Root?.Element("Video")
                ?? throw new PlexParseException($"Plex metadata response for ratingKey '{ratingKey}' is missing Video.");

            movies.Add(ToMovie(section, metadataVideo));
        }

        return movies;
    }

    private async Task<XDocument> GetXmlAsync(string path, CancellationToken cancellationToken)
    {
        PlexOptions plexOptions = options.Value;
        if (string.IsNullOrWhiteSpace(plexOptions.Token))
        {
            throw new PlexUnavailableException("Plex token is not configured.");
        }

        string separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        string requestPath = $"{path}{separator}X-Plex-Token={Uri.EscapeDataString(plexOptions.Token)}";

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(requestPath, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new PlexUnavailableException("Plex could not be reached.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PlexUnavailableException("Plex request timed out.", exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new PlexUnavailableException($"Plex returned HTTP {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new PlexUnavailableException($"Plex returned HTTP {(int)response.StatusCode}.");
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                return XDocument.Parse(content);
            }
            catch (Exception exception) when (exception is System.Xml.XmlException)
            {
                throw new PlexParseException("Plex returned malformed XML.", exception);
            }
        }
    }

    private static PlexMovieDto ToMovie(PlexLibrarySectionDto section, XElement video)
    {
        string? imdbId = null;
        int? tmdbId = null;
        int? tvdbId = null;

        foreach (XElement guid in video.Elements("Guid"))
        {
            string? id = guid.Attribute("id")?.Value;
            if (id is null)
            {
                continue;
            }

            if (id.StartsWith("imdb://", StringComparison.OrdinalIgnoreCase))
            {
                imdbId = id["imdb://".Length..];
            }
            else if (id.StartsWith("tmdb://", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(id["tmdb://".Length..], out int parsedTmdbId))
            {
                tmdbId = parsedTmdbId;
            }
            else if (id.StartsWith("tvdb://", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(id["tvdb://".Length..], out int parsedTvdbId))
            {
                tvdbId = parsedTvdbId;
            }
        }

        return new PlexMovieDto(
            RequiredAttribute(video, "ratingKey"),
            RequiredAttribute(video, "title"),
            OptionalIntAttribute(video, "year"),
            section.Key,
            section.Title,
            video.Attribute("guid")?.Value,
            imdbId,
            tmdbId,
            tvdbId);
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        return element.Attribute(name)?.Value
            ?? throw new PlexParseException($"Plex XML is missing required '{name}' attribute.");
    }

    private static int? OptionalIntAttribute(XElement element, string name)
    {
        string? value = element.Attribute(name)?.Value;
        return int.TryParse(value, out int parsed) ? parsed : null;
    }
}
