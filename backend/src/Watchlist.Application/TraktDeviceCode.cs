namespace Watchlist.Application;

/// <summary>
/// Represents a Trakt device authorization challenge.
/// </summary>
public sealed record TraktDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUrl,
    TimeSpan ExpiresIn,
    TimeSpan Interval)
{
    public override string ToString()
    {
        return "TraktDeviceCode { Values = [REDACTED] }";
    }
}
