using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Shell;
using Microsoft.Extensions.DependencyInjection;
using TKCode.Arc;

namespace TinaClass.App.Tests;

/// <summary>Helpers that drive the app's window the way a person would.</summary>
public static class Ui
{
    public static ShellViewModel Shell => DesktopApp.Services.GetRequiredService<ShellViewModel>();

    /// <summary>Opens the main window at 1440×900 and lets it lay out.</summary>
    public static MainWindow Open(string language = "en-US") => Open(null, language);

    /// <summary>Opens the window with a given shell (a layout other than the one the app builds), so every layout can be tested whichever one is the default.</summary>
    public static MainWindow Open(ShellBase? shell, string language = "en-US")
    {
        // A fresh set of services and sample data for every window (the headless app may outlive one test).
        TestApp.Prepare(language);
        TKCode.Arc.Localization.ArcLanguage.Set(language);
        // No animations: what the tests see (and the screenshots show) is the settled state.
        ArcTheme.Apply(t => t.MotionLevel(TKCode.Arc.Motion.ArcMotionLevel.Off));
        var window = new MainWindow { Width = 1440, Height = 900 };
        if (shell is not null)
        {
            // The window built its own shell without a view model yet; this one replaces it before anything is shown.
            window.Content = shell;
        }
        window.DataContext = Shell;
        window.Show();
        Settle();
        return window;
    }

    /// <summary>Shows a page and waits for its data.</summary>
    public static void Go(string key)
    {
        Shell.Navigate(key);
        Settle();
    }

    public static T Service<T>() where T : notnull => DesktopApp.Services.GetRequiredService<T>();

    /// <summary>The texts shown by the visible text blocks under <paramref name="root"/>.</summary>
    public static List<string> Texts(Visual root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!)];

    /// <summary>Runs the dispatcher until <paramref name="condition"/> holds (async loads, timers); false when it never did.</summary>
    public static bool Until(Func<bool> condition, int rounds = 120)
    {
        for (var i = 0; i < rounds && !condition(); i++)
        {
            Settle(1);
        }
        return condition();
    }

    public static PageHost Pages(MainWindow window) => window.GetVisualDescendants().OfType<PageHost>().Single();

    public static ShellBase View(MainWindow window) => window.Shell;

    public static Sidebar Nav(MainWindow window) => window.GetVisualDescendants().OfType<Sidebar>().Single();

    /// <summary>Runs the dispatcher until the async loads of the sample services finished.</summary>
    public static void Settle(int rounds = 20)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            // Headless time only moves when told: let animations (chart reveals, transitions) run.
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Thread.Sleep(15);
        }
        Dispatcher.UIThread.RunJobs();
    }

    public static void Theme(bool dark)
    {
        ArcTheme.Apply(t => t.Appearance(dark ? ArcAppearance.Dark : ArcAppearance.Light));
        Settle(4);
    }
}
