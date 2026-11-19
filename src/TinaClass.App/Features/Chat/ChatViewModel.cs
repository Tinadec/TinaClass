using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Chat.Localization;
using TinaClass.App.Features.Models;
using TinaClass.Contracts.Models;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;

namespace TinaClass.App.Features.Chat;

/// <summary>
/// The chat page: the models and starter prompts Arc's <c>AiChat</c> offers, the responder that answers, and opening a conversation
/// from the list (the sidebar, the command palette, Home). The inspector on the right is <see cref="Inspector"/>.
/// </summary>
public sealed partial class ChatViewModel : PageViewModel
{
    private readonly IChatService _chat;
    private readonly IModelsService _models;

    public ChatViewModel(IChatService chat, IModelsService models, ChatHub hub, ChatSession session, ChatResponder responder)
    {
        _chat = chat;
        _models = models;
        Hub = hub;
        Session = session;
        Responder = responder;
        // The top bar names the conversation and offers the inspector's switch (design 6: a breadcrumb and the right wing's button).
        Hub.Changed += (_, _) => RebuildBar();
        Hub.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatHub.CurrentId))
            {
                RebuildBar();
            }
        };
        RebuildBar();
    }

    private void RebuildBar()
    {
        var title = Hub.CurrentId is { } id ? Hub.Find(id)?.Title : null;
        Crumbs = string.IsNullOrWhiteSpace(title) ? [] : [title];
        Actions = [new PageAction(() => ChatLangResources.Get(ChatLangResourceKind.ToggleInspector), ToggleInspectorCommand, LucideIconKind.PanelRight, IconOnly: true)];
    }

    /// <summary>The words follow the language.</summary>
    public void RefreshTexts() => RebuildBar();

    public ChatHub Hub { get; }

    public ChatSession Session { get; }

    public ChatInspectorViewModel Inspector => Session.Inspector;

    public IAiChatResponder Responder { get; }

    public AiModelProvider[] Providers { get; private set; } = [];

    public AiModel[] Models { get; private set; } = [];

    public string? DefaultModelId { get; private set; }

    /// <summary>The starter prompts under the empty chat (and on Home).</summary>
    public static IReadOnlyList<AiChatSuggestion> Suggestions() =>
    [
        new() { Label = ChatLangResources.Get(ChatLangResourceKind.StarterSummarize), Prompt = ChatLangResources.Get(ChatLangResourceKind.StarterSummarizePrompt), Icon = Lucide.Geometry(LucideIconKind.FileText) },
        new() { Label = ChatLangResources.Get(ChatLangResourceKind.StarterPainPoints), Prompt = ChatLangResources.Get(ChatLangResourceKind.StarterPainPointsPrompt), Icon = Lucide.Geometry(LucideIconKind.ChartColumn) },
        new() { Label = ChatLangResources.Get(ChatLangResourceKind.StarterSql), Prompt = ChatLangResources.Get(ChatLangResourceKind.StarterSqlPrompt), Icon = Lucide.Geometry(LucideIconKind.Database) },
        new() { Label = ChatLangResources.Get(ChatLangResourceKind.StarterWeekly), Prompt = ChatLangResources.Get(ChatLangResourceKind.StarterWeeklyPrompt), Icon = Lucide.Geometry(LucideIconKind.NotebookPen) },
    ];

    protected override async Task LoadAsync()
    {
        try
        {
            var providers = await _models.ProvidersAsync();
            var all = await _models.ModelsAsync(false);
            var chatModels = all.Where(m => m.Kind == "chat").ToList();
            var names = chatModels.Select(m => (m.ProviderId, m.ProviderName)).Distinct().ToList();
            Providers = [.. names.Select(p => new AiModelProvider { Id = p.ProviderId.ToString(), Name = p.ProviderName })];
            Models = [.. chatModels.Select(ToModel)];
            DefaultModelId = chatModels.FirstOrDefault(m => m.IsDefault)?.Code ?? chatModels.FirstOrDefault()?.Code;
            await Hub.RefreshAsync();
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    public static AiModel ToModel(ModelDto m)
    {
        var efforts = m.Efforts.Select(e => e switch { "deep" => ModelEffort.High, "balanced" => ModelEffort.Medium, _ => ModelEffort.Low }).Distinct().OrderBy(e => e).ToList();
        return new AiModel
        {
            Id = m.Code,
            Name = m.DisplayName,
            ShortName = m.DisplayName,
            ProviderId = m.ProviderId.ToString(),
            Blurb = m.Note,
            ContextTokens = m.ContextTokens,
            InputPrice = m.PriceIn / 100m,
            OutputPrice = m.PriceOut / 100m,
            EffortLevels = efforts.Count > 1 ? efforts : null,
            DefaultEffort = efforts.Count > 1 ? efforts[efforts.Count / 2] : null,
        };
    }

    /// <summary>Opens a conversation in the chat block: its turns from the server (null: a new, empty conversation).</summary>
    public async Task<AiChatConversation?> LoadConversationAsync(Guid id)
    {
        if (Hub.Find(id) is not { } dto)
        {
            await Hub.RefreshAsync();
            dto = Hub.Find(id)!;
            if (dto is null)
            {
                return null;
            }
        }
        var messages = await _chat.MessagesAsync(id);
        Session.Load(id, messages);
        var conversation = ChatMapper.ToConversation(dto, messages);
        ShowAnswer(messages, dto);
        return conversation;
    }

    /// <summary>Shows the last answer of a conversation that is already open in the chat block (switching back to it).</summary>
    public async Task RefreshInspectorAsync(Guid id)
    {
        if (Hub.Find(id) is { } dto)
        {
            ShowAnswer(await _chat.MessagesAsync(id), dto);
        }
    }

    /// <summary>The right wing shows the last answer of a conversation just opened.</summary>
    private void ShowAnswer(IReadOnlyList<Contracts.Chat.MessageDto> messages, Contracts.Chat.ConversationDto dto)
    {
        var last = messages.LastOrDefault(m => m.Role == Contracts.Chat.MessageRoles.Assistant);
        Inspector.Reset(Models.FirstOrDefault(m => m.Id == last?.ModelCode)?.DisplayName ?? last?.ModelCode ?? "—");
        Inspector.StartedAt = null;
        Inspector.Status = GenerationStatus.Idle;
        if (last is null)
        {
            return;
        }
        if (last.Blocks.FirstOrDefault(b => b.Kind == "plan")?.Steps is { } steps)
        {
            Inspector.SetPlan(steps);
        }
        foreach (var c in last.Citations.OrderBy(c => c.Position))
        {
            Inspector.AddSource(c.Position, c.DocumentName, c.Where, c.Quote, c.Score);
        }
        if (last.Usage is { } usage)
        {
            Inspector.SetUsage(usage, Models.FirstOrDefault(m => m.Id == last.ModelCode)?.ContextTokens ?? 128_000);
            Inspector.Status = last.Status == Contracts.Chat.MessageStatuses.Stopped ? GenerationStatus.Stopped : GenerationStatus.Done;
        }
    }

    /// <summary>Whether the right wing is folded away (the page folds it by itself in a narrow window).</summary>
    [ObservableProperty]
    private bool _isInspectorCollapsed;

    [RelayCommand]
    private void ToggleInspector() => IsInspectorCollapsed = !IsInspectorCollapsed;
}
