using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Watchlist.Application;

namespace Watchlist.Infrastructure;

public sealed class DataProtectionTraktTokenProtector(IDataProtectionProvider provider)
    : ITraktTokenProtector
{
    private const string ProtectorPurpose = "Watchlist.Trakt.SingleAccountTokens.v1";
    private readonly IDataProtector protector = provider.CreateProtector(ProtectorPurpose);

    public string Protect(string plaintext)
    {
        return protector.Protect(plaintext);
    }

    public string Unprotect(string ciphertext)
    {
        try
        {
            return protector.Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            throw new TraktConnectionUnreadableException();
        }
    }
}
