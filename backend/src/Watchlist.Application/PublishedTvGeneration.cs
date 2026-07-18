using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Couples an immutable manifest with only the rows selected by its published pointer.
/// </summary>
public sealed record PublishedTvGeneration(
    TvGenerationManifest Manifest,
    IReadOnlyList<TvShow> Shows)
{
    private IReadOnlyList<TvShow> _shows = Snapshot(Shows);

    public IReadOnlyList<TvShow> Shows
    {
        get => _shows;
        init => _shows = Snapshot(value);
    }

    private static IReadOnlyList<TvShow> Snapshot(IReadOnlyList<TvShow> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
