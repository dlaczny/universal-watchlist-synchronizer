using MongoDB.Bson.Serialization.Attributes;

namespace Watchlist.Infrastructure;

public sealed class MongoTvPublishedPointerDocument
{
    public const string PublishedPointerId = "published-tv";
    public const string PointerDocumentKind = "pointer";

    [BsonId]
    public string Id { get; init; } = PublishedPointerId;

    [BsonElement("documentKind")]
    public string DocumentKind { get; init; } = PointerDocumentKind;

    [BsonElement("generationId")]
    public string GenerationId { get; init; } = string.Empty;

    [BsonElement("manifestId")]
    public string ManifestId { get; init; } = string.Empty;

    [BsonElement("showCount")]
    public int ShowCount { get; init; }

    [BsonElement("lifecycleEventCount")]
    public int LifecycleEventCount { get; init; }

    [BsonElement("membershipHash")]
    public string MembershipHash { get; init; } = string.Empty;

    [BsonElement("progressHash")]
    public string ProgressHash { get; init; } = string.Empty;

    [BsonElement("publishedAt")]
    public DateTimeOffset PublishedAt { get; init; }

    public static MongoTvPublishedPointerDocument FromManifest(
        MongoTvSyncManifestDocument manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new MongoTvPublishedPointerDocument
        {
            GenerationId = manifest.GenerationId,
            ManifestId = manifest.Id,
            ShowCount = manifest.ShowCount,
            LifecycleEventCount = manifest.LifecycleEventCount,
            MembershipHash = manifest.MembershipHash,
            ProgressHash = manifest.ProgressHash,
            PublishedAt = manifest.PublishedAt
        };
    }

    internal bool HasValidShape()
    {
        return string.Equals(Id, PublishedPointerId, StringComparison.Ordinal)
            && string.Equals(DocumentKind, PointerDocumentKind, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(GenerationId)
            && string.Equals(
                ManifestId,
                $"generation:{GenerationId}",
                StringComparison.Ordinal)
            && ShowCount >= 0
            && LifecycleEventCount >= 0
            && MembershipHash is { Length: 64 }
            && ProgressHash is { Length: 64 };
    }
}
