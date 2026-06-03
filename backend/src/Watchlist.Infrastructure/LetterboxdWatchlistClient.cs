using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class LetterboxdUnavailableException(string message) : Exception(message);

public sealed class LetterboxdParseException : Exception
{
    public LetterboxdParseException(string message)
        : base(message)
    {
    }

    public LetterboxdParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class LetterboxdWatchlistClient(
    HttpClient httpClient,
    IOptions<LetterboxdOptions> options) : ILetterboxdWatchlistClient
{
    public async Task<IReadOnlyList<LetterboxdMovieDto>> GetMoviesAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            options.Value.WatchlistUrl,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable
            || !response.IsSuccessStatusCode)
        {
            throw new LetterboxdUnavailableException(
                $"Letterboxd watchlist proxy returned HTTP {(int)response.StatusCode}.");
        }

        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            SourceMovie[]? movies = JsonSerializer.Deserialize<SourceMovie[]>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (movies is null)
            {
                return [];
            }

            foreach (SourceMovie movie in movies)
            {
                Validate(movie);
            }

            return movies.Select(ToDto).ToList();
        }
        catch (JsonException exception)
        {
            throw new LetterboxdParseException("Letterboxd watchlist proxy returned malformed JSON.", exception);
        }
    }

    private static LetterboxdMovieDto ToDto(SourceMovie movie)
    {
        return new LetterboxdMovieDto(
            movie.Id.ToString(),
            string.IsNullOrWhiteSpace(movie.ImdbId) ? null : movie.ImdbId,
            movie.Title,
            int.TryParse(movie.ReleaseYear, out int releaseYear) ? releaseYear : null,
            string.IsNullOrWhiteSpace(movie.CleanTitle) ? null : movie.CleanTitle);
    }

    private static void Validate(SourceMovie movie)
    {
        if (movie.Id <= 0 || string.IsNullOrWhiteSpace(movie.Title))
        {
            throw new LetterboxdParseException("Letterboxd watchlist proxy returned an invalid movie item.");
        }
    }

    private sealed record SourceMovie(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("imdb_id")] string? ImdbId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("release_year")] string? ReleaseYear,
        [property: JsonPropertyName("clean_title")] string? CleanTitle);
}
