using TinaClass.Contracts.Knowledge;

namespace TinaClass.App.Features.Knowledge;

/// <summary>
/// Where the app gets its knowledge bases and documents. The sample service keeps them in memory and moves in-progress documents along
/// (queued, parsing, embedding, ready); <c>Api/ApiKnowledgeService.cs</c> talks to <c>/knowledge</c> and <c>/documents</c>, where the
/// server indexes. Either way the page polls while a document is in progress.
/// </summary>
public interface IKnowledgeService
{
    Task<IReadOnlyList<KnowledgeBaseDto>> BasesAsync(CancellationToken cancellation = default);

    Task<KnowledgeBaseDto> CreateBaseAsync(string name, string description, CancellationToken cancellation = default);

    /// <summary>The documents of a base (newest first), optionally those whose name contains <paramref name="query"/>.</summary>
    Task<IReadOnlyList<DocumentDto>> DocumentsAsync(Guid baseId, string? query = null, CancellationToken cancellation = default);

    /// <summary>Adds a document: its name, type, size and, when it was readable as text, the text.</summary>
    Task<DocumentDto> AddDocumentAsync(Guid baseId, string name, string mime, long size, string? text, CancellationToken cancellation = default);

    Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellation = default);

    /// <summary>Indexes a document again (a failed one, or after the base's settings changed).</summary>
    Task<DocumentDto> ReindexAsync(Guid documentId, CancellationToken cancellation = default);

    Task<SearchKnowledgeResponse> SearchAsync(Guid baseId, string query, CancellationToken cancellation = default);
}
