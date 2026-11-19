using Microsoft.EntityFrameworkCore;
using TinaClass.Api.Common.Events;
using TinaClass.Api.Common.Security;
using TinaClass.Api.Common.Seams;
using TinaClass.Api.Features.Knowledge.Data;
using TinaClass.Contracts;
using TinaClass.Contracts.Knowledge;

namespace TinaClass.Api.Features.Knowledge;

/// <summary>
/// Moves documents along the state machine one step at a time: queued, uploading, parsing, chunking, embedding, ready; failed from any step
/// (with the reason); a base's new chunk settings or a retry go through reindexing. Each step is claimed with a conditional update, so two
/// API instances never advance the same document twice.
/// </summary>
public sealed class IndexingService(KnowledgeDbContext db, IVectorIndex vectors, IUserDirectory users, IEventPublisher events)
{
    /// <summary>Advances every document in progress by one step; returns how many moved.</summary>
    public async Task<int> AdvanceAsync(CancellationToken cancellationToken)
    {
        var ids = await db.Documents.AsNoTracking()
            .Where(d => d.State == DocumentStates.Queued || d.State == DocumentStates.Uploading || d.State == DocumentStates.Parsing || d.State == DocumentStates.Chunking
                     || d.State == DocumentStates.Embedding || d.State == DocumentStates.Reindexing)
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).Select(d => d.Id).Take(20).ToListAsync(cancellationToken);
        var moved = 0;
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            if (await StepAsync(id, cancellationToken))
            {
                moved++;
            }
        }
        return moved;
    }

    /// <summary>Advances until nothing is in progress (what the tests use).</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20 && await AdvanceAsync(cancellationToken) > 0; i++)
        {
        }
    }

    private async Task<bool> StepAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null || !DocumentStates.InProgress(document.State))
        {
            return false;
        }
        var (next, progress) = (document.State, document.Progress) switch
        {
            (DocumentStates.Queued, _) => (DocumentStates.Uploading, 10),
            (DocumentStates.Uploading, _) => (DocumentStates.Parsing, 28),
            (DocumentStates.Parsing, _) => (DocumentStates.Chunking, 45),
            (DocumentStates.Chunking, _) => (DocumentStates.Embedding, 62),
            (DocumentStates.Embedding, _) => (DocumentStates.Ready, 100),
            (DocumentStates.Reindexing, < 50) => (DocumentStates.Reindexing, 50),
            _ => (DocumentStates.Ready, 100),
        };
        try
        {
            var fromState = document.State;
            var fromProgress = document.Progress;
            var claimed = await db.Documents.Where(d => d.Id == id && d.State == fromState && d.Progress == fromProgress)
                                  .ExecuteUpdateAsync(s => s.SetProperty(d => d.State, next).SetProperty(d => d.Progress, progress), cancellationToken);
            if (claimed == 0)
            {
                return false;
            }
            if (fromState == DocumentStates.Parsing)
            {
                Parse(document);
            }
            else if (fromState == DocumentStates.Chunking || fromState == DocumentStates.Reindexing && fromProgress < 50)
            {
                await ChunkAsync(document, cancellationToken);
            }
            else if (next == DocumentStates.Ready)
            {
                await EmbedAsync(document, cancellationToken);
            }
        }
        catch (DocumentException exception)
        {
            await FailAsync(id, exception.Message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await FailAsync(id, "The document could not be indexed.", cancellationToken);
        }
        return true;
    }

    /// <summary>A file with no text (a picture, an empty file) cannot be indexed.</summary>
    private static void Parse(KbDocument document)
    {
        if (document.Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(document.Content))
        {
            throw new DocumentException("No text could be recognized in the picture.");
        }
        if (string.IsNullOrWhiteSpace(document.Content) && document.ChunkCount == 0)
        {
            throw new DocumentException("No text could be extracted from the file.");
        }
    }

    private async Task ChunkAsync(KbDocument document, CancellationToken cancellationToken)
    {
        var kb = await db.Bases.AsNoTracking().SingleAsync(b => b.Id == document.BaseId, cancellationToken);
        var existing = await db.Chunks.Where(c => c.DocumentId == document.Id).OrderBy(c => c.Ordinal).ThenBy(c => c.Id).ToListAsync(cancellationToken);
        var source = string.IsNullOrWhiteSpace(document.Content) ? string.Join("\n\n", existing.Select(c => c.Text)) : document.Content;
        if (string.IsNullOrWhiteSpace(source))
        {
            if (document.ChunkCount == 0)
            {
                throw new DocumentException("No text could be extracted from the file.");
            }
            // A sample document that only carries its chunk count: nothing to cut again.
            return;
        }
        var pieces = KnowledgeText.Chunk(source, kb.ChunkSize, kb.ChunkOverlap);
        vectors.Remove(existing.Select(c => c.Id));
        db.Chunks.RemoveRange(existing);
        var ordinal = 0;
        foreach (var piece in pieces)
        {
            db.Chunks.Add(new KbChunk { Id = Guid.NewGuid(), DocumentId = document.Id, BaseId = document.BaseId, Ordinal = ordinal++, Text = piece.Text, Heading = piece.Heading, TokenCount = KnowledgeText.Tokens(piece.Text).Count });
        }
        await db.SaveChangesAsync(cancellationToken);
        await db.Documents.Where(d => d.Id == document.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.ChunkCount, pieces.Count), cancellationToken);
    }

    private async Task EmbedAsync(KbDocument document, CancellationToken cancellationToken)
    {
        foreach (var chunk in await db.Chunks.AsNoTracking().Where(c => c.DocumentId == document.Id).ToListAsync(cancellationToken))
        {
            vectors.Vector(chunk.Id, chunk.Text);
        }
        var done = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == document.Id, cancellationToken);
        await events.PublishAsync(KnowledgeEvents.DocumentIndexed, KnowledgeService.ToDto(done, await NamesAsync(cancellationToken)), ContractsJsonContext.Default.DocumentDto, cancellationToken);
    }

    private async Task FailAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await db.Documents.Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.State, DocumentStates.Failed).SetProperty(d => d.Progress, 0).SetProperty(d => d.Error, reason), cancellationToken);
        var failed = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == id, cancellationToken);
        await events.PublishAsync(KnowledgeEvents.DocumentFailed, KnowledgeService.ToDto(failed, await NamesAsync(cancellationToken)), ContractsJsonContext.Default.DocumentDto, cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(CancellationToken cancellationToken) =>
        (await users.ListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.Name);

    private sealed class DocumentException(string message) : Exception(message);
}

