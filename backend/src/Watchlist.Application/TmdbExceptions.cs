namespace Watchlist.Application;

public sealed class TmdbUnavailableException : Exception
{
    public TmdbUnavailableException(string message)
        : base(message)
    {
    }

    public TmdbUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class TmdbMovieNotFoundException : Exception
{
    public TmdbMovieNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class TmdbParseException : Exception
{
    public TmdbParseException(string message)
        : base(message)
    {
    }

    public TmdbParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
