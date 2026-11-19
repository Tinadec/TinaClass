using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Chat.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;

namespace TinaClass.Api.Features.Chat;

/// <summary>
/// Conversations, folders, spaces, search and shared links. A conversation belongs to the person who made it; someone with
/// <see cref="Permissions.ChatHistoryManage"/> may also read and delete others' (a conversation of another person that you may not see is a 404,
/// not a 403, so that its existence is not revealed). Generating an answer is in <see cref="ChatGenerator"/>.
/// </summary>
public sealed class ChatService(ChatDbContext db, IActorContext actors, IKnowledgeRetriever retriever, IAuditLog audit, IEventPublisher events, TimeProvider time)
{
    public const int MaxFolderDepth = 4;
    public const string NewTitle = "New chat";

    internal async Task<Conversation> FindAsync(Guid id, bool ownerOnly, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, cancellationToken);
        if (conversation is null || (conversation.OwnerId != actor.Id && (ownerOnly || !actor.Can(Permissions.ChatHistoryManage))))
        {
            throw ApiException.NotFound("The conversation");
        }
        return conversation;
    }

    // ---- conversations

    public async Task<Paged<ConversationDto>> ListAsync(string? q, Guid? folderId, Guid? spaceId, bool? archived, bool all, string? sort, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var paging = PageRequest.Of(page, pageSize);
        var query = db.Conversations.AsNoTracking().Where(c => c.DeletedAt == null);
        query = all && actor.Can(Permissions.ChatHistoryManage) ? query : query.Where(c => c.OwnerId == actor.Id);
        query = query.Where(c => c.Archived == (archived ?? false));
        if (folderId is { } folder)
        {
            query = query.Where(c => c.FolderId == folder);
        }
        if (spaceId is { } space)
        {
            query = query.Where(c => c.SpaceId == space);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(c => c.Title.ToLower().Contains(needle));
        }
        var total = await query.CountAsync(cancellationToken);
        var ordered = sort switch
        {
            "title" => query.OrderBy(c => c.Title).ThenBy(c => c.Id),
            "updatedAt" => query.OrderBy(c => c.UpdatedAt).ThenBy(c => c.Id),
            _ => query.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id),
        };
        var rows = await ordered.Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellationToken);
        return paging.Wrap<ConversationDto>([.. rows.Select(ChatMapping.ToDto)], total);
    }

    public async Task<ConversationDto> CreateAsync(CreateConversationRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var validator = new Validator().Text("title", request.Title, 200, required: false);
        validator.ThrowIfInvalid();
        if (request.FolderId is { } folderId && !await db.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == actor.Id, cancellationToken))
        {
            throw ApiException.Invalid("folderId", "The folder was not found.");
        }
        if (request.SpaceId is { } spaceId && !await db.Spaces.AnyAsync(s => s.Id == spaceId && s.OwnerId == actor.Id, cancellationToken))
        {
            throw ApiException.Invalid("spaceId", "The space was not found.");
        }
        var now = time.GetUtcNow();
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), OwnerId = actor.Id!.Value, FolderId = request.FolderId, SpaceId = request.SpaceId, ModelCode = request.ModelCode,
            Title = string.IsNullOrWhiteSpace(request.Title) ? NewTitle : request.Title.Trim(), TitleSetByUser = !string.IsNullOrWhiteSpace(request.Title), CreatedAt = now, UpdatedAt = now,
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        var dto = ChatMapping.ToDto(conversation);
        await events.PublishAsync(ChatEvents.ConversationCreated, dto, ContractsJsonContext.Default.ConversationDto, cancellationToken);
        return dto;
    }

    public async Task<ConversationDto> GetAsync(Guid id, CancellationToken cancellationToken) => ChatMapping.ToDto(await FindAsync(id, ownerOnly: false, cancellationToken));

    public async Task<ConversationDto> UpdateAsync(Guid id, UpdateConversationRequest request, CancellationToken cancellationToken)
    {
        var conversation = await FindAsync(id, ownerOnly: false, cancellationToken);
        var validator = new Validator();
        if (request.Title is not null)
        {
            validator.Text("title", request.Title, 200);
        }
        validator.ThrowIfInvalid();
        if (request.FolderId is { } folderId && !await db.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == conversation.OwnerId, cancellationToken))
        {
            throw ApiException.Invalid("folderId", "The folder was not found.");
        }
        db.ExpectVersion(conversation, request.Version);
        if (request.Title is not null)
        {
            conversation.Title = request.Title.Trim();
            conversation.TitleSetByUser = true;
        }
        if (request.MoveToRoot)
        {
            conversation.FolderId = null;
        }
        else if (request.FolderId is { } target)
        {
            conversation.FolderId = target;
        }
        conversation.Pinned = request.Pinned ?? conversation.Pinned;
        conversation.Archived = request.Archived ?? conversation.Archived;
        conversation.ModelCode = request.ModelCode ?? conversation.ModelCode;
        if (request.ActiveLeafId is { } leaf)
        {
            if (!await db.Messages.AnyAsync(m => m.Id == leaf && m.ConversationId == id, cancellationToken))
            {
                throw ApiException.Invalid("activeLeafId", "The message is not part of this conversation.");
            }
            conversation.ActiveLeafId = leaf;
        }
        await db.SaveChangesAsync(cancellationToken);
        return ChatMapping.ToDto(conversation);
    }

    /// <summary>A soft delete: the conversation disappears at once and is purged later (<see cref="ChatPurgeJob"/>).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await FindAsync(id, ownerOnly: false, cancellationToken);
        conversation.DeletedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(ChatAuditActions.ConversationDeleted, "conversation", id.ToString(), null), cancellationToken);
    }

    // ---- messages

    /// <summary>The messages of one branch, root first. <paramref name="leaf"/> may be any message: the branch continues below it along the newest answers.</summary>
    public async Task<IReadOnlyList<MessageDto>> MessagesAsync(Guid conversationId, Guid? leaf, CancellationToken cancellationToken)
    {
        var conversation = await FindAsync(conversationId, ownerOnly: false, cancellationToken);
        return await BranchAsync(conversation, leaf ?? conversation.ActiveLeafId, cancellationToken);
    }

    internal async Task<IReadOnlyList<MessageDto>> BranchAsync(Conversation conversation, Guid? leaf, CancellationToken cancellationToken)
    {
        var all = await db.Messages.AsNoTracking().Where(m => m.ConversationId == conversation.Id).ToListAsync(cancellationToken);
        if (all.Count == 0)
        {
            return [];
        }
        var byId = all.ToDictionary(m => m.Id);
        var children = all.Where(m => m.ParentId is not null).GroupBy(m => m.ParentId!.Value)
                          .ToDictionary(g => g.Key, g => g.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList());
        var current = leaf is { } l && byId.TryGetValue(l, out var found) ? found : all.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id).First();
        while (children.TryGetValue(current.Id, out var below))
        {
            current = below[^1];
        }
        var path = new List<ChatMessage>();
        for (ChatMessage? node = current; node is not null; node = node.ParentId is { } p && byId.TryGetValue(p, out var parent) ? parent : null)
        {
            path.Add(node);
        }
        path.Reverse();
        var roots = all.Where(m => m.ParentId is null).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();
        var ids = path.Select(m => m.Id).ToList();
        var citations = (await db.Citations.AsNoTracking().Where(c => ids.Contains(c.MessageId)).ToListAsync(cancellationToken))
                        .OrderBy(c => c.Position).ThenBy(c => c.Id).GroupBy(c => c.MessageId).ToDictionary(g => g.Key, g => (IReadOnlyList<CitationDto>)[.. g.Select(ChatMapping.ToDto)]);
        var result = new List<MessageDto>();
        foreach (var message in path)
        {
            var siblings = (message.ParentId is { } parentId ? children[parentId] : roots).Where(m => m.Role == message.Role).ToList();
            result.Add(ChatMapping.ToDto(message, siblings.FindIndex(m => m.Id == message.Id), siblings.Count, citations.GetValueOrDefault(message.Id, [])));
        }
        return result;
    }

    public async Task FeedbackAsync(Guid messageId, FeedbackRequest request, CancellationToken cancellationToken)
    {
        if (request.Value is not ("up" or "down" or ""))
        {
            throw ApiException.Invalid("value", "Use up, down or an empty value to clear it.");
        }
        var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken) ?? throw ApiException.NotFound("The message");
        await FindAsync(message.ConversationId, ownerOnly: true, cancellationToken);
        if (message.Role != MessageRoles.Assistant)
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "Only an answer can be rated.");
        }
        message.Feedback = request.Value.Length == 0 ? null : request.Value;
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- folders and spaces

    public async Task<IReadOnlyList<FolderDto>> FoldersAsync(CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var folders = await db.Folders.AsNoTracking().Where(f => f.OwnerId == actor.Id).OrderBy(f => f.Sort).ThenBy(f => f.Name).ThenBy(f => f.Id).ToListAsync(cancellationToken);
        var counts = await db.Conversations.AsNoTracking().Where(c => c.OwnerId == actor.Id && c.DeletedAt == null && c.FolderId != null)
                             .GroupBy(c => c.FolderId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return [.. folders.Select(f => new FolderDto(f.Id, f.ParentId, f.Name, f.Sort, counts.GetValueOrDefault(f.Id)))];
    }

    public async Task<FolderDto> CreateFolderAsync(CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        new Validator().Text("name", request.Name, 120).ThrowIfInvalid();
        var mine = await db.Folders.Where(f => f.OwnerId == actor.Id).ToListAsync(cancellationToken);
        if (request.ParentId is { } parent && mine.All(f => f.Id != parent))
        {
            throw ApiException.Invalid("parentId", "The folder was not found.");
        }
        if (Depth(mine, request.ParentId) + 1 > MaxFolderDepth)
        {
            throw ApiException.Invalid("parentId", $"Folders go at most {MaxFolderDepth} levels deep.");
        }
        var folder = new ChatFolder { Id = Guid.NewGuid(), OwnerId = actor.Id!.Value, ParentId = request.ParentId, Name = request.Name.Trim(), Sort = (mine.Count == 0 ? 0 : mine.Max(f => f.Sort)) + 1 };
        db.Folders.Add(folder);
        await db.SaveChangesAsync(cancellationToken);
        return new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Sort, 0);
    }

    public async Task<FolderDto> UpdateFolderAsync(Guid id, UpdateFolderRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var mine = await db.Folders.Where(f => f.OwnerId == actor.Id).ToListAsync(cancellationToken);
        var folder = mine.SingleOrDefault(f => f.Id == id) ?? throw ApiException.NotFound("The folder");
        if (request.Name is not null)
        {
            new Validator().Text("name", request.Name, 120).ThrowIfInvalid();
            folder.Name = request.Name.Trim();
        }
        if (request.MoveToRoot)
        {
            folder.ParentId = null;
        }
        else if (request.ParentId is { } parent)
        {
            if (mine.All(f => f.Id != parent) || parent == id || IsInside(mine, parent, id))
            {
                throw ApiException.Invalid("parentId", "A folder cannot go into itself or a folder below it.");
            }
            var height = Height(mine, id);
            if (Depth(mine, parent) + height > MaxFolderDepth)
            {
                throw ApiException.Invalid("parentId", $"Folders go at most {MaxFolderDepth} levels deep.");
            }
            folder.ParentId = parent;
        }
        await db.SaveChangesAsync(cancellationToken);
        var count = await db.Conversations.CountAsync(c => c.FolderId == id && c.DeletedAt == null, cancellationToken);
        return new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Sort, count);
    }

    /// <summary>Deletes the folder and the folders below it; their conversations go back to the top level.</summary>
    public async Task DeleteFolderAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var mine = await db.Folders.Where(f => f.OwnerId == actor.Id).ToListAsync(cancellationToken);
        var folder = mine.SingleOrDefault(f => f.Id == id) ?? throw ApiException.NotFound("The folder");
        var doomed = new List<Guid> { folder.Id };
        for (var i = 0; i < doomed.Count; i++)
        {
            doomed.AddRange(mine.Where(f => f.ParentId == doomed[i]).Select(f => f.Id));
        }
        await db.Conversations.Where(c => c.FolderId != null && doomed.Contains(c.FolderId.Value)).ExecuteUpdateAsync(s => s.SetProperty(c => c.FolderId, (Guid?)null), cancellationToken);
        db.Folders.RemoveRange(mine.Where(f => doomed.Contains(f.Id)));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static int Depth(List<ChatFolder> folders, Guid? id)
    {
        var depth = 0;
        for (var node = id; node is not null && depth < 50; depth++)
        {
            node = folders.FirstOrDefault(f => f.Id == node)?.ParentId;
        }
        return depth;
    }

    private static int Height(List<ChatFolder> folders, Guid id) => 1 + (folders.Where(f => f.ParentId == id).Select(f => Height(folders, f.Id)).DefaultIfEmpty(0).Max());

    private static bool IsInside(List<ChatFolder> folders, Guid candidate, Guid ancestor)
    {
        for (Guid? node = candidate; node is not null;)
        {
            if (node == ancestor)
            {
                return true;
            }
            node = folders.FirstOrDefault(f => f.Id == node)?.ParentId;
        }
        return false;
    }

    public async Task<IReadOnlyList<SpaceDto>> SpacesAsync(CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var spaces = await db.Spaces.AsNoTracking().Where(s => s.OwnerId == actor.Id).OrderBy(s => s.Name).ThenBy(s => s.Id).ToListAsync(cancellationToken);
        var counts = await db.Conversations.AsNoTracking().Where(c => c.OwnerId == actor.Id && c.DeletedAt == null && c.SpaceId != null)
                             .GroupBy(c => c.SpaceId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return [.. spaces.Select(s => new SpaceDto(s.Id, s.Name, s.Instruction, counts.GetValueOrDefault(s.Id)))];
    }

    public async Task<SpaceDto> CreateSpaceAsync(CreateSpaceRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        new Validator().Text("name", request.Name, 80).Text("instruction", request.Instruction, 2000, required: false).ThrowIfInvalid();
        var space = new ChatSpace { Id = Guid.NewGuid(), OwnerId = actor.Id!.Value, Name = request.Name.Trim(), Instruction = request.Instruction?.Trim() ?? "" };
        db.Spaces.Add(space);
        await db.SaveChangesAsync(cancellationToken);
        return new SpaceDto(space.Id, space.Name, space.Instruction, 0);
    }

    // ---- search, citations, sharing

    public async Task<IReadOnlyList<SearchResultDto>> SearchAsync(string? q, string? scope, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(q))
        {
            return [];
        }
        var needle = q.Trim().ToLower();
        var scopes = (scope ?? "conversations,knowledge").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new List<SearchResultDto>();
        if (scopes.Contains("conversations"))
        {
            var byTitle = await db.Conversations.AsNoTracking().Where(c => c.OwnerId == actor.Id && c.DeletedAt == null && c.Title.ToLower().Contains(needle))
                                  .OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id).Take(8).ToListAsync(cancellationToken);
            results.AddRange(byTitle.Select(c => new SearchResultDto("conversation", c.Id, c.Title, "")));
            var byText = await (from m in db.Messages.AsNoTracking() join c in db.Conversations.AsNoTracking() on m.ConversationId equals c.Id
                                where c.OwnerId == actor.Id && c.DeletedAt == null && m.SearchText.ToLower().Contains(needle)
                                orderby m.CreatedAt descending, m.Id select new { c.Id, c.Title, m.SearchText }).Take(8).ToListAsync(cancellationToken);
            foreach (var hit in byText.Where(h => results.All(r => r.Id != h.Id)))
            {
                results.Add(new SearchResultDto("conversation", hit.Id, hit.Title, Snippet(hit.SearchText, needle)));
            }
        }
        if (scopes.Contains("knowledge") && actor.Can(Permissions.ChatUse))
        {
            foreach (var chunk in await retriever.SearchAsync(q.Trim(), [], 5, cancellationToken))
            {
                results.Add(new SearchResultDto("knowledge", chunk.DocumentId, chunk.DocumentName, Snippet(chunk.Text, needle)));
            }
        }
        return results;
    }

    private static string Snippet(string text, string needle)
    {
        var at = text.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal);
        var start = Math.Max(0, at - 30);
        return (start > 0 ? "…" : "") + text.Substring(start, Math.Min(120, text.Length - start)).ReplaceLineEndings(" ");
    }

    public async Task<CitationDetailDto> CitationAsync(Guid id, CancellationToken cancellationToken)
    {
        var citation = await db.Citations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw ApiException.NotFound("The citation");
        var message = await db.Messages.AsNoTracking().SingleAsync(m => m.Id == citation.MessageId, cancellationToken);
        await FindAsync(message.ConversationId, ownerOnly: false, cancellationToken);
        var context = await retriever.GetChunkAsync(citation.ChunkId, cancellationToken);
        return new CitationDetailDto(citation.Id, citation.DocumentName, citation.Where, context?.Text ?? citation.Quote, context?.Before ?? "", context?.After ?? "", citation.Score);
    }

    public async Task<ShareDto> ShareAsync(Guid conversationId, CreateShareRequest request, CancellationToken cancellationToken)
    {
        var conversation = await FindAsync(conversationId, ownerOnly: true, cancellationToken);
        if (request.ExpiresInDays is < 1 or > 365)
        {
            throw ApiException.Invalid("expiresInDays", "Use 1 to 365 days, or leave it empty for a link that does not expire.");
        }
        var messages = await BranchAsync(conversation, conversation.ActiveLeafId, cancellationToken);
        if (messages.Count == 0)
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "There is nothing to share yet.");
        }
        var now = time.GetUtcNow();
        var token = Secrets.Token(24);
        var share = new ChatShare
        {
            Id = Guid.NewGuid(), ConversationId = conversation.Id, OwnerId = conversation.OwnerId, TokenHash = Secrets.Hash(token), Title = conversation.Title, CreatedAt = now,
            ExpiresAt = request.ExpiresInDays is { } days ? now.AddDays(days) : null,
            SnapshotJson = JsonSerializer.Serialize<IReadOnlyList<MessageDto>>([.. messages.Select(ChatMapping.ForSharing)], ContractsJsonContext.Default.IReadOnlyListMessageDto),
        };
        db.Shares.Add(share);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(ChatAuditActions.Shared, "conversation", conversation.Id.ToString(), null), cancellationToken);
        return new ShareDto(share.Id, token, share.ExpiresAt);
    }

    public async Task RevokeShareAsync(Guid id, CancellationToken cancellationToken)
    {
        var share = await db.Shares.SingleOrDefaultAsync(s => s.Id == id && s.RevokedAt == null, cancellationToken) ?? throw ApiException.NotFound("The link");
        await FindAsync(share.ConversationId, ownerOnly: false, cancellationToken);
        share.RevokedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(ChatAuditActions.ShareRevoked, "conversation", share.ConversationId.ToString(), null), cancellationToken);
    }

    /// <summary>The snapshot behind a link, for anyone who has it (no sign-in). A revoked, expired or unknown link is the same 404.</summary>
    public async Task<SharedConversationDto> SharedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Secrets.Hash(token);
        var share = await db.Shares.AsNoTracking().SingleOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
        if (share is null || share.RevokedAt is not null || (share.ExpiresAt is { } expires && expires <= time.GetUtcNow()))
        {
            throw ApiException.NotFound("The link");
        }
        var messages = JsonSerializer.Deserialize(share.SnapshotJson, ContractsJsonContext.Default.IReadOnlyListMessageDto) ?? [];
        return new SharedConversationDto(share.Title, share.CreatedAt, messages);
    }
}
