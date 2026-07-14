namespace Watchlist.Infrastructure;

public sealed class TmdbOptions
{
    private IReadOnlyList<int> _ownedProviderIds = SnapshotProviderIds([119, 1899, 1773]);

    public const string SectionName = "Tmdb";

    public string AccessToken { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.themoviedb.org/3";

    public string ImageBaseUrl { get; init; } = "https://image.tmdb.org/t/p";

    public int? AccountId { get; init; }

    public string SessionId { get; init; } = string.Empty;

    public string Language { get; init; } = "en-US";

    public string ProviderRegion { get; init; } = "PL";

    public IReadOnlyList<int> OwnedProviderIds
    {
        get => _ownedProviderIds;
        set => _ownedProviderIds = SnapshotProviderIds(value);
    }

    public TimeSpan ProviderCacheLifetime { get; init; } = TimeSpan.FromHours(24);

    private static IReadOnlyList<int> SnapshotProviderIds(IReadOnlyList<int> values)
    {
        return Array.AsReadOnly(values.ToArray());
    }
}
