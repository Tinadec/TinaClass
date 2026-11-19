using System.Security.Cryptography;
using System.Text;

namespace TinaClass.Api.Common.Security;

/// <summary>Random secrets and their hashes. Tokens, keys and codes are stored only as SHA-256 hashes.</summary>
public static class Secrets
{
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Compares two hashes in constant time.</summary>
    public static bool HashEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    /// <summary>URL-safe random text with <paramref name="bytes"/> bytes of entropy.</summary>
    public static string Token(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Lower-case hex characters, for keys that are easy to read and copy.</summary>
    public static string Hex(int length) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes((length + 1) / 2))[..length];
}
