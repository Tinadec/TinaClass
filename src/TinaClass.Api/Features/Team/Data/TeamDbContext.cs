using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Team.Data;

/// <summary>
/// Invitations and the audit log (table prefix <c>team_</c>). The people themselves stay in the auth feature (reached through
/// <c>IUserDirectory</c>). An invitation stores only the SHA-256 hash of its token; audit entries are append only.
/// </summary>
public abstract class TeamDbContext(DbContextOptions options) : FeatureDbContext(options)
{
    public const string Name = "team";

    public DbSet<Invite> Invites => Set<Invite>();

    public DbSet<AuditEntry> Audit => Set<AuditEntry>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invite>(e =>
        {
            e.ToTable("team_invite");
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.NormalizedEmail).HasMaxLength(254);
            e.Property(x => x.RoleId).HasMaxLength(40);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.NormalizedEmail);
        });
        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("team_audit");
            e.Property(x => x.ActorName).HasMaxLength(120);
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.TargetKind).HasMaxLength(40);
            e.Property(x => x.TargetId).HasMaxLength(80);
            e.Property(x => x.Detail).HasMaxLength(500);
            e.HasIndex(x => x.At);
        });
    }
}

public sealed class Invite
{
    public Guid Id { get; set; }

    public string Email { get; set; } = "";

    public string NormalizedEmail { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string TokenHash { get; set; } = "";

    public Guid InvitedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }
}

public sealed class AuditEntry
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public string ActorName { get; set; } = "";

    public string Action { get; set; } = "";

    public string TargetKind { get; set; } = "";

    public string TargetId { get; set; } = "";

    public string Detail { get; set; } = "";

    public DateTimeOffset At { get; set; }
}
