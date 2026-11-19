using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Models.Data;

/// <summary>
/// Providers and their models (tables <c>mdl_provider</c>, <c>mdl_model</c>). Provider neutral, like every feature's model;
/// <c>Data/Sqlite</c>, <c>Data/Postgres</c> and <c>Data/SqlServer</c> derive from it and own the migrations. A provider's key is stored
/// only as Data Protection cipher text (<see cref="AiProvider.KeyCipher"/>) and its last four characters; it is never returned.
/// </summary>
public abstract class ModelsDbContext(DbContextOptions options) : FeatureDbContext(options)
{
    public const string Name = "models";

    public DbSet<AiProvider> Providers => Set<AiProvider>();

    public DbSet<AiModel> ModelEntries => Set<AiModel>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiProvider>(e =>
        {
            e.ToTable("mdl_provider");
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Protocol).HasMaxLength(24);
            e.Property(x => x.BaseUrl).HasMaxLength(300);
            e.Property(x => x.KeyCipher).HasMaxLength(2000);
            e.Property(x => x.KeyHint).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => x.SortOrder);
        });
        modelBuilder.Entity<AiModel>(e =>
        {
            e.ToTable("mdl_model");
            e.Property(x => x.Code).HasMaxLength(120);
            e.Property(x => x.DisplayName).HasMaxLength(80);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Caps).HasMaxLength(200);
            e.Property(x => x.Efforts).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.ProviderId, x.Code }).IsUnique();
            e.HasOne<AiProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class AiProvider : IVersioned
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Protocol { get; set; }

    public required string BaseUrl { get; set; }

    /// <summary>The key, encrypted with Data Protection; null when none was set at run time.</summary>
    public string? KeyCipher { get; set; }

    /// <summary>The last four characters of the key, behind bullets (<c>••••••••4f2a</c>).</summary>
    public string? KeyHint { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Result of the last test: <c>unchecked</c>, <c>ok</c> or <c>error</c>.</summary>
    public string Status { get; set; } = "unchecked";

    public DateTimeOffset? LastCheckAt { get; set; }

    public int? LastLatencyMs { get; set; }

    public int SortOrder { get; set; }

    public int Version { get; set; }
}

public sealed class AiModel : IVersioned
{
    public Guid Id { get; set; }

    public Guid ProviderId { get; set; }

    public required string Code { get; set; }

    public required string DisplayName { get; set; }

    /// <summary><c>chat</c> or <c>embedding</c>.</summary>
    public string Kind { get; set; } = "chat";

    public int ContextTokens { get; set; }

    public long PriceIn { get; set; }

    public long PriceOut { get; set; }

    public long PriceCache { get; set; }

    /// <summary>Comma separated: vision, tools, reasoning, embedding, demo.</summary>
    public string Caps { get; set; } = "";

    /// <summary>Comma separated reasoning levels the model offers (deep, balanced, fast).</summary>
    public string Efforts { get; set; } = "";

    public string? Note { get; set; }

    public bool Enabled { get; set; } = true;

    public bool IsDefault { get; set; }

    public int Version { get; set; }
}
