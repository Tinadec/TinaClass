using Avalonia.Controls;
using TinaClass.App.Common.Navigation;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using TKCode.Arc.Localization;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Api.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App.Common.Shell;

/// <summary>
/// What every layout's shell shares: it shows the page the view model asks for, opens the command palette, offers the preferences menu
/// (and, with the API, covers the app with the sign-in screen). A layout (<c>StudioShell</c>, <c>FocusShell</c>, <c>SpacesShell</c>) is a
/// derived view that arranges the sidebar, top bar and page differently and names its page host, overlay panel and palette.
/// </summary>
public abstract class ShellBase : UserControl
{
    private AppPalette? _palette;
    private MenuSurface? _preferences;

    protected ShellViewModel? ViewModel => DataContext as ShellViewModel;

    /// <summary>Where the pages show.</summary>
    public abstract PageHost Pages { get; }

    /// <summary>The panel over everything (palette, sign-in).</summary>
    protected abstract Panel OverlayRoot { get; }

    protected abstract CommandPaletteDialog PaletteDialog { get; }

    /// <summary>What the preferences menu opens beside, for the current state of the layout.</summary>
    protected abstract Control PreferencesAnchor { get; }

    /// <summary>Whether this layout has a sidebar the title bar button folds.</summary>
    protected abstract bool HasSidebar { get; }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is not { } vm)
        {
            return;
        }
        vm.HasSidebar = HasSidebar;
        _palette ??= new AppPalette(PaletteDialog, vm);
        vm.PaletteRequested += (_, _) => _palette.Open();
        _preferences = PreferencesMenu.Create(vm);
        Prepare(vm);
        // The app starts on the chat (the first conversation), as the design's default screen.
        vm.Navigate(vm.SelectedKey ?? AppPages.Chat);
        _ = vm.LoadAsync();
        AttachSignIn(vm);
    }

    /// <summary>A layout builds its sidebar or rail from the view model here.</summary>
    protected virtual void Prepare(ShellViewModel vm)
    {
    }

    public MenuSurface Preferences => _preferences!;

    /// <summary>Opens the preferences menu beside the user button.</summary>
    public void OpenPreferences(MenuOpenFocus focus = MenuOpenFocus.Surface)
    {
        var menu = Preferences;
        menu.FocusReturnTarget = PreferencesAnchor;
        menu.Open(PreferencesAnchor, focus);
    }

    public void ClosePreferences() => Preferences.Close(returnFocus: false);

    /// <summary>
    /// With <c>"dataSource": "api"</c>: covers the app with the sign-in screen until someone has signed in, forgets the pages when they
    /// sign out (the next person starts fresh) and shows them again after the next sign-in, and offers "Sign out" in the preferences
    /// menu. On the sample data none of this exists.
    /// </summary>
    private void AttachSignIn(ShellViewModel vm)
    {
        if (!ApiSettings.Current.UsesApi || DesktopApp.Services.GetService<SignInViewModel>() is not { } signIn)
        {
            return;
        }
        OverlayRoot.Children.Add(new SignInOverlay(signIn));
        var signOut = new MenuEntry
        {
            Header = ApiLangResources.Get(ApiLangResourceKind.SignOut),
            Icon = new LucideIcon { Kind = LucideIconKind.LogOut },
            Command = signIn.SignOutCommand,
        };
        Preferences.Items.Add(signOut);
        ArcLanguage.Changed += (_, _) => signOut.Header = ApiLangResources.Get(ApiLangResourceKind.SignOut);
        signIn.Session.SignedOut += (_, _) => Pages.Clear();
        signIn.Session.SignedIn += (_, _) =>
        {
            if (vm.CurrentPage is { } page && Pages.Page is null)
            {
                Pages.SetCurrentValue(PageHost.PageProperty, page);
            }
            _ = vm.LoadAsync();
        };
        // A "keep me signed in" session from last time signs in without asking.
        _ = signIn.TryResumeAsync();
    }

}
