namespace Watchlist.Infrastructure;

public sealed class LetterboxdOptions
{
    public const string SectionName = "Letterboxd";

    public string WatchlistUrl { get; init; } = string.Empty;
}
