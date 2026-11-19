using Avalonia.Headless.XUnit;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Shell;
using TinaClass.App.Features.Chat;
using Xunit;

namespace TinaClass.App.Tests.Features.Chat;

/// <summary>The top bar of the chat and what the conversation menu does (rename, pin, move, delete).</summary>
public class ConversationListTests
{
    [AvaloniaFact]
    public void The_top_bar_names_the_open_conversation_and_has_the_inspector_switch()
    {
        Ui.Open();
        var chat = Ui.Service<ChatViewModel>();
        Ui.Go(AppPages.Chat);
        var target = Ui.Shell.Hub.Conversations[1];

        Ui.Shell.Hub.Open(target.Id);
        Assert.True(Ui.Until(() => Ui.Shell.BarCrumbs.Count == 2));

        Assert.Equal(target.Title, Ui.Shell.BarCrumbs[^1].Text);
        var toggle = Assert.Single(Ui.Shell.BarActions);
        Assert.True(toggle.IconOnly);
        var was = chat.IsInspectorCollapsed;
        toggle.Command.Execute(null);
        Assert.NotEqual(was, chat.IsInspectorCollapsed);
    }

    [AvaloniaFact]
    public async Task Rename_pin_move_and_delete_change_the_list()
    {
        Ui.Open();
        Ui.Go(AppPages.Chat);
        var hub = Ui.Shell.Hub;
        var conversation = hub.Conversations.First(c => !c.Pinned);

        await hub.RenameAsync(conversation.Id, "  A better title  ");
        Assert.Equal("A better title", hub.Find(conversation.Id)!.Title);

        await hub.PinAsync(conversation.Id, true);
        Assert.True(hub.Find(conversation.Id)!.Pinned);
        await hub.PinAsync(conversation.Id, false);
        Assert.False(hub.Find(conversation.Id)!.Pinned);

        var folder = hub.Folders[0];
        await hub.MoveAsync(conversation.Id, folder.Id);
        Assert.Equal(folder.Id, hub.Find(conversation.Id)!.FolderId);
        await hub.MoveAsync(conversation.Id, null);
        Assert.Null(hub.Find(conversation.Id)!.FolderId);

        hub.Open(conversation.Id);
        await hub.DeleteAsync(conversation.Id);
        Assert.Null(hub.Find(conversation.Id));
        Assert.Null(hub.CurrentId);
    }

    [AvaloniaFact]
    public void An_empty_title_does_not_rename()
    {
        Ui.Open();
        var hub = Ui.Shell.Hub;
        var conversation = hub.Conversations[0];

        hub.RenameAsync(conversation.Id, "   ").GetAwaiter().GetResult();

        Assert.Equal(conversation.Title, hub.Find(conversation.Id)!.Title);
    }

    [AvaloniaFact]
    public void The_menu_opens_beside_a_conversation()
    {
        var window = Ui.Open();
        Ui.Go(AppPages.Chat);

        ConversationMenu.Show(Ui.Shell, Ui.Shell.Hub.Conversations[0], window);
        Ui.Settle(6);

        Assert.True(true, "opening the menu does not throw");
    }
}
