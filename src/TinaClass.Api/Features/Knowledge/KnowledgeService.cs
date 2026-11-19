using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Auditing;
using TinaClass.Api.Common.Http;
using TinaClass.Api.Common.Persistence;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Knowledge.Data;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Knowledge;

namespace TinaClass.Api.Features.Knowledge;

public static class KnowledgeAudit
{
    public const string BaseCreated = "knowledge.base_created";
    public const string BaseUpdated = "knowledge.base_updated";
    public const string BaseDeleted = "knowledge.base_deleted";
    public const string DocumentAdded = "knowledge.document_added";
    public const string DocumentDeleted = "knowledge.document_deleted";
    public const string DocumentReindexed = "knowledge.document_reindexed";
}

/// <summary>Which bases an actor may see: workspace and space bases, and the private ones of their owner. The background (no actor) sees what it is asked for.</summary>
public static class BaseVisibility
{
    public static readonly string[] All = ["private", "space", "workspace"];

    public static IQueryable<KbBase> For(this IQueryable<KbBase> bases, Actor? actor)
    {
        if (actor is null || actor.Type == ActorType.System)
        {
            return bases;
        }
        var id = actor.Id;
        return bases.Where(b => b.Visibility != "private" || b.OwnerId == id);
    }
}

/// <summary>Bases, documents and the retrieval test (design 4.2).</summary>
public sealed class KnowledgeService(KnowledgeDbContext db, IActorContext actors, IUserDirectory users, IVectorIndex vectors, IAuditLog audit, TimeProvider time)
{
    private static readonly string[] Visibilities = BaseVisibility.All;

    public async Task<IReadOnlyList<KnowledgeBaseDto>> ListAsync(CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var bases = await db.Bases.AsNoTracking().For(actor).OrderBy(b => b.Name).ThenBy(b => b.Id).ToListAsync(cancellationToken);
        var ids = bases.Select(b => b.Id).ToList();
        var counts = await db.Documents.AsNoTracking().Where(d => ids.Contains(d.BaseId)).GroupBy(d => d.BaseId)
            .Select(g => new { g.Key, Documents = g.Count(), Chunks = g.Sum(d => d.ChunkCount), Busy = g.Count(d => d.State != DocumentStates.Ready && d.State != DocumentStates.Failed) })
            .ToDictionaryAsync(x => x.Key, cancellationToken);
        return [.. bases.Select(b => ToDto(b, counts.GetValueOrDefault(b.Id)?.Documents ?? 0, counts.GetValueOrDefault(b.Id)?.Chunks ?? 0, counts.GetValueOrDefault(b.Id)?.Busy ?? 0))];
    }

