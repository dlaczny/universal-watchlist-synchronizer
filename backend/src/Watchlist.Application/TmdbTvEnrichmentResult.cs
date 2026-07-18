using System.Collections.ObjectModel;
using Watchlist.Domain;

namespace Watchlist.Application;

public sealed record TmdbTvEnrichmentResult(
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    TvIdentityStatus IdentityStatus,
    string Title,
    int? Year,
    string? Overview,
    string? PosterUrl,
    string? BackdropUrl,
    DateTimeOffset MetadataFetchedAt,
    TvProviderAvailability Availability,
    IReadOnlyDictionary<int, TvProviderAvailability> SeasonAvailability,
    IReadOnlyList<string> Errors)
{
    private IReadOnlyDictionary<int, TvProviderAvailability> _seasonAvailability =
        SnapshotAvailability(SeasonAvailability);
    private IReadOnlyList<string> _errors = SnapshotErrors(Errors);

    public IReadOnlyDictionary<int, TvProviderAvailability> SeasonAvailability
    {
        get => _seasonAvailability;
        init => _seasonAvailability = SnapshotAvailability(value);
    }

    public IReadOnlyList<string> Errors
    {
        get => _errors;
        init => _errors = SnapshotErrors(value);
    }

    private static IReadOnlyDictionary<int, TvProviderAvailability> SnapshotAvailability(
        IReadOnlyDictionary<int, TvProviderAvailability> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Dictionary<int, TvProviderAvailability> snapshot = new();
        foreach (KeyValuePair<int, TvProviderAvailability> entry in values)
        {
            if (entry.Key <= 0 || entry.Value is null || !snapshot.TryAdd(entry.Key, entry.Value))
            {
                throw new ArgumentException("Season availability must use unique positive season numbers.", nameof(SeasonAvailability));
            }
        }

        return new ReadOnlyDictionary<int, TvProviderAvailability>(snapshot);
    }

    private static IReadOnlyList<string> SnapshotErrors(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        string[] snapshot = values.ToArray();
        if (snapshot.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Enrichment errors must be stable non-empty values.", nameof(Errors));
        }

        return Array.AsReadOnly(snapshot);
    }
}
