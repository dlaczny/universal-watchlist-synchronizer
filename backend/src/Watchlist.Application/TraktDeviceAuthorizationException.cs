namespace Watchlist.Application;

/// <summary>
/// Carries a stable, non-secret Trakt device authorization outcome.
/// </summary>
public sealed class TraktDeviceAuthorizationException(string code)
    : Exception(CreateMessage(code))
{
    /// <summary>
    /// Gets the stable, non-secret device authorization outcome code.
    /// </summary>
    public string Code { get; } = NormalizeCode(code);

    private static string CreateMessage(string code)
    {
        return $"Trakt device authorization ended with code {NormalizeCode(code)}.";
    }

    private static string NormalizeCode(string code)
    {
        return code switch
        {
            "invalid" => "invalid",
            "already_used" => "already_used",
            "expired" => "expired",
            "denied" => "denied",
            "slow_down" => "slow_down",
            _ => "invalid"
        };
    }
}
