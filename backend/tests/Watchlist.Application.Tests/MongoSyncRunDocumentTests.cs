using FluentAssertions;
using Watchlist.Application;
using Watchlist.Infrastructure;

namespace Watchlist.Application.Tests;

public sealed class MongoSyncRunDocumentTests
{
    [Fact]
    public void ToDto_WhenDocumentIsComplete_MapsFields()
    {
        DateTimeOffset syncedAt = DateTimeOffset.Parse("2026-06-02T10:00:00+02:00");
        MongoSyncRunDocument document = new()
        {
            Id = "seeded-bootstrap",
            Status = "seeded",
            LastSuccessfulSyncAt = syncedAt
        };

        SyncStatusDto status = document.ToDto();

        status.Should().Be(new SyncStatusDto("seeded", syncedAt));
    }
}
