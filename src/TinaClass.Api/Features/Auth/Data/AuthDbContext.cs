using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Auth.Data;

/// <summary>
/// Users, sign-in sessions and API keys (tables <c>auth_</c>). The model is provider neutral (see <see cref="FeatureDbContext"/>);
/// <c>Data/Sqlite</c>, <c>Data/Postgres</c> and <c>Data/SqlServer</c> derive from it and own the migrations. Tokens, keys and
/// codes are stored as SHA-256 hashes only.
/// </summary>
public abstract class AuthDbContext(DbContextOptions options) : FeatureDbContext(options)
{
    public const string Name = "auth";

    public DbSet<User> Users => Set<User>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("auth_users");
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.NormalizedEmail).HasMaxLength(254);
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.Property(x => x.RoleId).HasMaxLength(40);
        });
        modelBuilder.Entity<Session>(e =>
        {
            e.ToTable("auth_sessions");
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.PreviousTokenHash).HasMaxLength(64);
            e.Property(x => x.Device).HasMaxLength(200);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.PreviousTokenHash);
            e.HasIndex(x => x.UserId);
        });
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.ToTable("auth_api_keys");
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Prefix).HasMaxLength(20);
            e.Property(x => x.SecretHash).HasMaxLength(64);
            e.Property(x => x.Scopes).HasMaxLength(1000);
            e.HasIndex(x => x.SecretHash).IsUnique();
            e.HasIndex(x => x.UserId);
        });
    }
}

public sealed class User : IVersioned
{
    public Guid Id { get; set; }

    public required string Email { get; set; }

    /// <summary>The email in lower case: the sign-in key (databases differ in how they compare text, so the API never relies on it).</summary>
    public required string NormalizedEmail { get; set; }

    public required string Name { get; set; }

    /// <summary>PBKDF2 (ASP.NET Core Identity's format).</summary>
    public required string PasswordHash { get; set; }

    public required string RoleId { get; set; }

    public bool Disabled { get; set; }

    public int FailedSignIns { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int Version { get; set; }
}

/// <summary>A signed-in device: the hash of its current refresh token and of the one before (to detect reuse).</summary>
public sealed class Session : IVersioned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string TokenHash { get; set; }

    public string? PreviousTokenHash { get; set; }

    public required string Device { get; set; }

    public string? Ip { get; set; }

    public bool Remember { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public int Version { get; set; }
}

/// <summary>An API key: <c>ak_live_</c> + 32 hex characters, stored as a SHA-256 hash; it acts for <see cref="UserId"/> within its scopes.</summary>
public sealed class ApiKey : IVersioned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Name { get; set; }

    /// <summary>The first characters of the key (<c>ak_live_3f9a</c>), to recognize it in lists.</summary>
    public required string Prefix { get; set; }

    public required string SecretHash { get; set; }

    /// <summary>Permission names separated by commas.</summary>
    public string Scopes { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public int Version { get; set; }

    public IReadOnlyList<string> ScopeList => Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
