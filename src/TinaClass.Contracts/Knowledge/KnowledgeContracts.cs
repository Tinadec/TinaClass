namespace TinaClass.Contracts.Knowledge;

/// <summary>The document state machine (design 4.2): queued, uploading, parsing, chunking, embedding, ready; failed from any step; reindexing after a settings change.</summary>
public static class DocumentStates
{
    public const string Queued = "queued";
    public const string Uploading = "uploading";
    public const string Parsing = "parsing";
    public const string Chunking = "chunking";
    public const string Embedding = "embedding";
    public const string Ready = "ready";
    public const string Reindexing = "reindexing";
    public const string Failed = "failed";

    public static bool InProgress(string s) => s is Queued or Uploading or Parsing or Chunking or Embedding or Reindexing;
}

public sealed record KnowledgeBaseDto(Guid Id, string Name, string Description, string EmbeddingModelCode, int ChunkSize, int ChunkOverlap, bool Hybrid, bool Rerank,
    int ThresholdPercent, int TopK, string Visibility, int DocumentCount, int ChunkCount, int InProgressCount, int Version);

public sealed record CreateKnowledgeBaseRequest(string Name, string Description, string? EmbeddingModelCode, int? ChunkSize, int? ChunkOverlap, string? Visibility);

public sealed record UpdateKnowledgeBaseRequest(string? Name, string? Description, int? ChunkSize, int? ChunkOverlap, bool? Hybrid, bool? Rerank, int? ThresholdPercent, int? TopK,
    string? Visibility, int Version);

public sealed record DocumentDto(Guid Id, Guid BaseId, string Name, string Mime, long Size, string State, int Progress, int ChunkCount, string? Error, bool Pinned,
    string UploaderName, DateTimeOffset CreatedAt);

/// <summary>POST /knowledge/{id}/documents. <see cref="Text"/> is the extracted text; an image without text is accepted and then fails parsing.</summary>
public sealed record CreateDocumentRequest(string Name, string Mime, long Size, string? Text);

public sealed record SearchKnowledgeRequest(string Query, int? TopK);

public sealed record SearchHitDto(Guid DocumentId, Guid ChunkId, string DocumentName, double Score, string Text, int? Page, string? Heading);

public sealed record SearchKnowledgeResponse(IReadOnlyList<SearchHitDto> Hits, int ElapsedMs);

public static class KnowledgeEvents
{
    public const string DocumentIndexed = "document.indexed";
    public const string DocumentFailed = "document.failed";
}
