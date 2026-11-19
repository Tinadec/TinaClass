using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Auth.Data;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Features.Auth;

/// <summary>
/// Loads the actor of a request once, from the database (so that a disabled account, a revoked session or a changed role takes
/// effect at once, not when the access token expires). A user's permissions are those of the role; an API key's are its scopes
/// that the role still has.
/// </summary>
internal sealed class ActorContext(IHttpContextAccessor http, AuthDbContext db) : IActorContext
{
    private Actor? _actor;
    private bool _loaded;

    public string? Ip => http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? Device
    {
        get
        {
            var headers = http.HttpContext?.Request.Headers;
            // The app percent-encodes the name when it is not ASCII (header values are ASCII only).
            var device = headers?["X-Client-Device"].ToString() is { Length: > 0 } d ? Uri.UnescapeDataString(d) : headers?.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(device) ? null : device.Length > 200 ? device[..200] : device;
        }
    }

    public async Task<Actor?> GetAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return _actor;
        }
        _loaded = true;
        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirst(AppClaims.Subject)?.Value, out var userId))
        {
            return null;
        }
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.Disabled)
        {
            return null;
        }
        var role = Roles.PermissionsOf(user.RoleId);
        if (principal.FindFirst(AppClaims.Kind)?.Value == AppClaims.ApiKeyKind)
        {
            var scopes = principal.FindAll(AppClaims.Scope).Select(c => c.Value).Where(role.Contains).ToHashSet(StringComparer.Ordinal);
            return _actor = new Actor(ActorType.ApiKey, user.Id, user.Name, user.RoleId, scopes, null);
        }

        if (!Guid.TryParse(principal.FindFirst(AppClaims.Session)?.Value, out var sessionId))
        {
            return null;
        }
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null || session.UserId != user.Id || session.RevokedAt is not null)
        {
            return null;
        }
        return _actor = new Actor(ActorType.User, user.Id, user.Name, user.RoleId, role, sessionId);
    }

    public async Task<Actor> RequireAsync(CancellationToken cancellationToken) =>
        await GetAsync(cancellationToken) ?? throw ApiException.Unauthorized(ErrorCodes.Unauthorized, "Sign in first.");
}
