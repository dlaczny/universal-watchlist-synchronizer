namespace Watchlist.Application;

public interface ILetterboxdSourceSnapshotRepository
{
    Task<LetterboxdSourceSnapshot?> GetLatestAsync(CancellationToken cancellationToken);
}
