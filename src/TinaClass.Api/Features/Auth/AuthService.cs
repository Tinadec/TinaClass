using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Features.Auth.Data;
using TinaClass.Contracts.Auth;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Features.Auth;

/// <summary>Sign-in with email and password, refresh-token rotation and sign-out.</summary>
public sealed class AuthService(AuthDbContext db, Passwords passwords, TokenIssuer tokens, IActorContext actors, IAuditLog audit, TimeProvider time)
{
    public const int MaxFailedSignIns = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RememberFor = TimeSpan.FromDays(30);
    public static readonly TimeSpan SessionFor = TimeSpan.FromHours(12);

    /// <summary>
    /// One answer for every failure (unknown email, wrong password, a locked or disabled account), so that the response does not
    /// tell which emails have accounts. The password is checked even for an unknown email, so the time does not tell either.
    /// </summary>
    private static ApiException InvalidCredentials() =>
        ApiException.Unauthorized(ErrorCodes.InvalidCredentials, "The email or password is wrong, or sign-in is blocked for a few minutes after too many attempts.");

    public async Task<TokenPair> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalized = Passwords.Normalize(request.Email ?? "");
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);
        var now = time.GetUtcNow();
        var passwordOk = passwords.Verify(user, request.Password ?? "");
        if (!passwordOk || user is null || user.Disabled || user.LockedUntil > now)
        {
            await FailAsync(user, request.Email ?? "", cancellationToken);
            throw InvalidCredentials();
        }
        if (user.FailedSignIns != 0 || user.LockedUntil is not null)
        {
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(
                s => s.SetProperty(u => u.FailedSignIns, 0).SetProperty(u => u.LockedUntil, (DateTimeOffset?)null), cancellationToken);
        }
        return await StartSessionAsync(user, request.Remember, cancellationToken);
    }

    /// <summary>A new session for <paramref name="user"/> on this device, and its first token pair.</summary>
    private async Task<TokenPair> StartSessionAsync(User user, bool remember, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = "",
            Device = actors.Device ?? "Unknown device",
            Ip = actors.Ip,
            Remember = remember,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + (remember ? RememberFor : SessionFor),
        };
        var pair = tokens.Issue(user, session);
        session.PreviousTokenHash = null;
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(AuthAuditActions.SignedIn, "user", user.Id.ToString(), user.Name, user.Name), cancellationToken);
        return pair;
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair; the old token stops working. A token that was already exchanged ends its
    /// session (someone else may hold it). Two exchanges of the same token at once: the session's <c>Version</c> lets one win.
    /// </summary>
    public async Task<TokenPair> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = Secrets.Hash(request.RefreshToken ?? "");
        var now = time.GetUtcNow();
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (session is null)
        {
            var reused = await db.Sessions.Where(s => s.PreviousTokenHash == hash && s.RevokedAt == null).Select(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            if (reused != Guid.Empty)
            {
                await db.Sessions.Where(s => s.Id == reused).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
            }
            throw ApiException.Unauthorized(ErrorCodes.InvalidRefreshToken, "Sign in again.");
        }
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == session.UserId, cancellationToken);
        if (session.RevokedAt is not null || session.ExpiresAt <= now || user is null || user.Disabled)
        {
            throw ApiException.Unauthorized(ErrorCodes.InvalidRefreshToken, "Sign in again.");
        }
        var pair = tokens.Issue(user, session);
        session.LastSeenAt = now;
        session.Ip = actors.Ip ?? session.Ip;
        session.ExpiresAt = now + (session.Remember ? RememberFor : SessionFor);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Unauthorized(ErrorCodes.InvalidRefreshToken, "Sign in again.");
        }
        return pair;
    }

    /// <summary>Ends the session of a refresh token. Always succeeds: an unknown token is not an error.</summary>
    public async Task LogoutAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = Secrets.Hash(request.RefreshToken ?? "");
        var now = time.GetUtcNow();
        await db.Sessions.Where(s => s.TokenHash == hash && s.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
    }

    /// <summary>Counts a failed attempt (atomically, so parallel guesses cannot dodge the lock) and locks the account after five.</summary>
    private async Task FailAsync(User? user, string email, CancellationToken cancellationToken)
    {
        if (user is not null)
        {
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedSignIns, u => u.FailedSignIns + 1), cancellationToken);
            var failed = await db.Users.Where(u => u.Id == user.Id).Select(u => u.FailedSignIns).SingleAsync(cancellationToken);
            if (failed >= MaxFailedSignIns)
            {
                var until = time.GetUtcNow() + LockoutDuration;
                await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(
                    s => s.SetProperty(u => u.FailedSignIns, 0).SetProperty(u => u.LockedUntil, (DateTimeOffset?)until), cancellationToken);
            }
        }
        var name = email.Length > 254 ? email[..254] : email;
        await audit.RecordAsync(new AuditRecord(AuthAuditActions.SignInFailed, "user", user?.Id.ToString(), user?.Name ?? name, name), cancellationToken);
    }
}
