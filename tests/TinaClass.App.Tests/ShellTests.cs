using Avalonia.Headless.XUnit;
using TinaClass.App.Common;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Settings;
using TinaClass.App.Common.Theme;
using TKCode.Arc;
using TKCode.Arc.Localization;
using TKCode.Arc.Motion;
using Xunit;

namespace TinaClass.App.Tests;

/// <summary>The window's chrome: the sidebar, pages, appearance, language and motion.</summary>
public class ShellTests
{
    [AvaloniaFact]
    public void The_window_opens_on_the_chat_with_the_title_of_the_app()
    {
        var window = Ui.Open();
        Assert.Equal("TinaClass", window.Title);
        Assert.Equal(AppPages.Chat, Ui.Shell.CurrentPage?.Key);
        // Home is a page of its own, not an entry of the navigation.
        Assert.False(AppPages.All.Single(page => page.Key == AppPages.Home).InSidebar);
    }

    [AvaloniaFact]
    public void Every_page_can_be_opened_and_shows_its_view()
    {
        var window = Ui.Open();
        foreach (var page in AppPages.All)
        {
            Ui.Go(page.Key);
            Assert.Equal(page.Key, Ui.Shell.CurrentPage?.Key);
            if (page.Key != AppPages.Chat)
            {
                Assert.Equal(page.Key, Ui.Shell.SelectedKey);
            }
            Assert.NotNull(Ui.Pages(window).View);
        }
    }

    [AvaloniaFact]
    public void The_preferences_menu_has_appearance_language_and_motion()
    {
        var window = Ui.Open();
        var view = Ui.View(window);
        view.OpenPreferences();
        Ui.Settle(6);
        Assert.True(view.Preferences.IsOpen);
        var rows = view.Preferences.Items.OfType<MenuEntry>().Where(row => row.HasSubmenu).ToList();
        // Appearance, language and motion.
        Assert.Equal(3, rows.Count);
        view.ClosePreferences();
    }

    [AvaloniaFact]
    public void Appearance_switches_between_light_and_dark()
    {
        Ui.Open();
        Ui.Shell.SetAppearanceCommand.Execute("Dark");
        Ui.Settle(4);
        Assert.True(ArcTheme.Manager.IsDark);
        Assert.True(Ui.Shell.IsDark);

        Ui.Shell.ToggleThemeCommand.Execute(null);
        Ui.Settle(4);
        Assert.True(Ui.Shell.IsLight);
        Ui.Theme(dark: false);
    }

    [AvaloniaFact]
    public void Language_follows_the_menu_and_is_remembered()
    {
        Ui.Open();
        Ui.Shell.SetLanguageCommand.Execute("zh-CN");
        Ui.Settle(4);
        Assert.Equal("zh-CN", ArcLanguage.Current.Value);
        Assert.True(Ui.Shell.IsSimplifiedChinese);
        Assert.Equal("对话", Ui.Shell.CurrentPage?.Label);
        Assert.Equal("zh-CN", UserSettings.Current.Language);

        Ui.Shell.SetLanguageCommand.Execute("zh-TW");
        Ui.Settle(4);
        Assert.Equal("對話", Ui.Shell.CurrentPage?.Label);
        ArcLanguage.Set("en-US");
    }

    [AvaloniaFact]
    public void The_motion_preference_sets_the_level_and_is_remembered()
    {
        Ui.Open();
        try
        {
            Ui.Shell.SetMotionCommand.Execute("Reduced");
            Assert.Equal(ArcMotionLevel.Reduced, ArcTheme.State.MotionLevel);
            Assert.True(Ui.Shell.IsMotionReduced);

            Ui.Shell.SetMotionCommand.Execute("Off");
            Assert.Equal(ArcMotionLevel.Off, ArcTheme.State.MotionLevel);

            UserSettings.Reset();
            Assert.Equal(MotionPreference.Off, UserSettings.Current.Motion);
            Assert.Equal(ArcMotionLevel.Full, AppMotion.Resolve(MotionPreference.Full));
            Assert.Equal(ArcMotionLevel.Reduced, AppMotion.Resolve(MotionPreference.Reduced));
        }
        finally
        {
            // The other tests run without animations.
            ArcTheme.Apply(t => t.MotionLevel(ArcMotionLevel.Off));
        }
    }

    [AvaloniaFact]
    public void Changing_the_appearance_with_motion_on_still_lands_on_the_new_theme()
    {
        Ui.Open();
        try
        {
            ArcTheme.Apply(t => t.MotionLevel(ArcMotionLevel.Full));
            Ui.Shell.SetAppearanceCommand.Execute("Dark");
            Ui.Settle(40);
            Assert.True(ArcTheme.Manager.IsDark);
        }
        finally
        {
            ArcTheme.Apply(t => t.MotionLevel(ArcMotionLevel.Off));
            Ui.Theme(dark: false);
        }
    }

    [AvaloniaFact]
    public void The_theme_file_and_the_brand_file_are_read_from_data()
    {
        var theme = ThemeFile.Load(Path.Combine(AppContext.BaseDirectory, "arc.theme.json"));
        Assert.NotNull(theme.ParsedAccent);
        Assert.Equal("1.2", theme.Corners);
        Assert.Equal("272", theme.Tokens["Sidebar"]["ExpandedWidth"]);

        var app = AppInfo.Load(Path.Combine(AppContext.BaseDirectory, "app.json"));
        Assert.False(string.IsNullOrWhiteSpace(app.Name));

        // A missing or broken file gives the defaults instead of stopping the app.
        Assert.Equal("TinaClass", AppInfo.Load(Path.Combine(AppContext.BaseDirectory, "missing.json")).Name);
        Assert.Null(ThemeFile.Load(Path.Combine(AppContext.BaseDirectory, "missing.json")).Accent);
    }
}
