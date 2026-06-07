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

    public string? Summary { get; init; }

    public string? PosterPath { get; init; }

    public string? BackdropPath { get; init; }

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
            TvdbId,
            LastSeenAt,
            Summary,
            PosterPath,
            BackdropPath);
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
            Summary = movie.Summary,
            PosterPath = movie.PosterPath,
            BackdropPath = movie.BackdropPath,
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
