using CommunityToolkit.Mvvm.ComponentModel;
using TinaClass.Contracts.Chat;

namespace TinaClass.App.Features.Chat;

/// <summary>
/// The conversation list the whole window shares: the sidebar shows it, the chat page opens from it, the command palette searches
/// it. One instance for the app; <see cref="Changed"/> fires after every change so each view rebuilds its rows.
/// </summary>
public sealed partial class ChatHub : ObservableObject
{
    private readonly IChatService _chat;

    public ChatHub(IChatService chat) => _chat = chat;

    public IReadOnlyList<FolderDto> Folders { get; private set; } = [];

    public IReadOnlyList<SpaceDto> Spaces { get; private set; } = [];

    public IReadOnlyList<ConversationDto> Conversations { get; private set; } = [];

    /// <summary>The conversation shown in the chat page (null: a new one).</summary>
    [ObservableProperty]
    private Guid? _currentId;

    /// <summary>The folder a new conversation goes into (set by "new chat in this folder"), or null for unfiled.</summary>
    public Guid? NewChatFolder { get; set; }

    public event EventHandler? Changed;

    /// <summary>A first message waiting for the chat page (Home's composer sets it; the page takes it and starts a new conversation).</summary>
    public string? PendingStart { get; private set; }

    /// <summary>Raised when something (Home) asks to begin a new conversation with <see cref="PendingStart"/>; the shell shows the chat.</summary>
    public event EventHandler? StartRequested;

    public void Start(string text)
    {
        PendingStart = text;
        CurrentId = null;
        StartRequested?.Invoke(this, EventArgs.Empty);
    }

    public string? TakePending()
    {
        var text = PendingStart;
        PendingStart = null;
        return text;
    }

    /// <summary>Raised when something asks the chat page to show a conversation (null: a new, empty one).</summary>
    public event EventHandler<Guid?>? OpenRequested;

    public async Task RefreshAsync()
    {
        Folders = await _chat.FoldersAsync();
        Spaces = await _chat.SpacesAsync();
        Conversations = await _chat.ConversationsAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ConversationDto? Find(Guid id) => Conversations.FirstOrDefault(c => c.Id == id);

    public void Open(Guid? id)
    {
        CurrentId = id;
        OpenRequested?.Invoke(this, id);
    }

    public async Task RenameAsync(Guid id, string title)
    {
        if (Find(id) is { } c && !string.IsNullOrWhiteSpace(title))
        {
            await _chat.UpdateAsync(c, title: title.Trim());
            await RefreshAsync();
        }
    }

    public async Task PinAsync(Guid id, bool pinned)
    {
        if (Find(id) is { } c)
        {
            await _chat.UpdateAsync(c, pinned: pinned);
            await RefreshAsync();
        }
    }

    public async Task MoveAsync(Guid id, Guid? folderId)
    {
        if (Find(id) is { } c)
        {
            await _chat.UpdateAsync(c, folderId: folderId, moveToRoot: folderId is null);
            await RefreshAsync();
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        await _chat.DeleteAsync(id);
        if (CurrentId == id)
        {
            Open(null);
        }
        await RefreshAsync();
    }

    public async Task<FolderDto> CreateFolderAsync(string name)
    {
        var folder = await _chat.CreateFolderAsync(name);
        await RefreshAsync();
        return folder;
    }
}
