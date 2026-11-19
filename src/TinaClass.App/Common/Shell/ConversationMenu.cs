using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Localization;
using TinaClass.App.Features.Chat.Localization;
using TinaClass.Contracts.Chat;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;

namespace TinaClass.App.Common.Shell;

/// <summary>
/// What a right-click on a conversation offers (design 6): rename, pin, move to a folder, delete. Arc's <c>MenuSurface</c> opens beside the row, the
/// rename is a dialog with one <c>Input</c> and the deletion asks first. Every layout that lists conversations opens it with <see cref="Show"/>.
/// </summary>
public static class ConversationMenu
{
    public static void Show(ShellViewModel vm, ConversationDto conversation, Control anchor)
    {
        var hub = vm.Hub;
        var menu = new MenuSurface { Side = MenuSide.Right, Alignment = MenuAlignment.Start, AnchorGap = 6 };
        menu.Items.Add(Entry(ChatLangResourceKind.Rename, LucideIconKind.Pencil, () => _ = RenameAsync(vm, conversation, anchor)));
        menu.Items.Add(Entry(conversation.Pinned ? ChatLangResourceKind.Unpin : ChatLangResourceKind.Pin, conversation.Pinned ? LucideIconKind.PinOff : LucideIconKind.Pin,
            () => _ = Guard(anchor, () => hub.PinAsync(conversation.Id, !conversation.Pinned))));
        var move = new MenuEntry { Header = ChatLangResources.Get(ChatLangResourceKind.MoveTo), Icon = new LucideIcon { Kind = LucideIconKind.FolderInput, Size = 16 } };
        move.Items.Add(Where(vm, anchor, conversation, ChatLangResources.Get(ChatLangResourceKind.Unfiled), null));
        foreach (var folder in hub.Folders.OrderBy(f => f.Sort))
        {
            move.Items.Add(Where(vm, anchor, conversation, folder.Name, folder.Id));
        }
        menu.Items.Add(move);
        menu.Items.Add(Entry(ChatLangResourceKind.DeleteChat, LucideIconKind.Trash2, () => _ = DeleteAsync(vm, conversation, anchor)));
        menu.Open(anchor, MenuOpenFocus.Surface);
    }

    private static MenuEntry Entry(ChatLangResourceKind header, LucideIconKind icon, Action action) =>
        new() { Header = ChatLangResources.Get(header), Icon = new LucideIcon { Kind = icon, Size = 16 }, Command = new RelayCommand(action) };

    /// <summary>One folder of "Move to folder": a radio mark on the one the conversation is in.</summary>
    private static MenuEntry Where(ShellViewModel vm, Control anchor, ConversationDto conversation, string name, Guid? folderId) => new()
    {
        Header = name,
        ToggleType = MenuEntryToggleType.Radio,
        GroupName = "conversation-folder",
        IsChecked = conversation.FolderId == folderId,
        Command = new RelayCommand(() => _ = Guard(anchor, () => vm.Hub.MoveAsync(conversation.Id, folderId))),
    };

    private static async Task RenameAsync(ShellViewModel vm, ConversationDto conversation, Control owner)
    {
        var title = new Input { Label = ChatLangResources.Get(ChatLangResourceKind.RenameLabel), Text = conversation.Title };
        var body = new StackPanel { MinWidth = 360, Children = { title } };
        var result = await Dialog.ShowAsync(owner, ChatLangResources.Get(ChatLangResourceKind.RenameTitle), body, null,
            new DialogAction(Text.Get(AppLangResourceKind.Cancel), false, ButtonVariant.Secondary), new DialogAction(Text.Get(AppLangResourceKind.Save), true, ButtonVariant.Primary));
        if (result is true && !string.IsNullOrWhiteSpace(title.Text))
        {
            await Guard(owner, () => vm.Hub.RenameAsync(conversation.Id, title.Text));
        }
    }

    private static async Task DeleteAsync(ShellViewModel vm, ConversationDto conversation, Control owner)
    {
        var body = new TextBlock { Text = ChatLangResources.Format(ChatLangResourceKind.DeleteBody, conversation.Title), TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 360 };
        var result = await Dialog.ShowAsync(owner, ChatLangResources.Get(ChatLangResourceKind.DeleteTitle), body, null,
            new DialogAction(Text.Get(AppLangResourceKind.Cancel), false, ButtonVariant.Secondary), new DialogAction(Text.Get(AppLangResourceKind.Delete), true, ButtonVariant.Danger));
        if (result is true)
        {
            await Guard(owner, () => vm.Hub.DeleteAsync(conversation.Id));
        }
    }

    /// <summary>A change the server refused (the conversation is gone, no permission) is shown in a toast instead of crashing the handler.</summary>
    private static async Task Guard(Control owner, Func<Task> change)
    {
        try
        {
            await change();
        }
        catch (ServiceException e)
        {
            Toast.Show(owner, e.Message, ToastKind.Error);
        }
    }
}
