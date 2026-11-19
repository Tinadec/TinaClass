using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Auth.Data;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Features.Auth;

/// <summary>Hashes and checks passwords (PBKDF2, ASP.NET Core Identity's format).</summary>
public sealed class Passwords
{
    public const int MinimumLength = 10;
    public const int MaximumLength = 128;

    private static readonly PasswordHasher<User> s_hasher = new();
    private static readonly User s_dummyUser = new() { Email = "", NormalizedEmail = "", Name = "", PasswordHash = "", RoleId = "" };
    private static readonly Lazy<string> s_dummy = new(() => s_hasher.HashPassword(s_dummyUser, Guid.NewGuid().ToString()));

    public string Hash(User user, string password) => s_hasher.HashPassword(user, password);

    public bool Verify(User? user, string password)
    {
        if (user is null)
        {
            // Same work as a real check, so that the response time does not tell which emails have accounts.
            s_hasher.VerifyHashedPassword(s_dummyUser, s_dummy.Value, password);
            return false;
        }
        return s_hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static bool IsEmail(string value) =>
        value.Length <= 254 && MailAddress.TryCreate(value, out var address) && address.Address == value.Trim() && value.Contains('.', StringComparison.Ordinal);
}

public sealed record UserSummary(Guid Id, string Email, string Name, string RoleId, bool Disabled, DateTimeOffset CreatedAt);

/// <summary>The accounts: creating them (the first administrator, the sample users, an invitation feature) and looking them up.</summary>
public sealed class UserAccounts(AuthDbContext db, Passwords passwords, TimeProvider time)
{
    public Task<int> CountAsync(CancellationToken cancellationToken) => db.Users.CountAsync(cancellationToken);

    public async Task<UserSummary?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Passwords.Normalize(email);
        return await db.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalized).Select(s_project).SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Creates an account; 400 for a bad email, name, role or password, 409 <c>conflict</c> when the email is taken.</summary>
    public async Task<UserSummary> CreateAsync(string email, string name, string password, string roleId, CancellationToken cancellationToken, Guid? id = null)
    {
        var validator = new Validator().Text("name", name, 120).Text("email", email, 254);
        if (!string.IsNullOrWhiteSpace(email) && !Passwords.IsEmail(email.Trim()))
        {
            validator.Add("email", "Not a valid email address.");
        }
        if (!Roles.Exists(roleId))
        {
            validator.Add("roleId", "Unknown role.");
        }
        if (password.Length < Passwords.MinimumLength || password.Length > Passwords.MaximumLength)
        {
            validator.Add("password", $"Use {Passwords.MinimumLength} to {Passwords.MaximumLength} characters.");
        }
        validator.ThrowIfInvalid();

        var normalized = Passwords.Normalize(email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            throw ApiException.Conflict(ErrorCodes.Conflict, "An account with this email already exists.");
        }
        var user = new User
        {
            Id = id ?? Guid.NewGuid(),
            Email = email.Trim(),
            NormalizedEmail = normalized,
            Name = name.Trim(),
            PasswordHash = "",
            RoleId = roleId,
            CreatedAt = time.GetUtcNow(),
        };
        user.PasswordHash = passwords.Hash(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return s_project.Compile()(user);
    }

    private static readonly System.Linq.Expressions.Expression<Func<User, UserSummary>> s_project =
        u => new UserSummary(u.Id, u.Email, u.Name, u.RoleId, u.Disabled, u.CreatedAt);
}

/// <summary>The accounts as the team feature sees them (<see cref="IUserDirectory"/>); a change of role or a disabled account takes effect on the user's next request.</summary>
internal sealed class UserDirectory(AuthDbContext db, UserAccounts accounts) : IUserDirectory
{
    public async Task<IReadOnlyList<DirectoryUser>> ListAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
                .Select(u => new DirectoryUser(u.Id, u.Email, u.Name, u.RoleId, u.Disabled, u.CreatedAt)).ToListAsync(cancellationToken);

    public async Task<DirectoryUser?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new DirectoryUser(u.Id, u.Email, u.Name, u.RoleId, u.Disabled, u.CreatedAt)).SingleOrDefaultAsync(cancellationToken);

    public async Task<DirectoryUser?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = Passwords.Normalize(email);
        return await db.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalized).Select(u => new DirectoryUser(u.Id, u.Email, u.Name, u.RoleId, u.Disabled, u.CreatedAt))
                       .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task ChangeRoleAsync(Guid id, string roleId, CancellationToken cancellationToken)
    {
        if (!Roles.Exists(roleId))
        {
            throw ApiException.Invalid("roleId", "Unknown role.");
        }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken) ?? throw ApiException.NotFound("The member");
        user.RoleId = roleId;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetDisabledAsync(Guid id, bool disabled, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken) ?? throw ApiException.NotFound("The member");
        user.Disabled = disabled;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DirectoryUser> CreateAsync(string email, string name, string password, string roleId, CancellationToken cancellationToken)
    {
        var u = await accounts.CreateAsync(email, name, password, roleId, cancellationToken);
        return new DirectoryUser(u.Id, u.Email, u.Name, u.RoleId, u.Disabled, u.CreatedAt);
    }
}
