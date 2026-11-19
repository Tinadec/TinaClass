using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Agents.Data;

/// <summary>
/// Agents (table prefix <c>agt_</c>): the agent with its tools and knowledge bases, the tool servers and their tools, runs with their steps, and the
/// approvals people give. Knowledge bases, models and people are plain ids.
/// </summary>
public abstract class AgentsDbContext(DbContextOptions options) : FeatureDbContext(options)
{
    public const string Name = "agents";

    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<AgentToolLink> AgentTools => Set<AgentToolLink>();

    public DbSet<AgentBaseLink> AgentBases => Set<AgentBaseLink>();

    public DbSet<ToolServer> Servers => Set<ToolServer>();

    public DbSet<Tool> Tools => Set<Tool>();

    public DbSet<AgentRun> Runs => Set<AgentRun>();

    public DbSet<RunStep> Steps => Set<RunStep>();

    public DbSet<AgentApproval> Approvals => Set<AgentApproval>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agent>(e =>
        {
            e.ToTable("agt_agent");
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Description).HasMaxLength(400);
            e.Property(x => x.Instruction).HasMaxLength(8_000);
            e.Property(x => x.ModelCode).HasMaxLength(120);
            e.Property(x => x.Visibility).HasMaxLength(16);
            e.Property(x => x.State).HasMaxLength(16);
            e.Property(x => x.Schedule).HasMaxLength(60);
            e.Property(x => x.Icon).HasMaxLength(24);
            e.HasIndex(x => x.OwnerId);
        });
        modelBuilder.Entity<AgentToolLink>(e =>
        {
            e.ToTable("agt_agent_tool");
            e.Property(x => x.Approval).HasMaxLength(16);
            e.HasIndex(x => new { x.AgentId, x.ToolId }).IsUnique();
            e.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AgentBaseLink>(e =>
        {
            e.ToTable("agt_agent_base");
            e.HasIndex(x => new { x.AgentId, x.BaseId }).IsUnique();
            e.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ToolServer>(e =>
        {
            e.ToTable("agt_tool_server");
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Endpoint).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(16);
        });
        modelBuilder.Entity<Tool>(e =>
        {
            e.ToTable("agt_tool");
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Risk).HasMaxLength(16);
            e.HasIndex(x => x.ServerId);
            e.HasOne<ToolServer>().WithMany().HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AgentRun>(e =>
        {
            e.ToTable("agt_run");
            e.Property(x => x.Trigger).HasMaxLength(16);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.State).HasMaxLength(24);
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasIndex(x => new { x.AgentId, x.StartedAt });
            e.HasIndex(x => x.State);
            e.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RunStep>(e =>
        {
            e.ToTable("agt_run_step");
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(2_000);
            e.Property(x => x.State).HasMaxLength(16);
            e.HasIndex(x => new { x.RunId, x.Ordinal }).IsUnique();
            e.HasOne<AgentRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AgentApproval>(e =>
        {
            e.ToTable("agt_approval");
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(1_000);
            e.Property(x => x.ArgumentsJson).HasMaxLength(4_000);
            e.Property(x => x.Risk).HasMaxLength(16);
            e.Property(x => x.State).HasMaxLength(16);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => new { x.State, x.CreatedAt });
            e.HasIndex(x => x.RunId);
        });
    }
}

public sealed class Agent : IVersioned
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public required string Name { get; set; }

    public string Description { get; set; } = "";

    public string Instruction { get; set; } = "";

    public string ModelCode { get; set; } = "";

    public int MaxSteps { get; set; } = 20;

    /// <summary>The most one run may cost, in minor units; 0 means no limit.</summary>
    public long MaxCost { get; set; }

    public string Visibility { get; set; } = "workspace";

    /// <summary><c>draft</c> or <c>published</c>; only a published agent can be run.</summary>
    public string State { get; set; } = "draft";

    public string? Schedule { get; set; }

    public string Icon { get; set; } = "bot";

    public int RunCount { get; set; }

    public int SuccessCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; }
}

public sealed class AgentToolLink
{
    public Guid Id { get; set; }

    public Guid AgentId { get; set; }

    public Guid ToolId { get; set; }

    public bool Enabled { get; set; } = true;

    public string Approval { get; set; } = "auto";

    public int Sort { get; set; }
}

public sealed class AgentBaseLink
{
    public Guid Id { get; set; }

    public Guid AgentId { get; set; }

    public Guid BaseId { get; set; }
}

public sealed class ToolServer
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary><c>builtin</c>, <c>mcp</c> or <c>http</c>.</summary>
    public required string Kind { get; set; }

    public required string Endpoint { get; set; }

    public string Status { get; set; } = "unchecked";

    public DateTimeOffset? LastSeenAt { get; set; }
}

public sealed class Tool
{
    public Guid Id { get; set; }

    public Guid ServerId { get; set; }

    public required string Name { get; set; }

    public string Description { get; set; } = "";

    public string Risk { get; set; } = "write";

    public int Sort { get; set; }
}

public sealed class AgentRun : IVersioned
{
    public Guid Id { get; set; }

    public Guid AgentId { get; set; }

    public Guid? StartedBy { get; set; }

    public string Trigger { get; set; } = "manual";

    public string Subject { get; set; } = "";

    public string State { get; set; } = "queued";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public int Tokens { get; set; }

    public long Cost { get; set; }

    public string? Error { get; set; }

    /// <summary>The index of the next tool (of the agent's enabled tools, in order) the run has to deal with; a resumed run continues here.</summary>
    public int Cursor { get; set; }

    public int Version { get; set; }
}

public sealed class RunStep
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    public int Ordinal { get; set; }

    /// <summary><c>think</c>, <c>tool</c>, <c>reply</c> or <c>approval</c>.</summary>
    public string Kind { get; set; } = "think";

    public string Title { get; set; } = "";

    public string Detail { get; set; } = "";

    public string State { get; set; } = "done";

    public int AtMs { get; set; }
}

public sealed class AgentApproval
{
    public Guid Id { get; set; }

    /// <summary><c>agent</c>, <c>chat</c> or <c>flow</c>.</summary>
    public required string Source { get; set; }

    public Guid SourceId { get; set; }

    public Guid? RunId { get; set; }

    /// <summary>For an agent's run: the index of the tool the approval is for.</summary>
    public int ToolIndex { get; set; }

    public Guid? StepId { get; set; }

    public required string Title { get; set; }

    public string Detail { get; set; } = "";

    /// <summary>The arguments as shown to the approver, a JSON array of name/value pairs.</summary>
    public string ArgumentsJson { get; set; } = "[]";

    public string Risk { get; set; } = "external";

    /// <summary><c>pending</c>, <c>approved</c> or <c>rejected</c>.</summary>
    public string State { get; set; } = "pending";

    public string? Reason { get; set; }

    public Guid? RequestedBy { get; set; }

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}
