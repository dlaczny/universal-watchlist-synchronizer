using System.Globalization;
using System.Text;
using Watchlist.Domain;

namespace Watchlist.Application;

public static class PlexMovieMatcher
{
    public static PlexMatchResult Match(
        WatchlistItemWriteModel watchlistMovie,
        IReadOnlyList<PlexMovieDto> plexMovies)
    {
        PlexMovieDto? imdbMatch = FindSingle(
            plexMovies.Where(movie => !string.IsNullOrWhiteSpace(watchlistMovie.ImdbId)
                && string.Equals(movie.ImdbId, watchlistMovie.ImdbId, StringComparison.OrdinalIgnoreCase)));
        if (imdbMatch is not null)
        {
            return Available(imdbMatch, "imdb");
        }

        PlexMovieDto? tmdbMatch = FindSingle(
            plexMovies.Where(movie => watchlistMovie.TmdbId is not null && movie.TmdbId == watchlistMovie.TmdbId));
        if (tmdbMatch is not null)
        {
            return Available(tmdbMatch, "tmdb");
        }

        List<PlexMovieDto> titleYearMatches = plexMovies
            .Where(movie => watchlistMovie.Item.Year is not null
                && movie.Year == watchlistMovie.Item.Year
                && NormalizeTitle(movie.Title) == NormalizeTitle(watchlistMovie.Item.Title))
            .ToList();
        if (titleYearMatches.Count == 1)
        {
            return Available(titleYearMatches[0], "title_year");
        }

        if (titleYearMatches.Count > 1)
        {
            return new PlexMatchResult(AvailabilityStatus.UnknownMatch, null, "ambiguous", "ambiguous");
        }

        AvailabilityStatus noMatchStatus = watchlistMovie.Item.ReleaseStatus == ReleaseStatus.Unreleased
            ? AvailabilityStatus.Unreleased
            : AvailabilityStatus.NotOnPlex;

        return new PlexMatchResult(noMatchStatus, null, "none", "none");
    }

    private static PlexMovieDto? FindSingle(IEnumerable<PlexMovieDto> matches)
    {
        List<PlexMovieDto> list = matches.Take(2).ToList();
        return list.Count == 1 ? list[0] : null;
    }

    private static PlexMatchResult Available(PlexMovieDto movie, string reason)
    {
        return new PlexMatchResult(AvailabilityStatus.AvailableOnPlex, movie.RatingKey, reason, "exact");
    }

    private static string NormalizeTitle(string title)
    {
        StringBuilder builder = new();
        bool lastWasSpace = false;

        foreach (char character in title.ToLower(CultureInfo.InvariantCulture).Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSpace = false;
            }
            else if (char.IsWhiteSpace(character) && !lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }
}
