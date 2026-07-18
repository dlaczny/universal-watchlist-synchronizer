namespace Watchlist.Application;

/// <summary>
/// Indicates that the published TV pointer, manifest, or generation rows disagree.
/// </summary>
public sealed class TvPublishedGenerationInvalidException : Exception
{
    public TvPublishedGenerationInvalidException(string code)
        : base("The published TV generation is invalid.")
    {
        Code = ValidateCode(code);
    }

    public string Code { get; }

    private static string ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)
            || code.Any(character => character is not (>= 'a' and <= 'z')
                && character is not (>= '0' and <= '9')
                && character != '_'))
        {
            throw new ArgumentException("The failure code must be stable and redacted.", nameof(code));
        }

        return code;
    }
}
