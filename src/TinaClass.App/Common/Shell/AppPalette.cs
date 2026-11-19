using Avalonia.Controls;
using TinaClass.App.Common.Localization;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Chat;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using TKCode.Arc.Localization;

namespace TinaClass.App.Common.Shell;

/// <summary>
/// The command palette (Cmd+K) on Arc's <c>CommandPaletteDialog</c>: pages to go to, conversations that match, and a few actions. With a query that
/// matches nothing, or with Tab, the last entry asks the assistant (a new chat that begins with the query). The dialog filters by itself;
/// this class only fills its items, and refills them when the language or the conversations change.
/// </summary>
public sealed class AppPalette
{
    private readonly CommandPaletteDialog _dialog;
    private readonly ShellViewModel _vm;

    public AppPalette(CommandPaletteDialog dialog, ShellViewModel vm)
    {
        _dialog = dialog;
        _vm = vm;
        dialog.IsFilteringEnabled = true;
        dialog.PanelWidth = 640;
        Labels();
        ArcLanguage.Changed += (_, _) => Fill();
        vm.ConversationsChanged += (_, _) => Fill();
        dialog.Opened += (_, _) => Fill();
        dialog.ItemInvoked += (_, e) =>
        {
            if (e.Item.Tag is Action action)
            {
                action();
            }
        };
        dialog.PropertyChanged += (_, e) =>
        {
            if (e.Property == CommandPaletteDialog.QueryProperty)
            {
                Fill();
            }
        };
        Fill();
    }

    public void Open() => _dialog.Open();

    private void Labels()
    {
        _dialog.PlaceholderText = Text.Get(AppLangResourceKind.Palette);
        _dialog.EmptyTitle = Text.Get(AppLangResourceKind.PaletteEmpty);
        _dialog.EmptyDescription = Text.Get(AppLangResourceKind.PaletteEmptyHint);
        _dialog.CloseButtonLabel = Text.Get(AppLangResourceKind.PaletteClose);
        _dialog.ResultsLabel = Text.Get(AppLangResourceKind.PaletteResults);
        _dialog.Label = Text.Get(AppLangResourceKind.Palette);
    }

    private void Fill()
    {
        Labels();
        var pages = Text.Get(AppLangResourceKind.PaletteGroupPages);
        var chats = Text.Get(AppLangResourceKind.PaletteGroupChats);
        var actions = Text.Get(AppLangResourceKind.PaletteGroupActions);
        var items = new List<CommandPaletteItem>();
        foreach (var page in AppPages.All.Where(p => p.InSidebar))
        {
            var key = page.Key;
            items.Add(Item("page:" + key, page.Label, pages, LucideIconKind.ChevronRight, () => _vm.Navigate(key)));
        }
        foreach (var c in _vm.Hub.Conversations.OrderByDescending(c => c.UpdatedAt).Take(12))
        {
            var id = c.Id;
            items.Add(Item("chat:" + id, c.Title, chats, LucideIconKind.MessageSquare, () => _vm.Hub.Open(id)));
        }
        items.Add(Item("act:new", Text.Get(AppLangResourceKind.ActionNewChat), actions, LucideIconKind.Plus, () => _vm.NewChatCommand.Execute(null), "Mod+N"));
        items.Add(Item("act:theme", Text.Get(AppLangResourceKind.ActionToggleTheme), actions, LucideIconKind.Sun, () => _vm.ToggleThemeCommand.Execute(null), "Mod+Shift+L"));
        items.Add(Item("act:sidebar", Text.Get(AppLangResourceKind.ActionToggleSidebar), actions, LucideIconKind.PanelLeft, () => _vm.ToggleSidebarCommand.Execute(null), "Mod+B"));
        if (!string.IsNullOrWhiteSpace(_dialog.Query))
        {
            var query = _dialog.Query.Trim();
            var ask = Item("ask", Text.Format(AppLangResourceKind.PaletteAsk, query), Text.Get(AppLangResourceKind.PaletteGroupActions), LucideIconKind.Sparkles, () => _vm.Hub.Start(query));
            ask.Keywords = [query];
            items.Add(ask);
        }
        _dialog.Items.Clear();
        _dialog.Items.AddRange(items);
    }

    private static CommandPaletteItem Item(string id, string label, string group, LucideIconKind icon, Action run, string? gesture = null) => new()
    {
        Id = id,
        Label = label,
        Group = group,
        Icon = Lucide.Geometry(icon),
        Shortcut = gesture is null ? null : Ui.KeyCap.Format(gesture),
        Tag = run,
    };
}
