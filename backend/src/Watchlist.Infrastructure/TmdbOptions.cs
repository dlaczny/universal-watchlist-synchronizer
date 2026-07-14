namespace Watchlist.Infrastructure;

public sealed class TmdbOptions
{
    private static readonly int[] s_defaultOwnedProviderIds = [119, 1899, 1773];
    private IReadOnlyList<int> _ownedProviderIds = [.. s_defaultOwnedProviderIds];

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
        init => _ownedProviderIds = ReplaceBinderAppendedDefaults(value);
    }

    public TimeSpan ProviderCacheLifetime { get; init; } = TimeSpan.FromHours(24);

    private static IReadOnlyList<int> ReplaceBinderAppendedDefaults(IReadOnlyList<int> values)
    {
        bool hasAppendedConfiguration = values.Count > s_defaultOwnedProviderIds.Length
            && values.Take(s_defaultOwnedProviderIds.Length).SequenceEqual(s_defaultOwnedProviderIds);

        return hasAppendedConfiguration
            ? values.Skip(s_defaultOwnedProviderIds.Length).ToArray()
            : values;
    }
}
