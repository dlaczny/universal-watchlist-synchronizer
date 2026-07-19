namespace Watchlist.Application;

public interface ITvExportService
{
    Task<WorkerTvSnapshotDto?> GetTvSyncSnapshotAsync(CancellationToken cancellationToken);
}