    public async Task<KnowledgeBaseDto> CreateAsync(CreateKnowledgeBaseRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var size = request.ChunkSize ?? KnowledgeDefaults.ChunkSize;
        var overlap = request.ChunkOverlap ?? KnowledgeDefaults.ChunkOverlap;
        var validator = new Validator().Text("name", request.Name, 80).Text("description", request.Description, 300, required: false).Text("embeddingModelCode", request.EmbeddingModelCode, 80, required: false);
        ValidateChunking(validator, size, overlap);
        ValidateVisibility(validator, request.Visibility);
        validator.ThrowIfInvalid();
        var kb = new KbBase
        {
            Id = Guid.NewGuid(), OwnerId = actor.Id ?? Guid.Empty, Name = request.Name.Trim(), Description = request.Description?.Trim() ?? "", ChunkSize = size, ChunkOverlap = overlap,
            EmbeddingModelCode = string.IsNullOrWhiteSpace(request.EmbeddingModelCode) ? KnowledgeDefaults.EmbeddingModel : request.EmbeddingModelCode.Trim(),
            Visibility = request.Visibility ?? "workspace", CreatedAt = time.GetUtcNow(),
        };
        db.Bases.Add(kb);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(KnowledgeAudit.BaseCreated, "knowledge_base", kb.Id.ToString(), kb.Name), cancellationToken);
        return ToDto(kb, 0, 0, 0);
    }

    public async Task<KnowledgeBaseDto> UpdateAsync(Guid id, UpdateKnowledgeBaseRequest request, CancellationToken cancellationToken)
    {
        var kb = await FindBaseAsync(id, tracking: true, cancellationToken);
        var validator = new Validator();
        if (request.Name is not null)
        {
            validator.Text("name", request.Name, 80);
        }
        if (request.Description is not null)
        {
            validator.Text("description", request.Description, 300, required: false);
        }
        var size = request.ChunkSize ?? kb.ChunkSize;
        var overlap = request.ChunkOverlap ?? kb.ChunkOverlap;
        ValidateChunking(validator, size, overlap);
        if (request.ThresholdPercent is < 0 or > 100)
        {
            validator.Add("thresholdPercent", "Use 0 to 100.");
        }
        if (request.TopK is < 1 or > 20)
        {
            validator.Add("topK", "Use 1 to 20.");
        }
        ValidateVisibility(validator, request.Visibility);
        validator.ThrowIfInvalid();
        db.ExpectVersion(kb, request.Version);
        var rechunk = size != kb.ChunkSize || overlap != kb.ChunkOverlap;
        kb.Name = request.Name?.Trim() ?? kb.Name;
        kb.Description = request.Description?.Trim() ?? kb.Description;
        kb.ChunkSize = size;
        kb.ChunkOverlap = overlap;
        kb.Hybrid = request.Hybrid ?? kb.Hybrid;
        kb.Rerank = request.Rerank ?? kb.Rerank;
        kb.ThresholdPercent = request.ThresholdPercent ?? kb.ThresholdPercent;
        kb.TopK = request.TopK ?? kb.TopK;
        kb.Visibility = request.Visibility ?? kb.Visibility;
        await db.SaveChangesAsync(cancellationToken);
        if (rechunk)
        {
            // New chunk settings: every finished document is cut again.
            await db.Documents.Where(d => d.BaseId == id && (d.State == DocumentStates.Ready || d.State == DocumentStates.Failed))
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.State, DocumentStates.Reindexing).SetProperty(d => d.Progress, 0).SetProperty(d => d.Error, (string?)null), cancellationToken);
        }
        await audit.RecordAsync(new AuditRecord(KnowledgeAudit.BaseUpdated, "knowledge_base", kb.Id.ToString(), kb.Name), cancellationToken);
        return await DtoAsync(kb, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var kb = await FindBaseAsync(id, tracking: true, cancellationToken);
        var chunkIds = await db.Chunks.Where(c => c.BaseId == id).Select(c => c.Id).ToListAsync(cancellationToken);
        db.Bases.Remove(kb);
        await db.SaveChangesAsync(cancellationToken);
        vectors.Remove(chunkIds);
        await audit.RecordAsync(new AuditRecord(KnowledgeAudit.BaseDeleted, "knowledge_base", id.ToString(), kb.Name), cancellationToken);
    }

    public async Task<Paged<DocumentDto>> DocumentsAsync(Guid baseId, string? state, string? q, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        await FindBaseAsync(baseId, tracking: false, cancellationToken);
        var paging = PageRequest.Of(page, pageSize);
        var query = db.Documents.AsNoTracking().Where(d => d.BaseId == baseId);
        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(d => d.State == state);
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(d => d.Name.ToLower().Contains(needle));
        }
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Name).ThenBy(d => d.Id).Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellationToken);
        var names = await NamesAsync(cancellationToken);
        return paging.Wrap<DocumentDto>([.. rows.Select(d => ToDto(d, names))], total);
    }

    public async Task<DocumentDto> AddDocumentAsync(Guid baseId, CreateDocumentRequest request, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        await FindBaseAsync(baseId, tracking: false, cancellationToken);
        var validator = new Validator().Text("name", request.Name, 260).Text("mime", request.Mime, 120);
        if (request.Size is < 0 or > 50_000_000)
        {
            validator.Add("size", "A file may be at most 50 MB.");
        }
        if (request.Text is { Length: > KnowledgeDbContext.MaxContent })
        {
            validator.Add("text", $"At most {KnowledgeDbContext.MaxContent} characters of text.");
        }
        validator.ThrowIfInvalid();
        var hash = Secrets.Hash(string.IsNullOrWhiteSpace(request.Text) ? $"{request.Name}|{request.Size}" : request.Text);
        if (await db.Documents.AnyAsync(d => d.BaseId == baseId && d.Hash == hash, cancellationToken))
        {
            throw ApiException.Conflict(ErrorCodes.DuplicateDocument, "This document is already in the knowledge base.");
        }
        var document = new KbDocument
        {
            Id = Guid.NewGuid(), BaseId = baseId, Name = request.Name.Trim(), Mime = request.Mime.Trim(), Size = request.Size, Hash = hash, State = DocumentStates.Queued, Progress = 0,
            UploaderId = actor.Id ?? Guid.Empty, Content = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text, CreatedAt = time.GetUtcNow(),
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(KnowledgeAudit.DocumentAdded, "document", document.Id.ToString(), document.Name), cancellationToken);
        return ToDto(document, await NamesAsync(cancellationToken));
    }

    public async Task DeleteDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw ApiException.NotFound("The document");
        await FindBaseAsync(document.BaseId, tracking: false, cancellationToken);
        if (document.UploaderId != actor.Id && !actor.Can(Permissions.KnowledgeManage))
        {
            throw ApiException.Forbidden("Only the person who added a document, or someone who manages knowledge, can delete it.");
        }
        var chunkIds = await db.Chunks.Where(c => c.DocumentId == id).Select(c => c.Id).ToListAsync(cancellationToken);
        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        vectors.Remove(chunkIds);
        await audit.RecordAsync(new AuditRecord(KnowledgeAudit.DocumentDeleted, "document", id.ToString(), document.Name), cancellationToken);
    }

    public async Task<DocumentDto> ReindexAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw ApiException.NotFound("The document");
        await FindBaseAsync(document.BaseId, tracking: false, cancellationToken);
        if (DocumentStates.InProgress(document.State))
        {
            throw ApiException.Conflict(ErrorCodes.InvalidState, "The document is being indexed already.");
        }
        document.State = DocumentStates.Reindexing;
        document.Progress = 0;
        document.Error = null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditRecord(KnowledgeAudit.DocumentReindexed, "document", id.ToString(), document.Name), cancellationToken);
        return ToDto(document, await NamesAsync(cancellationToken));
    }

    public async Task<SearchKnowledgeResponse> SearchAsync(Guid baseId, SearchKnowledgeRequest request, CancellationToken cancellationToken)
    {
        var kb = await FindBaseAsync(baseId, tracking: false, cancellationToken);
        var validator = new Validator().Text("query", request.Query, 500);
        if (request.TopK is < 1 or > 20)
        {
            validator.Add("topK", "Use 1 to 20.");
        }
        validator.ThrowIfInvalid();
        var clock = Stopwatch.StartNew();
        var hits = await new KnowledgeSearch(db, vectors).RunAsync(request.Query, [kb], request.TopK ?? kb.TopK, cancellationToken);
        return new SearchKnowledgeResponse([.. hits.Select(h => new SearchHitDto(h.DocumentId, h.ChunkId, h.DocumentName, Math.Round(h.Score, 3), h.Text, h.Page, h.Heading))], (int)clock.ElapsedMilliseconds);
    }

    private async Task<KbBase> FindBaseAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var actor = await actors.RequireAsync(cancellationToken);
        var query = tracking ? db.Bases.AsQueryable() : db.Bases.AsNoTracking();
        // A private base of someone else answers 404, as if it did not exist.
        return await query.For(actor).SingleOrDefaultAsync(b => b.Id == id, cancellationToken) ?? throw ApiException.NotFound("The knowledge base");
    }

    private async Task<KnowledgeBaseDto> DtoAsync(KbBase kb, CancellationToken cancellationToken)
    {
        var documents = await db.Documents.AsNoTracking().Where(d => d.BaseId == kb.Id).GroupBy(d => 1)
            .Select(g => new { Documents = g.Count(), Chunks = g.Sum(d => d.ChunkCount), Busy = g.Count(d => d.State != DocumentStates.Ready && d.State != DocumentStates.Failed) }).SingleOrDefaultAsync(cancellationToken);
        return ToDto(kb, documents?.Documents ?? 0, documents?.Chunks ?? 0, documents?.Busy ?? 0);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(CancellationToken cancellationToken) =>
        (await users.ListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.Name);

    internal static KnowledgeBaseDto ToDto(KbBase b, int documents, int chunks, int busy) =>
        new(b.Id, b.Name, b.Description, b.EmbeddingModelCode, b.ChunkSize, b.ChunkOverlap, b.Hybrid, b.Rerank, b.ThresholdPercent, b.TopK, b.Visibility, documents, chunks, busy, b.Version);

    internal static DocumentDto ToDto(KbDocument d, IReadOnlyDictionary<Guid, string> names) =>
        new(d.Id, d.BaseId, d.Name, d.Mime, d.Size, d.State, d.Progress, d.ChunkCount, d.Error, d.Pinned, names.GetValueOrDefault(d.UploaderId, ""), d.CreatedAt);

    private static void ValidateChunking(Validator validator, int size, int overlap)
    {
        if (size is < 100 or > 2_000)
        {
            validator.Add("chunkSize", "Use 100 to 2000 characters.");
        }
        if (overlap < 0 || overlap > size / 2)
        {
            validator.Add("chunkOverlap", "Use 0 up to half the chunk size.");
        }
    }

    private static void ValidateVisibility(Validator validator, string? visibility)
    {
        if (visibility is not null && !Visibilities.Contains(visibility))
        {
            validator.Add("visibility", "Use private, space or workspace.");
        }
    }
}

