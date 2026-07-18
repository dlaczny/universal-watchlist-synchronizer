namespace Watchlist.Application;

public sealed record TmdbWatchProviderCatalogEntryDto(
    int ProviderId,
    string ProviderName,
    string? LogoPath,
    int DisplayPriority)
{
    public int ProviderId { get; init; } = ProviderId > 0
        ? ProviderId
        : throw new ArgumentOutOfRangeException(nameof(ProviderId));

    public string ProviderName { get; init; } =
        TmdbContractValidation.EnsureRequired(ProviderName, nameof(ProviderName));

    public string? LogoPath { get; init; } = TmdbContractValidation.NormalizeOptional(LogoPath);

    public int DisplayPriority { get; init; } = DisplayPriority >= 0
        ? DisplayPriority
        : throw new ArgumentOutOfRangeException(nameof(DisplayPriority));
}
