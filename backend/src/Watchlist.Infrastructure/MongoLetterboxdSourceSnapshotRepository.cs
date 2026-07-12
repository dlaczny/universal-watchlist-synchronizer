using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class MongoLetterboxdSourceSnapshotRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options) : ILetterboxdSourceSnapshotRepository
{
    private readonly IMongoCollection<MongoLetterboxdSourceSnapshotDocument> snapshots =
        database.GetCollection<MongoLetterboxdSourceSnapshotDocument>(
            options.Value.LetterboxdSourceSnapshotsCollectionName);

    public async Task<LetterboxdSourceSnapshot?> GetLatestAsync(
        CancellationToken cancellationToken)
    {
        MongoLetterboxdSourceSnapshotDocument? document = await snapshots
            .Find(FilterDefinition<MongoLetterboxdSourceSnapshotDocument>.Empty)
            .SortByDescending(snapshot => snapshot.PublishedAt)
            .ThenByDescending(snapshot => snapshot.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            return null;
        }

        return new LetterboxdSourceSnapshot(
            document.Id,
            document.PublishedAt,
            document.SourceIds.ToHashSet(StringComparer.Ordinal),
            document.WatchedMovies
                .Select(movie => new PublishedWatchedMovie(
                    movie.SourceId,
                    movie.LifecycleEventId,
                    movie.WatchedAt,
                    movie.LifecycleVersion))
                .ToList());
    }
}
