namespace Watchlist.Infrastructure;

public sealed class PlexOptions
{
    public const string SectionName = "Plex";

    public string BaseUrl { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;
}
