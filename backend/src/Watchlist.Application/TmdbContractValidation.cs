namespace Watchlist.Application;

internal static class TmdbContractValidation
{
    public static DateTimeOffset EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero
            || value == DateTimeOffset.MinValue
            || value == DateTimeOffset.MaxValue)
        {
            throw new ArgumentException("Timestamp must be a finite UTC value.", parameterName);
        }

        return value;
    }

    public static string EnsureRegionCode(string value, string parameterName)
    {
        if (value is null
            || value.Length != 2
            || value[0] is < 'A' or > 'Z'
            || value[1] is < 'A' or > 'Z')
        {
            throw new ArgumentException("Region must be an uppercase ISO 3166-1 alpha-2 code.", parameterName);
        }

        return value;
    }

    public static string EnsureRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value;
    }

    public static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static string EnsureStableErrorCode(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !IsStableErrorCodeCharacter(character)))
        {
            throw new ArgumentException("Error code must use stable lowercase code characters.", parameterName);
        }

        return value;
    }

    private static bool IsStableErrorCodeCharacter(char value)
    {
        return value is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_';
    }
}
