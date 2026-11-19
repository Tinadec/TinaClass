using TinaClass.App.Common.Api;
using TinaClass.Contracts;
using TinaClass.Contracts.Common;
using TinaClass.Contracts.Knowledge;

namespace TinaClass.App.Features.Knowledge.Api;

/// <summary>The knowledge bases and documents of the API; the server does the indexing and the page polls the documents.</summary>
public sealed class ApiKnowledgeService(ApiClient api) : IKnowledgeService
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public Task<IReadOnlyList<KnowledgeBaseDto>> BasesAsync(CancellationToken cancellation = default) =>
        api.GetAsync(ApiRoutes.Knowledge, Json.IReadOnlyListKnowledgeBaseDto, cancellation);

    public Task<KnowledgeBaseDto> CreateBaseAsync(string name, string description, CancellationToken cancellation = default) =>
        api.PostAsync(ApiRoutes.Knowledge, new CreateKnowledgeBaseRequest(name, description, null, null, null, null), Json.CreateKnowledgeBaseRequest, Json.KnowledgeBaseDto, cancellation);

    public async Task<IReadOnlyList<DocumentDto>> DocumentsAsync(Guid baseId, string? query = null, CancellationToken cancellation = default)
    {
        var q = string.IsNullOrWhiteSpace(query) ? string.Empty : "&q=" + Uri.EscapeDataString(query.Trim());
        return (await api.GetAsync($"{ApiRoutes.Knowledge}/{baseId}/documents?pageSize=100{q}", Json.PagedDocumentDto, cancellation)).Items;
    }

    public Task<DocumentDto> AddDocumentAsync(Guid baseId, string name, string mime, long size, string? text, CancellationToken cancellation = default) =>
        api.PostAsync($"{ApiRoutes.Knowledge}/{baseId}/documents", new CreateDocumentRequest(name, mime, size, text), Json.CreateDocumentRequest, Json.DocumentDto, cancellation);

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellation = default) => api.DeleteAsync($"{ApiRoutes.Documents}/{documentId}", cancellation);

    public Task<DocumentDto> ReindexAsync(Guid documentId, CancellationToken cancellation = default) =>
        api.PostAsync($"{ApiRoutes.Documents}/{documentId}/reindex", Json.DocumentDto, cancellation);

    public Task<SearchKnowledgeResponse> SearchAsync(Guid baseId, string query, CancellationToken cancellation = default) =>
        api.PostAsync($"{ApiRoutes.Knowledge}/{baseId}/search", new SearchKnowledgeRequest(query, null), Json.SearchKnowledgeRequest, Json.SearchKnowledgeResponse, cancellation);
}
