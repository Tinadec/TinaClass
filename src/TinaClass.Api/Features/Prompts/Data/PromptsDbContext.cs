using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Prompts.Data;

/// <summary>
/// The prompt library (table prefix <c>pmt_</c>): a prompt keeps its draft, every publish adds an immutable version, and test cases check the draft.
/// Owners are plain ids.
/// </summary>
public abstract class PromptsDbContext(DbContextOptions options) : FeatureDbContext(options)
{
    public const string Name = "prompts";

    public DbSet<Prompt> Prompts => Set<Prompt>();

    public DbSet<PromptVersion> Versions => Set<PromptVersion>();

    public DbSet<PromptCase> Cases => Set<PromptCase>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Prompt>(e =>
        {
            e.ToTable("pmt_prompt");
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Command).HasMaxLength(33);
            e.Property(x => x.Visibility).HasMaxLength(16);
            e.Property(x => x.SystemText).HasMaxLength(8_000);
            e.Property(x => x.UserText).HasMaxLength(8_000);
            e.HasIndex(x => x.Command).IsUnique();
            e.HasIndex(x => x.OwnerId);
        });
        modelBuilder.Entity<PromptVersion>(e =>
        {
            e.ToTable("pmt_version");
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.SystemText).HasMaxLength(8_000);
            e.Property(x => x.UserText).HasMaxLength(8_000);
            e.HasIndex(x => new { x.PromptId, x.Number }).IsUnique();
            e.HasOne<Prompt>().WithMany().HasForeignKey(x => x.PromptId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PromptCase>(e =>
        {
            e.ToTable("pmt_case");
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Input).HasMaxLength(4_000);
            e.Property(x => x.Expect).HasMaxLength(500);
            e.HasIndex(x => x.PromptId);
            e.HasOne<Prompt>().WithMany().HasForeignKey(x => x.PromptId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class Prompt : IVersioned
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public required string Name { get; set; }

    /// <summary>The slash command, lower case with its slash (<c>/review</c>); unique.</summary>
    public required string Command { get; set; }

    /// <summary><c>private</c> (only the owner) or <c>workspace</c>.</summary>
    public string Visibility { get; set; } = "workspace";

    public string SystemText { get; set; } = "";

    public string UserText { get; set; } = "";

    /// <summary>The number of the last published version; 0 until the first publish.</summary>
    public int CurrentVersion { get; set; }

    public int UseCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; }
}

public sealed class PromptVersion
{
    public Guid Id { get; set; }

    public Guid PromptId { get; set; }

    public int Number { get; set; }

    public string Note { get; set; } = "";

    public string SystemText { get; set; } = "";

    public string UserText { get; set; } = "";

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PromptCase
{
    public Guid Id { get; set; }

    public Guid PromptId { get; set; }

    public required string Name { get; set; }

    /// <summary>The variables as a JSON object of strings.</summary>
    public string Input { get; set; } = "{}";

    /// <summary>Text the answer must contain (compared without regard to case).</summary>
    public string Expect { get; set; } = "";

    /// <summary>The result of the last run of all cases; null before one.</summary>
    public bool? LastPassed { get; set; }

    public int Sort { get; set; }
}
