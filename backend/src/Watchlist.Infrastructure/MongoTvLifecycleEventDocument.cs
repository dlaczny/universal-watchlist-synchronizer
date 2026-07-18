using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed record MongoTvLifecycleEventDocument
{
    // Mongo identity is attempt-scoped; EventId remains the stable lifecycle identity.
    [BsonId]
    public string Id { get; init; } = string.Empty;

    [BsonElement("eventId")]
    public string EventId { get; init; } = string.Empty;

    [BsonElement("traktId")]
    public long TraktId { get; init; }

    [BsonElement("lifecycleVersion")]
    public long LifecycleVersion { get; init; }

    [BsonElement("generationId")]
    public string GenerationId { get; init; } = string.Empty;

    [BsonElement("eventType")]
    public string EventType { get; init; } = string.Empty;

    [BsonElement("occurredAt")]
    public DateTimeOffset OccurredAt { get; init; }

    [BsonElement("predicateHash")]
    public string PredicateHash { get; init; } = string.Empty;

    [BsonElement("reason")]
    public string Reason { get; init; } = string.Empty;

    public static MongoTvLifecycleEventDocument FromDomain(TvLifecycleEvent lifecycleEvent)
    {
        ArgumentNullException.ThrowIfNull(lifecycleEvent);
        return new MongoTvLifecycleEventDocument
        {
            Id = CreatePhysicalId(lifecycleEvent.GenerationId, lifecycleEvent.Id),
            EventId = lifecycleEvent.Id,
            TraktId = lifecycleEvent.TraktId,
            LifecycleVersion = lifecycleEvent.Version,
            GenerationId = lifecycleEvent.GenerationId,
            EventType = lifecycleEvent.EventType,
            OccurredAt = lifecycleEvent.OccurredAt,
            PredicateHash = lifecycleEvent.PredicateHash,
            Reason = lifecycleEvent.Reason
        };
    }

    public TvLifecycleEvent ToDomain()
    {
        if (!string.Equals(
                Id,
                CreatePhysicalId(GenerationId, EventId),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("tv_lifecycle_event_identity_invalid");
        }

        return new TvLifecycleEvent(
            EventId,
            TraktId,
            LifecycleVersion,
            GenerationId,
            EventType,
            OccurredAt,
            PredicateHash,
            Reason);
    }

    private static string CreatePhysicalId(string generationId, string eventId)
    {
        return $"generation:{generationId}:{eventId}";
    }
}
