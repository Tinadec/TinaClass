using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Auth.Data;
using TinaClass.Contracts.Auth;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Features.Auth;

/// <summary>
/// API keys: <c>ak_live_</c> + 32 hex characters, stored as a SHA-256 hash; the secret is shown once. A key acts for the user who
/// made it and may do what its scopes allow (and what that user's role still allows). Only a signed-in user, never another key,
/// manages keys.
/// </summary>
public sealed class ApiKeyService(AuthDbContext db, IActorContext actors, IAuditLog audit, TimeProvider time)
{
    public const string KeyPrefix = AuthSchemes.ApiKeyPrefix + "live_";

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken cancellationToken)
    {
        var actor = await RequireUserAsync(cancellationToken);
        var keys = await db.ApiKeys.AsNoTracking().Where(k => k.UserId == actor.Id && k.RevokedAt == null).OrderByDescending(k => k.CreatedAt)
                           .ThenBy(k => k.Id).ToListAsync(cancellationToken);
        return [.. keys.Select(ToDto)];
    }

    public async Task<CreatedApiKey> CreateAsync(CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var actor = await RequireUserAsync(cancellationToken);
        var validator = new Validator().Text("name", request.Name, 80);
        var scopes = (request.Scopes ?? []).Distinct().ToList();
        if (scopes.Count == 0)
        {
            validator.Add("scopes", "Choose at least one permission.");
        }
        foreach (var scope in scopes.Where(s => !Permissions.All.Contains(s) || !actor.Can(s)))
        {
            validator.Add("scopes", $"\"{scope}\" is unknown or more than you may do yourself.");
        }
        validator.ThrowIfInvalid();
        var secret = KeyPrefix + Secrets.Hex(32);
        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = actor.Id!.Value,
            Name = request.Name.Trim(),
            Prefix = secret[..(KeyPrefix.Length + 4)],
            SecretHash = Secrets.Hash(secret),
            Scopes = string.Join(',', Permissions.All.Where(scopes.Contains)),
            CreatedAt = time.GetUtcNow(),
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(AuthAuditActions.ApiKeyCreated, "api_key", key.Id.ToString(), key.Name), cancellationToken);
        return new CreatedApiKey(ToDto(key), secret);
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await RequireUserAsync(cancellationToken);
        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == id && k.UserId == actor.Id && k.RevokedAt == null, cancellationToken)
                  ?? throw ApiException.NotFound("The API key");
        key.RevokedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(AuthAuditActions.ApiKeyRevoked, "api_key", key.Id.ToString(), key.Name), cancellationToken);
    }

    private async Task<Actor> RequireUserAsync(CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        return actor.Type == ActorType.User ? actor : throw ApiException.Forbidden("API keys are managed by a signed-in user, not by a key.");
    }

    private static ApiKeyDto ToDto(ApiKey k) => new(k.Id, k.Name, k.Prefix, k.ScopeList, k.CreatedAt, k.LastUsedAt);
}

/// <summary>Authenticates <c>Authorization: Bearer ak_live_…</c>; the default scheme forwards such requests here.</summary>
internal sealed class ApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer " + ApiKeyService.KeyPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }
        var hash = Secrets.Hash(header["Bearer ".Length..].Trim());
        var db = Context.RequestServices.GetRequiredService<AuthDbContext>();
        var key = await db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(k => k.SecretHash == hash && k.RevokedAt == null, Context.RequestAborted);
        if (key is null)
        {
            return AuthenticateResult.Fail("The API key is unknown or revoked.");
        }
        var now = time.GetUtcNow();
        if (key.LastUsedAt is null || now - key.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            // A hint for the key list: the last write may win, so it skips the version check.
            await db.ApiKeys.Where(k => k.Id == key.Id).ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), Context.RequestAborted);
        }
        var claims = new List<Claim>
        {
            new(AppClaims.Subject, key.UserId.ToString()),
            new(AppClaims.Name, key.Name),
            new(AppClaims.Kind, AppClaims.ApiKeyKind),
        };
        claims.AddRange(key.ScopeList.Select(s => new Claim(AppClaims.Scope, s)));
        var identity = new ClaimsIdentity(claims, Scheme.Name, AppClaims.Name, null);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
