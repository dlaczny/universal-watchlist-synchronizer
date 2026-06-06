using System.Text.Json.Serialization;

namespace Watchlist.Application;

public sealed record RadarrMovieExportItemDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("imdb_id")] string ImdbId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("release_year")] string ReleaseYear,
    [property: JsonPropertyName("clean_title")] string CleanTitle,
    [property: JsonPropertyName("adult")] bool Adult);
