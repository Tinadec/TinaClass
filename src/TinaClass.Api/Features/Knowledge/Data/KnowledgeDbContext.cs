using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Knowledge.Data;

/// <summary>
/// Knowledge bases (table prefix <c>kb_</c>): the base and its retrieval settings, its documents (the index state machine) and the chunks
/// retrieval finds. Vectors are not stored: <see cref="IVectorIndex"/> computes them from the chunk text. People are kept as plain ids.
/// </summary>
public abstract class KnowledgeDbContext(DbContextOptions options) : FeatureDbContext(options)
{
    public const string Name = "knowledge";

    /// <summary>The largest text of one document the index accepts (characters).</summary>
    public const int MaxContent = 200_000;

    public DbSet<KbBase> Bases => Set<KbBase>();

    public DbSet<KbDocument> Documents => Set<KbDocument>();

    public DbSet<KbChunk> Chunks => Set<KbChunk>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KbBase>(e =>
        {
            e.ToTable("kb_base");
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.EmbeddingModelCode).HasMaxLength(80);
            e.Property(x => x.Visibility).HasMaxLength(16);
            e.HasIndex(x => x.OwnerId);
        });
        modelBuilder.Entity<KbDocument>(e =>
        {
            e.ToTable("kb_document");
            e.Property(x => x.Name).HasMaxLength(260);
            e.Property(x => x.Mime).HasMaxLength(120);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.Property(x => x.State).HasMaxLength(16);
            e.Property(x => x.Error).HasMaxLength(500);
            e.Property(x => x.Content).HasMaxLength(MaxContent);
            e.HasIndex(x => new { x.BaseId, x.Hash }).IsUnique();
            e.HasIndex(x => x.State);
            e.HasOne<KbBase>().WithMany().HasForeignKey(x => x.BaseId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<KbChunk>(e =>
        {
            e.ToTable("kb_chunk");
            e.Property(x => x.Text).HasMaxLength(4_000);
            e.Property(x => x.Heading).HasMaxLength(200);
            e.HasIndex(x => new { x.DocumentId, x.Ordinal });
            e.HasIndex(x => x.BaseId);
            e.HasOne<KbDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class KbBase : IVersioned
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public required string Name { get; set; }

    public string Description { get; set; } = "";

    public string EmbeddingModelCode { get; set; } = KnowledgeDefaults.EmbeddingModel;

    public int ChunkSize { get; set; } = KnowledgeDefaults.ChunkSize;

    public int ChunkOverlap { get; set; } = KnowledgeDefaults.ChunkOverlap;

    public bool Hybrid { get; set; } = true;

    public bool Rerank { get; set; }

    public int ThresholdPercent { get; set; } = KnowledgeDefaults.ThresholdPercent;

    public int TopK { get; set; } = KnowledgeDefaults.TopK;

    public string Visibility { get; set; } = "workspace";

    public DateTimeOffset CreatedAt { get; set; }

    public int Version { get; set; }
}

public sealed class KbDocument
{
    public Guid Id { get; set; }

    public Guid BaseId { get; set; }

    public required string Name { get; set; }

    public string Mime { get; set; } = "text/plain";

    public long Size { get; set; }

    /// <summary>SHA-256 of the text (or of name and size when there is none): the same file is not added twice to a base.</summary>
    public required string Hash { get; set; }

    public string State { get; set; } = "queued";

    public int Progress { get; set; }

    public int ChunkCount { get; set; }

    public string? Error { get; set; }

    public bool Pinned { get; set; }

    public Guid UploaderId { get; set; }

    /// <summary>The extracted text, kept so that a reindex does not need the file again.</summary>
    public string? Content { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class KbChunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    /// <summary>The base, as a plain id: retrieval filters by it without joining the documents.</summary>
    public Guid BaseId { get; set; }

    public int Ordinal { get; set; }

    public required string Text { get; set; }

    public int? Page { get; set; }

    public string? Heading { get; set; }

    public int TokenCount { get; set; }
}

public static class KnowledgeDefaults
{
    public const string EmbeddingModel = "builtin-hash-256";
    public const int ChunkSize = 800;
    public const int ChunkOverlap = 80;
    public const int ThresholdPercent = 20;
    public const int TopK = 5;
}
