using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Workflows.Data;

/// <summary>
/// Workflows (table prefix <c>wf_</c>): the flow with its draft graph, immutable published versions, runs and the run of each node (what the run trace shows).
/// A graph is kept as JSON, as the canvas sends it.
/// </summary>
public abstract class WorkflowsDbContext(DbContextOptions options) : FeatureDbContext(options)
{
    public const string Name = "workflows";

    public const int MaxGraph = 100_000;

    public DbSet<Flow> Flows => Set<Flow>();

    public DbSet<FlowVersion> Versions => Set<FlowVersion>();

    public DbSet<FlowRun> Runs => Set<FlowRun>();

    public DbSet<FlowNodeRun> NodeRuns => Set<FlowNodeRun>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Flow>(e =>
        {
            e.ToTable("wf_flow");
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.DraftJson).HasMaxLength(MaxGraph);
        });
        modelBuilder.Entity<FlowVersion>(e =>
        {
            e.ToTable("wf_flow_version");
            e.Property(x => x.GraphJson).HasMaxLength(MaxGraph);
            e.HasIndex(x => new { x.FlowId, x.Number }).IsUnique();
            e.HasOne<Flow>().WithMany().HasForeignKey(x => x.FlowId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<FlowRun>(e =>
        {
            e.ToTable("wf_run");
            e.Property(x => x.State).HasMaxLength(24);
            e.Property(x => x.Input).HasMaxLength(4_000);
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasIndex(x => new { x.FlowId, x.StartedAt });
            e.HasIndex(x => x.Ordinal).IsUnique();
            e.HasOne<Flow>().WithMany().HasForeignKey(x => x.FlowId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<FlowNodeRun>(e =>
        {
            e.ToTable("wf_node_run");
            e.Property(x => x.NodeKey).HasMaxLength(60);
            e.Property(x => x.State).HasMaxLength(16);
            e.Property(x => x.Output).HasMaxLength(2_000);
            e.HasIndex(x => new { x.RunId, x.NodeKey }).IsUnique();
            e.HasOne<FlowRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class Flow : IVersioned
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public required string Name { get; set; }

    public string DraftJson { get; set; } = "{}";

    /// <summary>The number of the last published version; 0 until the first publish.</summary>
    public int PublishedVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; }
}

public sealed class FlowVersion
{
    public Guid Id { get; set; }

    public Guid FlowId { get; set; }

    public int Number { get; set; }

    public required string GraphJson { get; set; }

    public Guid PublishedBy { get; set; }

    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class FlowRun
{
    public Guid Id { get; set; }

    public Guid FlowId { get; set; }

    public int FlowVersion { get; set; }

    /// <summary>The number the run trace shows (#842), counted over all flows.</summary>
    public int Ordinal { get; set; }

    /// <summary><c>running</c>, <c>waiting</c> (for an approval), <c>succeeded</c> or <c>failed</c>.</summary>
    public string State { get; set; } = "running";

    public string Input { get; set; } = "";

    public Guid? StartedBy { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public long Cost { get; set; }

    public string? Error { get; set; }
}

public sealed class FlowNodeRun
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    public required string NodeKey { get; set; }

    public string State { get; set; } = "pending";

    public int StartMs { get; set; }

    public int DurationMs { get; set; }

    public string? Output { get; set; }
}
