using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Features.Chat.Data;

/// <summary>
/// Conversations (table prefix <c>chat_</c>): folders, spaces, the message tree, citations and share links. Provider neutral, like every
/// feature's model; <c>Data/Sqlite</c>, <c>Data/Postgres</c> and <c>Data/SqlServer</c> derive from it and own the migrations. Owners, documents
/// and models of other features are kept as plain ids (no foreign key, no reference to their types).
/// </summary>
public abstract class ChatDbContext(DbContextOptions options) : FeatureDbContext(options)
{
    public const string Name = "chat";

    /// <summary>The largest message the chat stores (a long text column; it is validated before it is written).</summary>
    public const int MaxContent = 200_000;

    public DbSet<ChatFolder> Folders => Set<ChatFolder>();

    public DbSet<ChatSpace> Spaces => Set<ChatSpace>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ChatMessage> Messages => Set<ChatMessage>();

    public DbSet<ChatCitation> Citations => Set<ChatCitation>();

    public DbSet<ChatShare> Shares => Set<ChatShare>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatFolder>(e =>
        {
            e.ToTable("chat_folder");
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.OwnerId, x.ParentId });
        });
        modelBuilder.Entity<ChatSpace>(e =>
        {
            e.ToTable("chat_space");
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Instruction).HasMaxLength(2000);
            e.HasIndex(x => x.OwnerId);
        });
        modelBuilder.Entity<Conversation>(e =>
        {
            e.ToTable("chat_conversation");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.ModelCode).HasMaxLength(120);
            e.Property(x => x.ParamsJson).HasMaxLength(2000);
            e.HasIndex(x => new { x.OwnerId, x.UpdatedAt });
            e.HasIndex(x => x.FolderId);
        });
        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.ToTable("chat_message");
            e.Property(x => x.Role).HasMaxLength(16);
            e.Property(x => x.ContentJson).HasMaxLength(MaxContent);
            e.Property(x => x.SearchText).HasMaxLength(MaxContent);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Error).HasMaxLength(500);
            e.Property(x => x.ModelCode).HasMaxLength(120);
            e.Property(x => x.UsageJson).HasMaxLength(1000);
            e.Property(x => x.Feedback).HasMaxLength(8);
            e.HasIndex(x => new { x.ConversationId, x.ParentId });
            // A conversation owns its messages; the tree (ParentId) is an id only, so that deleting the conversation is one cascade path.
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ChatCitation>(e =>
        {
            e.ToTable("chat_citation");
            e.Property(x => x.Quote).HasMaxLength(1000);
            e.Property(x => x.DocumentName).HasMaxLength(260);
            e.Property(x => x.Where).HasMaxLength(200);
            e.HasIndex(x => new { x.MessageId, x.Position });
            e.HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ChatShare>(e =>
        {
            e.ToTable("chat_share");
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.SnapshotJson).HasMaxLength(MaxContent * 2);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.ConversationId);
        });
    }
}

public sealed class ChatFolder : IVersioned
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? ParentId { get; set; }

    public required string Name { get; set; }

    public int Sort { get; set; }

    public int Version { get; set; }
}

public sealed class ChatSpace : IVersioned
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public required string Name { get; set; }

    public string Instruction { get; set; } = "";

    public int Version { get; set; }
}

public sealed class Conversation : IVersioned
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public Guid? SpaceId { get; set; }

    public Guid? FolderId { get; set; }

    public required string Title { get; set; }

    /// <summary>A title a person typed is never replaced by the automatic one.</summary>
    public bool TitleSetByUser { get; set; }

    public bool Pinned { get; set; }

    public bool Archived { get; set; }

    public string? ModelCode { get; set; }

    public string? ParamsJson { get; set; }

    /// <summary>The leaf of the branch that is shown; switching to another answer moves it.</summary>
    public Guid? ActiveLeafId { get; set; }

    public long TokenTotal { get; set; }

    public long CostTotal { get; set; }

    /// <summary>Set by a delete: the conversation is hidden at once and purged after <c>Chat:PurgeAfterDays</c>.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; }
}

public sealed class ChatMessage : IVersioned
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid? ParentId { get; set; }

    public required string Role { get; set; }

    /// <summary>The blocks (text, reasoning, plan, tool_call) as JSON.</summary>
    public required string ContentJson { get; set; }

    /// <summary>The plain text of the blocks, for search.</summary>
    public string SearchText { get; set; } = "";

    public required string Status { get; set; }

    public string? Error { get; set; }

    public string? ModelCode { get; set; }

    public string? UsageJson { get; set; }

    public string? Feedback { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int Version { get; set; }
}

public sealed class ChatCitation
{
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }

    public int Position { get; set; }

    public Guid DocumentId { get; set; }

    public Guid ChunkId { get; set; }

    public double Score { get; set; }

    public required string Quote { get; set; }

    /// <summary>The document's name when the answer was written: a citation still reads right after the document is renamed or deleted.</summary>
    public required string DocumentName { get; set; }

    public string? Where { get; set; }
}

public sealed class ChatShare : IVersioned
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid OwnerId { get; set; }

    /// <summary>SHA-256 of the link's token: the token itself is shown once and not stored.</summary>
    public required string TokenHash { get; set; }

    public required string Title { get; set; }

    public required string SnapshotJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public int Version { get; set; }
}
