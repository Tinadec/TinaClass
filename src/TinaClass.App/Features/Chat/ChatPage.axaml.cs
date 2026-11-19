using Avalonia;
using Avalonia.Controls;
using TinaClass.App.Features.Chat.Localization;
using TKCode.Arc;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Chat;

/// <summary>
/// The chat: Arc's <c>AiChat</c> in the middle, the inspector in the right wing. The chat block keeps the open conversations while the
/// app runs; this page fills it from the service (<see cref="ChatViewModel.LoadConversationAsync"/>), opens the one the sidebar or
/// palette asks for, and folds the right wing away in a narrow window (under 1360 px, as the design says).
/// </summary>
public partial class ChatPage : UserControl
{
    private ChatViewModel? _viewModel;
    private bool _wasNarrow;

    public ChatPage()
    {
        InitializeComponent();
        Chat.ConversationOpened += (_, e) =>
        {
            if (Guid.TryParse(e.Conversation.Id, out var id) && _viewModel is { } vm)
            {
                vm.Hub.CurrentId = id;
                _ = ShowInspectorOfAsync(vm, id);
            }
        };
        Chat.ConversationCreated += (_, _) => { };
        Chat.Labels = ChatLabels();
        ArcLanguage.Changed += (_, _) =>
        {
            Chat.Labels = ChatLabels();
            if (_viewModel is { } vm)
            {
                ApplySuggestions(vm);
                vm.RefreshTexts();
            }
        };
    }

    /// <summary>AiChat's own words (Thinking, Copy, Stop ...) in the app's language; what is not set keeps Arc's English.</summary>
    private static AiChatLabels ChatLabels()
    {
        static string L(ChatLangResourceKind kind) => ChatLangResources.Get(kind);
        return new AiChatLabels
        {
            Greeting = L(ChatLangResourceKind.Greeting),
            Placeholder = L(ChatLangResourceKind.Placeholder),
            Thinking = L(ChatLangResourceKind.AiThinking),
            ThoughtFor = seconds => ChatLangResources.Format(ChatLangResourceKind.AiThoughtFor, seconds),
            Copy = L(ChatLangResourceKind.AiCopy),
            Copied = L(ChatLangResourceKind.AiCopied),
            Edit = L(ChatLangResourceKind.AiEdit),
            Cancel = L(ChatLangResourceKind.AiCancel),
            Send = L(ChatLangResourceKind.AiSend),
            SendMessage = L(ChatLangResourceKind.AiSend),
            Retry = L(ChatLangResourceKind.AiRetry),
            GoodResponse = L(ChatLangResourceKind.AiGood),
            BadResponse = L(ChatLangResourceKind.AiBad),
            Stopped = L(ChatLangResourceKind.AiStopped),
            StoppedBeforeAnswering = L(ChatLangResourceKind.AiStoppedEarly),
            ReplyFailed = L(ChatLangResourceKind.AiReplyFailed),
            StopReply = L(ChatLangResourceKind.AiStop),
            AttachFiles = L(ChatLangResourceKind.AiAttach),
            Sources = L(ChatLangResourceKind.AiSources),
            Searches = L(ChatLangResourceKind.AiSearches),
            Result = L(ChatLangResourceKind.AiResult),
            NewChat = L(ChatLangResourceKind.AiNewChat),
            UntitledChat = L(ChatLangResourceKind.AiUntitled),
            ScrollToLatest = L(ChatLangResourceKind.AiScroll),
            PreviousVersion = L(ChatLangResourceKind.AiPrevVersion),
            NextVersion = L(ChatLangResourceKind.AiNextVersion),
            Version = (i, n) => ChatLangResources.Format(ChatLangResourceKind.AiVersion, i, n),
            Composer = new AiComposerLabels
            {
                Placeholder = L(ChatLangResourceKind.Placeholder),
                Send = L(ChatLangResourceKind.AiSend),
                Stop = L(ChatLangResourceKind.AiStop),
                Attach = L(ChatLangResourceKind.AiAttach),
            },
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ChatViewModel vm || vm == _viewModel)
        {
            return;
        }
        _viewModel = vm;
        vm.Hub.OpenRequested += async (_, id) => await OpenAsync(vm, id);
        vm.Hub.StartRequested += async (_, _) => await StartPendingAsync(vm);
        vm.PropertyChanged += async (_, p) =>
        {
            if (p.PropertyName == nameof(ChatViewModel.IsLoading) && !vm.IsLoading)
            {
                await FillAsync(vm);
            }
        };
        if (!vm.IsLoading && vm.Models.Length > 0)
        {
            _ = FillAsync(vm);
        }
    }

    /// <summary>After the data is loaded: the models and starters, then the conversation the app opens with.</summary>
    private async Task FillAsync(ChatViewModel vm)
    {
        if (Chat.Models.Count == 0)
        {
            Chat.Providers.AddRange(vm.Providers);
            Chat.Models.AddRange(vm.Models);
            Chat.SelectedModel = Chat.Models.FirstOrDefault(m => m.Id == vm.DefaultModelId) ?? Chat.Models.FirstOrDefault();
            ApplySuggestions(vm);
        }
        if (vm.Hub.PendingStart is not null)
        {
            await StartPendingAsync(vm);
            return;
        }
        // The conversation shown when the page opens: the one the sidebar picked, else the most recent.
        if (Chat.Conversations.Count == 0)
        {
            var id = vm.Hub.CurrentId ?? vm.Hub.Conversations.FirstOrDefault()?.Id;
            if (id is not null)
            {
                await OpenAsync(vm, id);
            }
        }
    }

    /// <summary>Home's composer: a new conversation that begins with the text it sent.</summary>
    private async Task StartPendingAsync(ChatViewModel vm)
    {
        if (Chat.Models.Count == 0)
        {
            return; // FillAsync calls this again once the models are in.
        }
        if (vm.Hub.TakePending() is { Length: > 0 } text)
        {
            await OpenAsync(vm, null);
            Chat.Send(text, []);
        }
    }

    private void ApplySuggestions(ChatViewModel vm)
    {
        Chat.Suggestions.Clear();
        Chat.Suggestions.AddRange(ChatViewModel.Suggestions());
    }

    /// <summary>Shows a conversation (opening it from the server the first time) or a new, empty one.</summary>
    public async Task OpenAsync(ChatViewModel vm, Guid? id)
    {
        if (id is null)
        {
            Chat.NewChat();
            vm.Inspector.Reset("—");
            vm.Inspector.Status = GenerationStatus.Idle;
            return;
        }
        var key = id.ToString();
        var open = Chat.Conversations.FirstOrDefault(c => c.Id == key);
        if (open is null)
        {
            open = await vm.LoadConversationAsync(id.Value);
            if (open is null)
            {
                return;
            }
            Chat.Conversations.Add(open);
        }
        else
        {
            await ShowInspectorOfAsync(vm, id.Value);
        }
        Chat.CurrentConversation = open;
    }

    private static async Task ShowInspectorOfAsync(ChatViewModel vm, Guid id) => await vm.RefreshInspectorAsync(id);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // The design's narrow rule: under 1360 px of window the right wing folds away (the page is 272 px narrower than the window).
        var narrow = e.NewSize.Width + 272 < 1360;
        if (narrow != _wasNarrow && _viewModel is { } vm)
        {
            _wasNarrow = narrow;
            vm.IsInspectorCollapsed = narrow;
        }
    }
}
