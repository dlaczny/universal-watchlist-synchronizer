namespace Watchlist.Application;

/// <summary>
/// Protects and restores sensitive Trakt connection values.
/// </summary>
public interface ITraktTokenProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}