/// <summary>Runs <see cref="IndexingService"/> every <c>Knowledge:IndexerPollMs</c> milliseconds (0 turns it off; tests call the service directly).</summary>
internal sealed partial class IndexerJob(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider time, ILogger<IndexerJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var milliseconds = configuration.GetValue("Knowledge:IndexerPollMs", 600);
        if (milliseconds <= 0)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(milliseconds), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IndexingService>().AdvanceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The document indexer failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>What chat and agents use: retrieval over the bases the actor may see, and the context of a cited chunk.</summary>
internal sealed class KnowledgeRetriever(KnowledgeDbContext db, IActorContext actors, IVectorIndex vectors) : IKnowledgeRetriever
{
    public async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(string query, IReadOnlyList<Guid> baseIds, int topK, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }
        var actor = await actors.GetAsync(cancellationToken);
        var bases = db.Bases.AsNoTracking().For(actor);
        if (baseIds.Count > 0)
        {
            bases = bases.Where(b => baseIds.Contains(b.Id));
        }
        else if (actor is null)
        {
            // The background has no person: without an explicit choice only what the workspace shares.
            bases = bases.Where(b => b.Visibility != "private");
        }
        var hits = await new KnowledgeSearch(db, vectors).RunAsync(query, await bases.OrderBy(b => b.Name).ThenBy(b => b.Id).ToListAsync(cancellationToken), Math.Clamp(topK, 1, 20), cancellationToken);
        return [.. hits.Select(h => new RetrievedChunk(h.DocumentId, h.ChunkId, h.DocumentName, Where(h.Heading, h.Page), h.Score, h.Text, h.BaseName))];
    }

    public async Task<ChunkContext?> GetChunkAsync(Guid chunkId, CancellationToken cancellationToken)
    {
        var actor = await actors.GetAsync(cancellationToken);
        var chunk = await db.Chunks.AsNoTracking().SingleOrDefaultAsync(c => c.Id == chunkId, cancellationToken);
        if (chunk is null || !await db.Bases.AsNoTracking().For(actor).AnyAsync(b => b.Id == chunk.BaseId, cancellationToken))
        {
            return null;
        }
        var document = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == chunk.DocumentId, cancellationToken);
        var neighbours = await db.Chunks.AsNoTracking().Where(c => c.DocumentId == chunk.DocumentId && (c.Ordinal == chunk.Ordinal - 1 || c.Ordinal == chunk.Ordinal + 1)).ToListAsync(cancellationToken);
        return new ChunkContext(document.Name, Where(chunk.Heading, chunk.Page), chunk.Text, Cut(neighbours.FirstOrDefault(c => c.Ordinal < chunk.Ordinal)?.Text), Cut(neighbours.FirstOrDefault(c => c.Ordinal > chunk.Ordinal)?.Text));
    }

    private static string? Where(string? heading, int? page) => (heading, page) switch
    {
        (null or "", null) => null,
        (null or "", { } p) => $"p.{p}",
        ({ } h, null) => h,
        ({ } h, { } p) => $"{h} · p.{p}",
    };

    private static string Cut(string? text) => text is null ? "" : text.Length <= 300 ? text : text[..300] + "…";
}
