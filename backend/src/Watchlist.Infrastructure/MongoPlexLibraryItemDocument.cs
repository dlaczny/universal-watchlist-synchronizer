using MongoDB.Bson.Serialization.Attributes;
using Watchlist.Application;
using Watchlist.Domain;

namespace Watchlist.Infrastructure;

public sealed class MongoPlexLibraryItemDocument
{
    [BsonId]
    public string Id { get; init; } = string.Empty;

    public string RatingKey { get; init; } = string.Empty;

    public MediaType MediaType { get; init; }

    public string Title { get; init; } = string.Empty;

    public int? Year { get; init; }

    public string LibrarySectionKey { get; init; } = string.Empty;

    public string LibrarySectionTitle { get; init; } = string.Empty;

    public string? PlexGuid { get; init; }

    public string? ImdbId { get; init; }

    public int? TmdbId { get; init; }

    public int? TvdbId { get; init; }

    public DateTimeOffset LastSeenAt { get; init; }

    public PlexMovieDto ToDto()
    {
        return new PlexMovieDto(
            RatingKey,
            Title,
            Year,
            LibrarySectionKey,
            LibrarySectionTitle,
            PlexGuid,
            ImdbId,
            TmdbId,
            TvdbId);
    }

    public static MongoPlexLibraryItemDocument FromDto(PlexMovieDto movie, DateTimeOffset lastSeenAt)
    {
        return new MongoPlexLibraryItemDocument
        {
            Id = $"plex-movie-{movie.RatingKey}",
            RatingKey = movie.RatingKey,
            MediaType = MediaType.Movie,
            Title = movie.Title,
            Year = movie.Year,
            LibrarySectionKey = movie.LibrarySectionKey,
            LibrarySectionTitle = movie.LibrarySectionTitle,
            PlexGuid = movie.PlexGuid,
            ImdbId = movie.ImdbId,
            TmdbId = movie.TmdbId,
            TvdbId = movie.TvdbId,
            LastSeenAt = lastSeenAt
        };
    }
}
