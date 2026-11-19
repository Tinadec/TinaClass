using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Localization;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Sample;
using TinaClass.App.Common.Shell;
using TinaClass.App.Common.Ui;
using TinaClass.App.Features.Chat;
using TinaClass.App.Features.Home.Localization;
using TinaClass.App.Features.Models;
using TinaClass.Contracts.Chat;
using TinaClass.Contracts.Sample;
using TKCode.Arc;

namespace TinaClass.App.Features.Home;

/// <summary>A recent conversation as a card on Home.</summary>
public sealed record RecentRow(Guid Id, string Title, string Meta);

/// <summary>
/// Home: a greeting, the composer, the starter prompts and the recent conversations. Sending from the composer asks the chat to begin
/// a new conversation with the text (<see cref="ChatHub.Start"/>).
/// </summary>
public sealed partial class HomeViewModel(ChatHub hub, ChatViewModel chat) : PageViewModel
{
    public ObservableCollection<RecentRow> Recent { get; } = [];

    public ChatHub Hub => hub;

    public IReadOnlyList<AiChatSuggestion> Suggestions => ChatViewModel.Suggestions();

    public IReadOnlyList<AiModel> Models => chat.Models;

    public AiModel? DefaultModel => chat.Models.FirstOrDefault(m => m.Id == chat.DefaultModelId) ?? chat.Models.FirstOrDefault();

    public bool HasRecent => Recent.Count > 0;

    public string Greeting
    {
        get
        {
            var name = SampleCore.People[0].Name;
            var hour = SampleClock.Now.LocalDateTime.Hour;
            var kind = hour < 12 ? HomeLangResourceKind.GreetingMorning : hour < 18 ? HomeLangResourceKind.GreetingAfternoon : HomeLangResourceKind.GreetingEvening;
            return string.Format(HomeLangResources.Get(kind), name);
        }
    }

    protected override async Task LoadAsync()
    {
        try
        {
            await chat.ActivateAsync();
            await hub.RefreshAsync();
            Rebuild();
            hub.Changed += (_, _) => Rebuild();
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>The cards follow the conversations and the language.</summary>
    public void Rebuild()
    {
        Recent.Clear();
        foreach (var c in hub.Conversations.OrderByDescending(c => c.UpdatedAt).Take(4))
        {
            var folder = hub.Folders.FirstOrDefault(f => f.Id == c.FolderId)?.Name;
            Recent.Add(new RecentRow(c.Id, c.Title, (folder is null ? string.Empty : folder + " · ") + TimeText.Short(c.UpdatedAt)));
        }
        OnPropertyChanged(nameof(HasRecent));
        OnPropertyChanged(nameof(Greeting));
    }

    [RelayCommand]
    private void Open(RecentRow row) => hub.Open(row.Id);

    [RelayCommand]
    private void Retry() => _ = RefreshAsync();
}
