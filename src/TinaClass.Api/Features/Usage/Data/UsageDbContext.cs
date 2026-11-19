using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Usage.Data;

/// <summary>
/// What the models cost (table prefix <c>use_</c>): one row per request (<c>use_record</c>, append only), a daily total per model and person
/// (<c>use_daily</c>, what the charts read, so that they never scan the details) and the budget. Amounts are whole minor units
/// (fen or cents): SQLite has no exact decimal, so money is an integer on every database.
/// </summary>
public abstract class UsageDbContext(DbContextOptions options) : FeatureDbContext(options)
{
    public const string Name = "usage";

    public DbSet<UsageRecord> Records => Set<UsageRecord>();

    public DbSet<DailyUsage> Daily => Set<DailyUsage>();

    public DbSet<Budget> Budgets => Set<Budget>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UsageRecord>(e =>
        {
            e.ToTable("use_record");
            e.Property(x => x.ModelName).HasMaxLength(80);
            e.Property(x => x.SubjectKind).HasMaxLength(16);
            e.Property(x => x.PriceSnapshot).HasMaxLength(300);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.UserId, x.At });
        });
        modelBuilder.Entity<DailyUsage>(e =>
        {
            e.ToTable("use_daily");
            e.Property(x => x.ModelName).HasMaxLength(80);
            e.HasIndex(x => new { x.Day, x.ModelId, x.UserId }).IsUnique();
        });
        modelBuilder.Entity<Budget>(e =>
        {
            e.ToTable("use_budget");
            e.Property(x => x.Scope).HasMaxLength(16);
            e.Property(x => x.Period).HasMaxLength(8);
            e.Property(x => x.WarnAt).HasMaxLength(24);
            e.Property(x => x.Action).HasMaxLength(16);
            e.Property(x => x.AlertMonth).HasMaxLength(8);
            e.Property(x => x.AlertState).HasMaxLength(16);
            e.HasIndex(x => new { x.Scope, x.ScopeId }).IsUnique();
        });
    }
}

public sealed class UsageRecord
{
    public Guid Id { get; set; }

    /// <summary>The person; null for the agents' own runs.</summary>
    public Guid? UserId { get; set; }

    public Guid? ModelId { get; set; }

    public Guid? ProviderId { get; set; }

    public required string ModelName { get; set; }

    /// <summary>chat, agent, flow or prompt.</summary>
    public required string SubjectKind { get; set; }

    public Guid SubjectId { get; set; }

    public int TokensIn { get; set; }

    public int TokensOut { get; set; }

    public int TokensCache { get; set; }

    public int FirstTokenMs { get; set; }

    public int TotalMs { get; set; }

    public long Cost { get; set; }

    /// <summary>The prices the cost was worked out with, so that a later price change does not rewrite history.</summary>
    public string PriceSnapshot { get; set; } = "";

    public DateTimeOffset At { get; set; }
}

public sealed class DailyUsage
{
    public Guid Id { get; set; }

    public DateOnly Day { get; set; }

    public Guid ModelId { get; set; }

    public required string ModelName { get; set; }

    /// <summary><see cref="Guid.Empty"/> for the agents' own runs (a key part cannot be null).</summary>
    public Guid UserId { get; set; }

    public int Requests { get; set; }

    public long Tokens { get; set; }

    public long CacheTokens { get; set; }

    public long FirstTokenMsTotal { get; set; }

    public long Cost { get; set; }
}

public sealed class Budget : IVersioned
{
    public Guid Id { get; set; }

    /// <summary><c>workspace</c> (the whole company) or <c>user</c> (the limit of each member).</summary>
    public required string Scope { get; set; }

    public Guid ScopeId { get; set; }

    public string Period { get; set; } = "month";

    public long Amount { get; set; }

    /// <summary>The percentages that warn, comma separated.</summary>
    public string WarnAt { get; set; } = "50,80";

    public string Action { get; set; } = "notify";

    /// <summary>The month (<c>2026-11</c>) and state of the last alert: a threshold is announced once a month.</summary>
    public string? AlertMonth { get; set; }

    public string? AlertState { get; set; }

    public int Version { get; set; }
}
