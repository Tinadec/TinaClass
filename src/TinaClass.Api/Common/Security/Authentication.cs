namespace TinaClass.Api.Common.Security;

/// <summary>
/// Authentication schemes. Requests carry <c>Authorization: Bearer …</c>: a JWT from sign-in, or an API key (<c>ak_live_…</c>).
/// The default scheme picks between them by the key prefix.
/// </summary>
public static class AuthSchemes
{
    public const string Default = "token-or-key";
    public const string Jwt = "jwt";
    public const string ApiKey = "api-key";
    public const string ApiKeyPrefix = "ak_";
}
