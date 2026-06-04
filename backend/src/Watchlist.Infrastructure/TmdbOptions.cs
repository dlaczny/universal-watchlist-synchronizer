namespace Watchlist.Infrastructure;

public sealed class TmdbOptions
{
    public const string SectionName = "Tmdb";

    public string AccessToken { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.themoviedb.org/3";

    public string ImageBaseUrl { get; init; } = "https://image.tmdb.org/t/p";
}
