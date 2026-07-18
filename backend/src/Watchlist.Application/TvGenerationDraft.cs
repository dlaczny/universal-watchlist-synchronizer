using System.Collections.ObjectModel;
using Watchlist.Domain;

namespace Watchlist.Application;

/// <summary>
/// Contains a complete, unpublished TV generation and its source evidence.
/// </summary>
public sealed record TvGenerationDraft(
    string GenerationId,
    TvGenerationKind Kind,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    TraktActivityCursor ActivityBefore,
    TraktActivityCursor ActivityAfter,
    int WatchlistPageCount,
    int WatchlistItemCount,
    int ProgressPageCount,
    int ProgressItemCount,
    string RequestContractVersion,
    IReadOnlyDictionary<string, string> RequestFilters,
    string MembershipHash,
    string ProgressHash,
    IReadOnlyList<TvShow> Shows,
    IReadOnlyList<TvLifecycleEvent> LifecycleEvents,
    IReadOnlyList<string> EnrichmentErrors)
{
    private IReadOnlyDictionary<string, string> _requestFilters = Snapshot(RequestFilters);
    private IReadOnlyList<TvShow> _shows = Snapshot(Shows);
    private IReadOnlyList<TvLifecycleEvent> _lifecycleEvents = Snapshot(LifecycleEvents);
    private IReadOnlyList<string> _enrichmentErrors = Snapshot(EnrichmentErrors);

    public IReadOnlyDictionary<string, string> RequestFilters
    {
        get => _requestFilters;
        init => _requestFilters = Snapshot(value);
    }

    public IReadOnlyList<TvShow> Shows
    {
        get => _shows;
        init => _shows = Snapshot(value);
    }

    public IReadOnlyList<TvLifecycleEvent> LifecycleEvents
    {
        get => _lifecycleEvents;
        init => _lifecycleEvents = Snapshot(value);
    }

    public IReadOnlyList<string> EnrichmentErrors
    {
        get => _enrichmentErrors;
        init => _enrichmentErrors = Snapshot(value);
    }

    private static IReadOnlyDictionary<string, string> Snapshot(
        IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        SortedDictionary<string, string> snapshot = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> value in values)
        {
            snapshot.Add(value.Key, value.Value);
        }

        return new ReadOnlyDictionary<string, string>(snapshot);
    }

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Array.AsReadOnly(values.ToArray());
    }
}
