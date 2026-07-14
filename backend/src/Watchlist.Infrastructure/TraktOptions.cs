namespace Watchlist.Infrastructure;

public sealed class TraktOptions
{
    public const string SectionName = "Trakt";

    public string BaseUrl { get; init; } = "https://api.trakt.tv";

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string RedirectUri { get; init; } = "urn:ietf:wg:oauth:2.0:oob";

    public TimeSpan ActivityPollInterval { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan FullSyncInterval { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan MetadataRefreshInterval { get; init; } = TimeSpan.FromDays(1);

    public TimeSpan TokenRefreshSkew { get; init; } = TimeSpan.FromMinutes(5);

    public int PageSize { get; init; } = 100;
}
