using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Settings;
using TinaClass.App.Common.Shell;
using TKCode.Arc;
using Xunit;

namespace TinaClass.App.Tests.Layouts;

/// <summary>The studio layout (the default, scheme B): the sidebar with the conversations, the page, the inspector of the chat on the right.</summary>
public class StudioLayoutTests
{
    private static IEnumerable<SidebarItem> Flatten(SidebarItem item) => new[] { item }.Concat(item.Items.SelectMany(Flatten));

    [AvaloniaFact]
    public void The_window_shows_the_studio_shell()
    {
        var window = Ui.Open();

        Assert.IsType<StudioShell>(window.Shell);
        Assert.True(Ui.Shell.HasSidebar);
    }

    [AvaloniaFact]
    public void The_sidebar_lists_the_conversations_and_the_chat_keeps_its_inspector_open()
    {
        var window = Ui.Open();
        Ui.Go(AppPages.Chat);

        var keys = Ui.Nav(window).Sections.SelectMany(section => section.Items).SelectMany(Flatten).Select(item => item.Key).OfType<string>().ToList();
        Assert.Contains(keys, key => key.StartsWith("c:", StringComparison.Ordinal));
        Assert.Equal(Ui.Shell.Hub.Conversations.Count, keys.Count(key => key.StartsWith("c:", StringComparison.Ordinal)));
        Assert.False(Ui.Service<TinaClass.App.Features.Chat.ChatViewModel>().IsInspectorCollapsed);
    }

    [AvaloniaFact]
    public void The_sidebar_lists_the_pages_in_the_order_of_the_page_list()
    {
        var window = Ui.Open();
        Assert.Equal("TinaClass", window.Title);
        Assert.Equal(AppPages.Chat, Ui.Shell.CurrentPage?.Key);
        var entries = Ui.Nav(window).Sections.SelectMany(section => section.Items).Select(item => item.Key).OfType<string>().ToList();
        // The pages that belong in the sidebar come in the order of AppPages (the conversations and folders sit among them).
        var pages = AppPages.All.Where(page => page.InSidebar).Select(page => page.Key).ToList();
        Assert.Equal(pages, entries.Where(key => pages.Contains(key)));
        Assert.DoesNotContain(AppPages.Home, entries);
    }

    [AvaloniaFact]
    public void Every_page_can_be_opened_and_the_sidebar_follows()
    {
        var window = Ui.Open();
        foreach (var page in AppPages.All)
        {
            Ui.Go(page.Key);
            Assert.Equal(page.Key, Ui.Shell.CurrentPage?.Key);
            Assert.NotNull(Ui.Pages(window).View);
            if (page.InSidebar && page.Key != AppPages.Chat)
            {
                Assert.Equal(page.Key, Ui.Nav(window).SelectedKey);
            }
        }
        // Choosing an entry in the sidebar shows its page.
        var last = AppPages.All[^1].Key;
        Ui.Go(AppPages.Chat);
        Ui.Nav(window).SelectedKey = last;
        Ui.Settle(6);
        Assert.Equal(last, Ui.Shell.CurrentPage?.Key);
    }

    [AvaloniaFact]
    public void The_sidebar_folds_to_a_rail_and_remembers_it()
    {
        var window = Ui.Open();
        Ui.Shell.IsSidebarCollapsed = false;
        Ui.Settle(6);
        Assert.False(Ui.Nav(window).IsCollapsed);

        Ui.Shell.ToggleSidebarCommand.Execute(null);
        Ui.Settle(6);
        Assert.True(Ui.Nav(window).IsCollapsed);

        // The next run starts as a rail.
        UserSettings.Reset();
        Assert.True(UserSettings.Current.IsSidebarCollapsed);
    }
}
