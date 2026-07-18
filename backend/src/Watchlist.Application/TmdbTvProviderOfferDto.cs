namespace Watchlist.Application;

public sealed record TmdbTvProviderOfferDto(
    int ProviderId,
    string ProviderName,
    string Category,
    string? LogoPath)
{
    private static readonly HashSet<string> SupportedCategories = new(StringComparer.Ordinal)
    {
        "flatrate",
        "free",
        "ads",
        "rent",
        "buy"
    };

    public int ProviderId { get; init; } = ProviderId > 0
        ? ProviderId
        : throw new ArgumentOutOfRangeException(nameof(ProviderId));

    public string ProviderName { get; init; } =
        TmdbContractValidation.EnsureRequired(ProviderName, nameof(ProviderName));

    public string Category { get; init; } = SupportedCategories.Contains(Category)
        ? Category
        : throw new ArgumentException("Provider category is unsupported.", nameof(Category));

    public string? LogoPath { get; init; } = TmdbContractValidation.NormalizeOptional(LogoPath);
}
