using System.Runtime.CompilerServices;
using TinaClass.App.Common.Api;
using TinaClass.Contracts;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Common;

namespace TinaClass.App.Features.Chat.Api;

/// <summary>Chat over the API: <c>/conversations</c>, <c>/folders</c>, <c>/messages</c>; the answer is read as server-sent events.</summary>
public sealed class ApiChatService(ApiClient api) : IChatService
{
    private static ContractsJsonContext Json => ContractsJsonContext.Default;

    public Task<IReadOnlyList<FolderDto>> FoldersAsync(CancellationToken cancellation = default) => api.GetAsync(ApiRoutes.Folders, Json.IReadOnlyListFolderDto, cancellation);

    public Task<IReadOnlyList<SpaceDto>> SpacesAsync(CancellationToken cancellation = default) => api.GetAsync(ApiRoutes.Spaces, Json.IReadOnlyListSpaceDto, cancellation);

    public async Task<IReadOnlyList<ConversationDto>> ConversationsAsync(CancellationToken cancellation = default) =>
        (await api.GetAsync(ApiRoutes.Conversations + "?pageSize=100", Json.PagedConversationDto, cancellation)).Items;

    public Task<ConversationDto> CreateAsync(string? title, Guid? folderId, Guid? spaceId, string? modelCode, CancellationToken cancellation = default) =>
        api.PostAsync(ApiRoutes.Conversations, new CreateConversationRequest(title, folderId, spaceId, modelCode), Json.CreateConversationRequest, Json.ConversationDto, cancellation);

    public Task<IReadOnlyList<MessageDto>> MessagesAsync(Guid conversationId, CancellationToken cancellation = default) =>
        api.GetAsync($"{ApiRoutes.Conversations}/{conversationId}/messages", Json.IReadOnlyListMessageDto, cancellation);

    public async IAsyncEnumerable<ChatStreamEvent> SendAsync(Guid conversationId, SendMessageRequest request, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        await foreach (var (_, data) in api.StreamAsync($"{ApiRoutes.Conversations}/{conversationId}/messages", request, Json.SendMessageRequest, cancellation))
        {
            if (System.Text.Json.JsonSerializer.Deserialize(data, Json.ChatStreamEvent) is { } e)
            {
                yield return e;
            }
        }
    }

    public async IAsyncEnumerable<ChatStreamEvent> RegenerateAsync(Guid messageId, string? modelCode, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        await foreach (var (_, data) in api.StreamAsync($"{ApiRoutes.Messages}/{messageId}/regenerate", new RegenerateRequest(modelCode), Json.RegenerateRequest, cancellation))
        {
            if (System.Text.Json.JsonSerializer.Deserialize(data, Json.ChatStreamEvent) is { } e)
            {
                yield return e;
            }
        }
    }

    public Task StopAsync(Guid messageId, CancellationToken cancellation = default) => api.PostEmptyAsync($"{ApiRoutes.Messages}/{messageId}/stop", cancellation);

    public Task FeedbackAsync(Guid messageId, string value, CancellationToken cancellation = default) =>
        api.PostAsync($"{ApiRoutes.Messages}/{messageId}/feedback", new FeedbackRequest(value), Json.FeedbackRequest, cancellation);

    public Task<ConversationDto> UpdateAsync(ConversationDto conversation, string? title = null, Guid? folderId = null, bool moveToRoot = false, bool? pinned = null,
        bool? archived = null, CancellationToken cancellation = default) =>
        api.PatchAsync($"{ApiRoutes.Conversations}/{conversation.Id}", new UpdateConversationRequest(title, folderId, moveToRoot, pinned, archived, null, null, conversation.Version),
            Json.UpdateConversationRequest, Json.ConversationDto, cancellation);

    public Task DeleteAsync(Guid conversationId, CancellationToken cancellation = default) => api.DeleteAsync($"{ApiRoutes.Conversations}/{conversationId}", cancellation);

    public Task<FolderDto> CreateFolderAsync(string name, CancellationToken cancellation = default) =>
        api.PostAsync(ApiRoutes.Folders, new CreateFolderRequest(name, null), Json.CreateFolderRequest, Json.FolderDto, cancellation);

    public Task<IReadOnlyList<SearchResultDto>> SearchAsync(string query, CancellationToken cancellation = default) =>
        api.GetAsync($"{ApiRoutes.Search}?q={Uri.EscapeDataString(query)}", Json.IReadOnlyListSearchResultDto, cancellation);
}
