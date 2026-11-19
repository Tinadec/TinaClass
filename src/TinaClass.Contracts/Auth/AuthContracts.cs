namespace TinaClass.Contracts.Auth;

/// <summary>POST /auth/login. <see cref="Remember"/> keeps the session for 30 days instead of 12 hours.</summary>
public sealed record LoginRequest(string Email, string Password, bool Remember);

/// <summary>A short-lived access token (Bearer) and a refresh token that POST /auth/refresh exchanges for a new pair.</summary>
public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);

/// <summary>POST /auth/refresh and POST /auth/logout.</summary>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>GET /me: who is signed in and what they may do.</summary>
public sealed record CurrentUser(Guid Id, string Email, string Name, string RoleId, IReadOnlyList<string> Permissions);

/// <summary>
/// GET /me/api-keys. Only <see cref="Prefix"/> is stored in clear; the secret is shown once, when the key is created. A key is
/// sent as <c>Authorization: Bearer ak_live_…</c> and may do what its scopes (permissions) allow.
/// </summary>
public sealed record ApiKeyDto(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

/// <summary>POST /me/api-keys; DELETE /me/api-keys/{id} revokes.</summary>
public sealed record CreateApiKeyRequest(string Name, IReadOnlyList<string> Scopes);

public sealed record CreatedApiKey(ApiKeyDto Key, string Secret);

/// <summary>What the audit log records for the auth feature (<c>IAuditLog</c> in the API).</summary>
public static class AuthAuditActions
{
    public const string SignedIn = "auth.signed_in";
    public const string SignInFailed = "auth.sign_in_failed";
    public const string ApiKeyCreated = "auth.api_key_created";
    public const string ApiKeyRevoked = "auth.api_key_revoked";
}
