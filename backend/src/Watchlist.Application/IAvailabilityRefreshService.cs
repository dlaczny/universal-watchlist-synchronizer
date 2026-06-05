namespace Watchlist.Application;

public interface IAvailabilityRefreshService
{
    Task<AvailabilityRefreshResultDto> RefreshAsync(CancellationToken cancellationToken);
}
