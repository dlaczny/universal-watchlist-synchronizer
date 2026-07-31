namespace Watchlist.Application;

/// <summary>
/// Represents a required TV generation retention failure using a stable public contract.
/// </summary>
public sealed class TvGenerationRetentionException : Exception
{
    public const string StableCode = "tv_generation_retention_failed";

    public TvGenerationRetentionException(Exception innerException)
        : base("TV generation retention failed.", innerException)
    {
    }

    public string Code => StableCode;
}
