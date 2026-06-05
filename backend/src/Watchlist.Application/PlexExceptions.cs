namespace Watchlist.Application;

public class PlexUnavailableException : Exception
{
    public PlexUnavailableException(string message) : base(message)
    {
    }

    public PlexUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class PlexParseException : Exception
{
    public PlexParseException(string message) : base(message)
    {
    }

    public PlexParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
