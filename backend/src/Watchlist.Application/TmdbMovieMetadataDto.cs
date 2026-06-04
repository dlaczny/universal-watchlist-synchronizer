namespace Watchlist.Application;

public sealed record TmdbMovieMetadataDto(
    TmdbMovieDetailsDto Details,
    TmdbMovieProviderDataDto Providers);
