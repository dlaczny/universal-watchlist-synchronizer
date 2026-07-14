namespace Watchlist.Application;

/// <summary>
/// Manages the single protected Trakt account connection.
/// </summary>
public interface ITraktConnectionService
{
    Task<TraktDeviceStartDto> StartDeviceAsync(CancellationToken cancellationToken);

    Task<TraktConnectionStatusDto> PollPendingAsync(CancellationToken cancellationToken);

    Task<TraktConnectionStatusDto> GetStatusAsync(CancellationToken cancellationToken);

    Task<TraktConnectionStatusDto> DisconnectAsync(CancellationToken cancellationToken);
}
