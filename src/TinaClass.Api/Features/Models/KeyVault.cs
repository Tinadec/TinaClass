using Microsoft.AspNetCore.DataProtection;

namespace TinaClass.Api.Features.Models;

/// <summary>
/// Provider keys at rest: encrypted with Data Protection (the keys folder is a volume in a container, see the README), never logged and
/// never returned by an endpoint. Only <see cref="Hint"/> (the last four characters) is shown.
/// </summary>
public sealed class KeyVault(IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("TinaClass.Models.ProviderKey.v1");

    public string Protect(string key) => _protector.Protect(key);

    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher))
        {
            return null;
        }
        try
        {
            return _protector.Unprotect(cipher);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // The data-protection keys were lost (a replaced container without the keys volume): treat the key as not set.
            return null;
        }
    }

    public static string Hint(string key) => "••••••••" + (key.Length <= 4 ? key : key[^4..]);
}
