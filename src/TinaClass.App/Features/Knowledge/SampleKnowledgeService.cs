using TinaClass.App.Common.Sample;
using TinaClass.Contracts.Knowledge;
using TinaClass.Contracts.Sample;

namespace TinaClass.App.Features.Knowledge;

/// <summary>
/// The sample bases and documents of 栖云科技 (Contracts/Knowledge/KnowledgeSample), in memory. The first base has the nine named documents (five ready,
/// two in progress, one queued, one failed); the others show a few generated rows. With <see cref="AutoAdvance"/> the in-progress documents move on
/// by 9 points a second (queued, parsing, chunking, embedding, ready), so the page shows the pipeline; tests and screenshots turn it off.
/// </summary>
public sealed class SampleKnowledgeService : IKnowledgeService
{
    public static bool AutoAdvance { get; set; } = true;

    private readonly List<KnowledgeBaseDto> _bases = [.. KnowledgeSample.Bases.Select(b => new KnowledgeBaseDto(b.Id, b.Name, b.Description, "nebula-embed-v2", 800, 100, true, false, 55, 4, b.Visibility, b.Documents, b.Chunks,
        b.Id == SampleIds.Base(1) ? 3 : 0, 1))];

    private readonly List<DocumentDto> _documents = [.. KnowledgeSample.Documents.Select(d => new DocumentDto(d.Id, d.BaseId, d.Name, d.Mime, d.Size, d.State, d.Progress, d.Chunks, d.Error, false,
        SampleCore.NameOf(d.UploaderId), SampleClock.Now.AddMinutes(-d.MinutesAgo)))];

    private DateTimeOffset _lastTick = DateTimeOffset.UtcNow;

    public Task<IReadOnlyList<KnowledgeBaseDto>> BasesAsync(CancellationToken cancellation = default) => Task.FromResult<IReadOnlyList<KnowledgeBaseDto>>([.. _bases.Select(Counted)]);

    public Task<KnowledgeBaseDto> CreateBaseAsync(string name, string description, CancellationToken cancellation = default)
    {
        var created = new KnowledgeBaseDto(Guid.NewGuid(), name, description, "nebula-embed-v2", 800, 100, true, false, 55, 4, "workspace", 0, 0, 0, 1);
        _bases.Insert(0, created);
        return Task.FromResult(created);
    }

    public Task<IReadOnlyList<DocumentDto>> DocumentsAsync(Guid baseId, string? query = null, CancellationToken cancellation = default)
    {
        Advance();
        var own = _documents.Where(d => d.BaseId == baseId).ToList();
        // The bases without named documents show a few generated rows, so every base has something to look at.
        if (own.Count == 0 && KnowledgeSample.Bases.FirstOrDefault(b => b.Id == baseId) is { } sample)
        {
            own = [.. Generate(sample)];
            _documents.AddRange(own);
        }
        var found = own.Where(d => string.IsNullOrWhiteSpace(query) || d.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).OrderByDescending(d => d.CreatedAt).ToList();
        return Task.FromResult<IReadOnlyList<DocumentDto>>(found);
    }

    public Task<DocumentDto> AddDocumentAsync(Guid baseId, string name, string mime, long size, string? text, CancellationToken cancellation = default)
    {
        var document = new DocumentDto(Guid.NewGuid(), baseId, name, mime, size, DocumentStates.Queued, 0, 0, null, false, SampleCore.People[0].Name, SampleClock.Now);
        _documents.Add(document);
        return Task.FromResult(document);
    }

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellation = default)
    {
        _documents.RemoveAll(d => d.Id == documentId);
        return Task.CompletedTask;
    }

    public Task<DocumentDto> ReindexAsync(Guid documentId, CancellationToken cancellation = default)
    {
        var index = _documents.FindIndex(d => d.Id == documentId);
        _documents[index] = _documents[index] with { State = DocumentStates.Queued, Progress = 0, Error = null };
        return Task.FromResult(_documents[index]);
    }

    public Task<SearchKnowledgeResponse> SearchAsync(Guid baseId, string query, CancellationToken cancellation = default)
    {
        var terms = Bigrams(query);
        var hits = KnowledgeSample.Chunks.Select(c => (Chunk: c, Score: terms.Count == 0 ? 0 : terms.Count(t => c.Text.Contains(t, StringComparison.OrdinalIgnoreCase)) / (double)terms.Count))
            .Where(x => x.Score > 0 && KnowledgeSample.Documents.Any(d => d.Id == x.Chunk.DocumentId && d.BaseId == baseId))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Chunk.Ordinal).Take(4)
            .Select(x => new SearchHitDto(x.Chunk.DocumentId, x.Chunk.Id, KnowledgeSample.Documents.First(d => d.Id == x.Chunk.DocumentId).Name, Math.Round(0.55 + x.Score * 0.4, 2), x.Chunk.Text, x.Chunk.Page, x.Chunk.Heading))
            .ToList();
        return Task.FromResult(new SearchKnowledgeResponse(hits, 38 + query.Length * 3));
    }

    private static List<string> Bigrams(string query)
    {
        var text = new string([.. query.Where(char.IsLetterOrDigit)]);
        return [.. Enumerable.Range(0, Math.Max(0, text.Length - 1)).Select(i => text.Substring(i, 2)).Distinct()];
    }

    private KnowledgeBaseDto Counted(KnowledgeBaseDto b) => b with { InProgressCount = _documents.Count(d => d.BaseId == b.Id && DocumentStates.InProgress(d.State)) };

    private static IEnumerable<DocumentDto> Generate(SampleBase b)
    {
        string[] stems = ["访谈纪要", "问卷汇总", "标准回复", "合同模板", "周报", "变更说明", "隐私条款", "FAQ"];
        for (var i = 0; i < 8; i++)
        {
            yield return new DocumentDto(SampleIds.Document(100 + (int)(b.Id.ToString()[^1] % 7) * 10 + i), b.Id, $"{stems[(i + b.Name.Length) % stems.Length]} {i + 1:00}.docx", "application/msword", 90_000 + i * 41_000, DocumentStates.Ready, 100,
                b.Chunks / b.Documents + i, null, false, SampleCore.People[i % SampleCore.People.Count].Name, SampleClock.Now.AddHours(-(i + 1) * 20));
        }
    }

    /// <summary>Moves the documents in progress on by the time passed since the last call: 9 points a second through the stages.</summary>
    private void Advance()
    {
        var now = DateTimeOffset.UtcNow;
        var seconds = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        if (!AutoAdvance || seconds <= 0)
        {
            return;
        }
        for (var i = 0; i < _documents.Count; i++)
        {
            var d = _documents[i];
            if (!DocumentStates.InProgress(d.State))
            {
                continue;
            }
            var progress = Math.Min(100, d.Progress + (int)Math.Ceiling(seconds * 9));
            _documents[i] = progress >= 100
                ? d with { State = DocumentStates.Ready, Progress = 100, ChunkCount = Math.Max(1, (int)(d.Size / 12_000)) }
                : d with { State = progress < 25 ? DocumentStates.Parsing : progress < 50 ? DocumentStates.Chunking : DocumentStates.Embedding, Progress = progress };
        }
    }
}
