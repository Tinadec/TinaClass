using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TinaClass.App.Common.Localization;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Chat;
using TinaClass.Contracts.Chat;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using TKCode.Arc.Localization;

namespace TinaClass.App.Common.Shell;

/// <summary>
/// The studio layout's code: builds the sidebar's sections from the pages and the conversations, keeps the spend meter, and folds the
/// sidebar to a 64 px rail in a narrow window (under 1200 px, as the design says).
/// </summary>
public partial class StudioShell : ShellBase
{
    private bool _narrowFolded;
    private bool _wasNarrow;

    public StudioShell()
    {
        InitializeComponent();
        ArcLanguage.Changed += (_, _) => BuildSidebar();
        // A right-click on a conversation offers rename, pin, move and delete.
        Nav.AddHandler(PointerPressedEvent, OnNavPointerPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Arc's sidebar keeps its row controls to itself, so the row under the pointer is found by what it shows: a nested row (a conversation sits inside a
    /// folder row) whose label is the title of a conversation. Two conversations with the same title: the one that is open, else the first.
    /// </summary>
    private void OnNavPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Nav).Properties.IsRightButtonPressed || ViewModel is not { } vm)
        {
            return;
        }
        var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<TemplatedControl>().FirstOrDefault(c => c.GetType().Name == "SidebarRow");
        if (row is null || !row.Classes.Contains(":nested"))
        {
            return;
        }
        var title = row.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrEmpty(t.Text))?.Text;
        var matches = vm.Hub.Conversations.Where(c => c.Title == title).ToList();
        if (matches.Count > 0)
        {
            var conversation = matches.FirstOrDefault(c => vm.SelectedKey == "c:" + c.Id) ?? matches[0];
            ConversationMenu.Show(vm, conversation, row);
            e.Handled = true;
        }
    }

    public override PageHost Pages => Host;

    protected override Panel OverlayRoot => Root;

    protected override CommandPaletteDialog PaletteDialog => Palette;

    protected override Control PreferencesAnchor => Nav.IsCollapsed ? UserAvatar : UserButton;

    protected override bool HasSidebar => true;

    protected override void Prepare(ShellViewModel vm)
    {
        vm.BadgesChanged += (_, _) => BuildSidebar();
        vm.ConversationsChanged += (_, _) => BuildSidebar();
        BuildSidebar();
    }

    /// <summary>The sidebar's sections: the pages, then the conversations in folders (pinned first, unfiled last).</summary>
    private void BuildSidebar()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }
        var selected = vm.SelectedKey;
        Nav.Sections.Clear();
        var main = new SidebarSection { Header = Text.Get(AppLangResourceKind.NavSectionMain) };
        foreach (var page in vm.Pages.Where(p => p.InSidebar))
        {
            main.Items.Add(new SidebarItem { Key = page.Key, Label = page.Label, Icon = Lucide.Geometry(page.Icon), Badge = vm.BadgeOf(page.Key) });
        }
        Nav.Sections.Add(main);
        var folders = new SidebarSection { Header = Text.Get(AppLangResourceKind.NavSectionFolders) };
        var conversations = vm.Hub.Conversations;
        var pinned = conversations.Where(c => c.Pinned).ToList();
        if (pinned.Count > 0)
        {
            folders.Items.Add(Folder(Features.Chat.Localization.ChatLangResources.Get(Features.Chat.Localization.ChatLangResourceKind.Pinned), LucideIconKind.Pin, pinned));
        }
        foreach (var folder in vm.Hub.Folders.OrderBy(f => f.Sort))
        {
            folders.Items.Add(Folder(folder.Name, LucideIconKind.Folder, conversations.Where(c => c.FolderId == folder.Id && !c.Pinned).ToList()));
        }
        var loose = conversations.Where(c => c.FolderId is null && !c.Pinned).OrderByDescending(c => c.UpdatedAt).ToList();
        if (loose.Count > 0)
        {
            folders.Items.Add(Folder(Features.Chat.Localization.ChatLangResources.Get(Features.Chat.Localization.ChatLangResourceKind.Unfiled), LucideIconKind.MessageSquare, loose));
        }
        Nav.Sections.Add(folders);
        Nav.SelectedKey = selected;
    }

    private static SidebarItem Folder(string name, LucideIconKind icon, IReadOnlyList<Contracts.Chat.ConversationDto> items)
    {
        var folder = new SidebarItem { Key = "f:" + name, Label = name, Icon = Lucide.Geometry(icon), IsExpanded = true, Badge = items.Count.ToString() };
        foreach (var c in items.OrderByDescending(c => c.UpdatedAt))
        {
            folder.Items.Add(new SidebarItem { Key = "c:" + c.Id, Label = c.Title, Tag = c });
        }
        return folder;
    }

    private void OnUserClick(object? sender, RoutedEventArgs e)
    {
        if (Preferences.IsOpen)
        {
            ClosePreferences();
        }
        else
        {
            OpenPreferences();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // The design's narrow rule: under 1200 px the left wing is the 64 px rail. Only the folding this rule did is undone on widening.
        var narrow = e.NewSize.Width < 1200;
        if (narrow == _wasNarrow || ViewModel is not { } vm)
        {
            return;
        }
        _wasNarrow = narrow;
        if (narrow && !vm.IsSidebarCollapsed)
        {
            _narrowFolded = true;
            Nav.IsCollapsed = true;
        }
        else if (!narrow && _narrowFolded)
        {
            _narrowFolded = false;
            Nav.IsCollapsed = false;
        }
    }
}