/// <summary>The retrieval: scores the chunks of the given bases against the query and keeps the best above each base's threshold.</summary>
internal sealed class KnowledgeSearch(KnowledgeDbContext db, IVectorIndex vectors)
{
    public sealed record Hit(Guid DocumentId, Guid ChunkId, string DocumentName, string BaseName, double Score, string Text, int? Page, string? Heading);

    public async Task<IReadOnlyList<Hit>> RunAsync(string query, IReadOnlyList<KbBase> bases, int topK, CancellationToken cancellationToken)
    {
        var queryVector = KnowledgeText.Embed(query);
        var queryTokens = KnowledgeText.Tokens(query).ToHashSet(StringComparer.Ordinal);
        var hits = new List<Hit>();
        foreach (var kb in bases)
        {
            var rows = await (from c in db.Chunks.AsNoTracking()
                              join d in db.Documents.AsNoTracking() on c.DocumentId equals d.Id
                              where c.BaseId == kb.Id && d.State != DocumentStates.Failed
                              select new { c.Id, c.DocumentId, c.Text, c.Page, c.Heading, DocumentName = d.Name }).ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                var score = KnowledgeText.Score(query, queryVector, queryTokens, row.Text, vectors.Vector(row.Id, row.Text), kb.Hybrid, kb.Rerank);
                if (score * 100 >= kb.ThresholdPercent && score > 0)
                {
                    hits.Add(new Hit(row.DocumentId, row.Id, row.DocumentName, kb.Name, score, row.Text, row.Page, row.Heading));
                }
            }
        }
        return [.. hits.OrderByDescending(h => h.Score).ThenBy(h => h.ChunkId).Take(topK)];
    }
}
