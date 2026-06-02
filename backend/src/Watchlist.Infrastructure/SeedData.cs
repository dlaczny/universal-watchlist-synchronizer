using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public static class SeedData
{
    public static readonly IReadOnlyList<WatchlistItem> WatchlistItems =
    [
        new WatchlistItem(
            "movie-dune-part-two",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            "letterboxd-dune-part-two",
            "Dune: Part Two",
            2024,
            "Paul Atreides unites with Chani and the Fremen while seeking revenge against the conspirators who destroyed his family.",
            "https://image.tmdb.org/t/p/w500/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg",
            "https://image.tmdb.org/t/p/w1280/xOMo8BRK7PfcJv9JCnx7s5hj0PX.jpg",
            ReleaseStatus.Released,
            AvailabilityStatus.AvailableOnPlex,
            DateTimeOffset.Parse("2026-05-25T10:00:00+02:00")),
        new WatchlistItem(
            "movie-unreleased-example",
            MediaType.Movie,
            WatchlistSource.Letterboxd,
            "letterboxd-unreleased-example",
            "Future Movie",
            2027,
            "A seed item representing a wanted movie that has not been released yet.",
            null,
            null,
            ReleaseStatus.Unreleased,
            AvailabilityStatus.Unreleased,
            DateTimeOffset.Parse("2026-05-25T10:00:00+02:00")),
        new WatchlistItem(
            "tv-andor",
            MediaType.TvShow,
            WatchlistSource.Tmdb,
            "tmdb-tv-83867",
            "Andor",
            2022,
            "The story of Cassian Andor's journey to discover the difference he can make.",
            "https://image.tmdb.org/t/p/w500/59SVNwLfoMnZPPB6ukW6dlPxAdI.jpg",
            "https://image.tmdb.org/t/p/w1280/5NbdcZdsu7Rr0RthcYk4qqv7W7J.jpg",
            ReleaseStatus.Released,
            AvailabilityStatus.NotOnPlex,
            DateTimeOffset.Parse("2026-05-25T10:00:00+02:00"))
    ];

    public static readonly IReadOnlyList<MongoSyncRunDocument> SyncRuns =
    [
        new MongoSyncRunDocument
        {
            Id = "seeded-bootstrap",
            Status = "seeded",
            LastSuccessfulSyncAt = DateTimeOffset.Parse("2026-05-25T10:00:00+02:00")
        }
    ];
}
