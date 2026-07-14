namespace Watchlist.Infrastructure;

public sealed class DataProtectionKeyRingOptions
{
    public const string SectionName = "DataProtection";

    public string KeyRingPath { get; init; } = ".artifacts/data-protection-keys";

    public string ApplicationName { get; init; } = "watchlist-api";
}
