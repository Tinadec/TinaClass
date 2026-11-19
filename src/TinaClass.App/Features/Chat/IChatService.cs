using TinaClass.Contracts.Chat;

namespace TinaClass.App.Features.Chat;

/// <summary>
/// Where chat comes from: conversations, folders, messages, and the streamed answer. <see cref="SampleChatService"/> keeps them
/// in memory and answers with a scripted stream; <c>Api/ApiChatService.cs</c> talks to the API (REST for the lists, server-sent
/// events for the answer). The events are the contract's <see cref="ChatStreamEvent"/> in both cases, so the view model does not
/// know which one it has.
/// </summary>
public interface IChatService
{
    Task<IReadOnlyList<FolderDto>> FoldersAsync(CancellationToken cancellation = default);

    Task<IReadOnlyList<SpaceDto>> SpacesAsync(CancellationToken cancellation = default);

    Task<IReadOnlyList<ConversationDto>> ConversationsAsync(CancellationToken cancellation = default);

    Task<ConversationDto> CreateAsync(string? title, Guid? folderId, Guid? spaceId, string? modelCode, CancellationToken cancellation = default);

    /// <summary>The shown branch of a conversation, root first (an older answer of a regenerated turn comes before the shown one).</summary>
    Task<IReadOnlyList<MessageDto>> MessagesAsync(Guid conversationId, CancellationToken cancellation = default);

    IAsyncEnumerable<ChatStreamEvent> SendAsync(Guid conversationId, SendMessageRequest request, CancellationToken cancellation = default);

    IAsyncEnumerable<ChatStreamEvent> RegenerateAsync(Guid messageId, string? modelCode, CancellationToken cancellation = default);

    /// <summary>Stops an answer that is being generated (what was generated is kept).</summary>
    Task StopAsync(Guid messageId, CancellationToken cancellation = default);

    /// <summary><paramref name="value"/> is <c>up</c>, <c>down</c> or empty (cleared).</summary>
    Task FeedbackAsync(Guid messageId, string value, CancellationToken cancellation = default);

    /// <summary>Renames, pins, archives or moves a conversation; a null argument leaves that part alone.</summary>
    Task<ConversationDto> UpdateAsync(ConversationDto conversation, string? title = null, Guid? folderId = null, bool moveToRoot = false, bool? pinned = null,
        bool? archived = null, CancellationToken cancellation = default);

    Task DeleteAsync(Guid conversationId, CancellationToken cancellation = default);

    Task<FolderDto> CreateFolderAsync(string name, CancellationToken cancellation = default);

    Task<IReadOnlyList<SearchResultDto>> SearchAsync(string query, CancellationToken cancellation = default);
}
