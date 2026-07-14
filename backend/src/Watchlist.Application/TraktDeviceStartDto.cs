namespace Watchlist.Application;

/// <summary>
/// Provides the one-time public details needed to authorize a Trakt device.
/// </summary>
public sealed record TraktDeviceStartDto(
    string UserCode,
    string VerificationUrl,
    DateTimeOffset ExpiresAt,
    int PollIntervalSeconds);
