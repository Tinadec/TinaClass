using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Settings;
using TinaClass.App.Common.Shell;
using Microsoft.Extensions.DependencyInjection;
using TKCode.Arc;
using TKCode.Arc.Motion;

namespace TinaClass.App.Common.Capture;

/// <summary>
/// Capture mode: with <c>TINACLASS_CAPTURE=&lt;folder&gt;</c> the app starts on the sample data, walks through every page in
/// the real window, in light and in dark, and writes each one to <c>&lt;folder&gt;/&lt;page&gt;.png</c> and
/// <c>&lt;page&gt;-dark.png</c>, then quits. For reviewing the UI on a real platform (fonts, metrics). Options:
/// <c>TINACLASS_CAPTURE_SIZE=1440x900</c>, <c>TINACLASS_CAPTURE_MOTION=Full|Reduced|Off</c>, <c>TINACLASS_CAPTURE_LANGUAGE=zh-CN</c>,
/// <c>TINACLASS_CAPTURE_PART=motion</c> (frames in the middle of the sidebar, page, menu and theme animations, with the
/// motion level a person gets by default) or <c>menus</c> (the preferences menu and its submenus). Open menus are drawn
/// onto the window picture at their place on screen.
/// </summary>
public sealed class CaptureRun
{
    private readonly string _folder;
    private readonly int _width;
    private readonly int _height;
    private readonly string _part;
    private Window _window = null!;

    private CaptureRun(string folder, int width, int height, string part)
    {
        _folder = folder;
        _width = width;
        _height = height;
        _part = part;
    }

    /// <summary>The capture asked for by the environment, or null for a normal start.</summary>
    public static CaptureRun? Current { get; private set; }

    /// <summary>Reads the environment before the app is built: settings in memory, the language.</summary>
    public static void PrepareFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("TINACLASS_CAPTURE") is not { Length: > 0 } folder)
        {
            return;
        }
        var size = (Environment.GetEnvironmentVariable("TINACLASS_CAPTURE_SIZE") ?? "1440x900").Split('x');
        var part = Environment.GetEnvironmentVariable("TINACLASS_CAPTURE_PART") is { Length: > 0 } p ? p : "all";
        Current = new CaptureRun(folder, int.Parse(size[0]), int.Parse(size[1]), part);
        Directory.CreateDirectory(folder);
        UserSettings.Store = new MemorySettingsStore();
        UserSettings.Current.Language = Environment.GetEnvironmentVariable("TINACLASS_CAPTURE_LANGUAGE") is { Length: > 0 } language ? language : "en-US";
        UserSettings.Current.Appearance = ArcAppearance.Light;
        // TINACLASS_CAPTURE_MOTION=Full|Reduced|Off picks the motion level of the motion frames (default: System).
        if (Enum.TryParse<Theme.MotionPreference>(Environment.GetEnvironmentVariable("TINACLASS_CAPTURE_MOTION"), true, out var preference))
        {
            UserSettings.Current.Motion = preference;
        }
    }

    /// <summary>Starts the walk once the main window is shown.</summary>
    public void Start(Window window)
    {
        _window = window;
        window.Width = _width;
        window.Height = _height;
        window.Opened += async (_, _) =>
        {
            // The motion frames use the level a person gets by default (preferences menu → Motion: System); the pictures of
            // the pages themselves are taken at rest.
            var motion = _part == "motion" ? Theme.AppMotion.Resolve(UserSettings.Current.Motion) : ArcMotionLevel.Off;
            if (_part == "motion")
            {
                File.WriteAllText(Path.Combine(_folder, "motion-level.txt"),
                    $"{UserSettings.Current.Motion} -> {motion}; system reduce motion: {Theme.AppMotion.SystemPrefersReducedMotion()}");
            }
            ArcTheme.Apply(t => t.MotionLevel(motion).Appearance(ArcAppearance.Light));
            try
            {
                // The pictures are exactly the asked size: grow the window by what its frame takes.
                await Wait(200);
                _window.Width += _width - _window.ClientSize.Width;
                _window.Height += _height - _window.ClientSize.Height;
                await Wait(300);
                await RunAsync();
            }
            catch (Exception exception)
            {
                await File.WriteAllTextAsync(Path.Combine(_folder, "error.txt"), exception.ToString());
            }
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };
    }

    private static ShellViewModel Shell => DesktopApp.Services.GetRequiredService<ShellViewModel>();

    private ShellBase View => ((MainWindow)_window).Shell;

    private async Task RunAsync()
    {
        Shell.IsSidebarCollapsed = true;
        await Wait(800);
        switch (_part)
        {
            case "motion":
                await MotionAsync();
                return;
            case "menus":
                await MenusAsync();
                return;
        }

        // Every page, light then dark (the appearance is changed without the crossfade: the picture is of the settled page).
        foreach (var dark in new[] { false, true })
        {
            ArcTheme.Apply(t => t.Appearance(dark ? ArcAppearance.Dark : ArcAppearance.Light));
            await Wait(500);
            foreach (var page in AppPages.All)
            {
                Shell.Navigate(page.Key);
                await Wait(900);
                Save(page.Key + (dark ? "-dark" : string.Empty));
            }
        }
        ArcTheme.Apply(t => t.Appearance(ArcAppearance.Light));
        Shell.IsSidebarCollapsed = false;
        await Wait(700);
        foreach (var page in AppPages.All)
        {
            Shell.Navigate(page.Key);
            await Wait(700);
            Save(page.Key + "-expanded");
        }
    }

    /// <summary>The preferences menu and its submenus, with the sidebar expanded and as a rail.</summary>
    private async Task MenusAsync()
    {
        var view = View;
        Shell.IsSidebarCollapsed = false;
        await Wait(600);
        view.OpenPreferences();
        await Wait(500);
        Save("menu-preferences");
        // Submenus open from the keyboard, as a person would: down to the row, then ArrowRight.
        foreach (var (row, downs) in new[] { ("appearance", 1), ("language", 2), ("motion", 3) })
        {
            view.ClosePreferences();
            await Wait(300);
            view.OpenPreferences(MenuOpenFocus.First);
            await Wait(400);
            for (var i = 0; i < downs - 1; i++)
            {
                Press(Avalonia.Input.Key.Down);
                await Wait(60);
            }
            Press(Avalonia.Input.Key.Right);
            await Wait(500);
            Save("menu-" + row);
        }
        view.ClosePreferences();
        Shell.IsSidebarCollapsed = true;
        await Wait(500);
        view.OpenPreferences();
        await Wait(500);
        Save("menu-rail");
        view.ClosePreferences();
        await Wait(300);
    }

    /// <summary>
    /// Frames taken while the main animations run (the motion level of <c>motion-level.txt</c>): the sidebar expanding and
    /// folding, the preferences menu opening, a page change (when the template has a second page) and the light → dark crossfade.
    /// </summary>
    private async Task MotionAsync()
    {
        var view = View;
        Shell.Navigate(AppPages.Chat);
        await Wait(900);
        await Frames("motion-rail-expand", () => Shell.IsSidebarCollapsed = false, 40, 90, 150, 230, 500);
        await Frames("motion-rail-collapse", () => Shell.IsSidebarCollapsed = true, 40, 90, 150, 230, 500);
        await Frames("motion-menu", () => view.OpenPreferences(), 16, 50, 100, 160, 400);
        view.ClosePreferences();
        await Wait(400);
        if (AppPages.All.Count > 1)
        {
            await Frames("motion-page", () => Shell.Navigate(AppPages.Models), 16, 40, 70, 110, 160, 230, 400);
            await Wait(600);
            // Back to the first page, whose view already exists: the swap alone, without the first load.
            await Frames("motion-page-back", () => Shell.Navigate(AppPages.Chat), 16, 40, 70, 110, 160, 230, 400);
            await Wait(600);
        }
        await Frames("motion-theme", () => Shell.SetAppearanceCommand.Execute("Dark"), 16, 70, 130, 200, 400);
    }

    /// <summary>Starts an action and captures the window after each delay (ms since the start).</summary>
    private async Task Frames(string name, Action start, params int[] at)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        start();
        foreach (var ms in at)
        {
            var left = ms - (int)watch.ElapsedMilliseconds;
            if (left > 0)
            {
                await Task.Delay(left);
            }
            Save($"{name}-{watch.ElapsedMilliseconds:000}ms");
        }
        await Wait(300);
    }

    /// <summary>Writes the window, with any open menu drawn over it at its place on screen, to <c>&lt;name&gt;.png</c>.</summary>
    private void Save(string name)
    {
        var client = _window.ClientSize;
        var size = new PixelSize((int)client.Width, (int)client.Height);
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(_window);
        var popups = OpenPopups().ToList();
        if (popups.Count == 0)
        {
            bitmap.Save(Path.Combine(_folder, name + ".png"), new PngBitmapEncoderOptions());
            return;
        }
        using var composed = new RenderTargetBitmap(size, new Vector(96, 96));
        var parts = new List<RenderTargetBitmap>();
        using (var context = composed.CreateDrawingContext())
        {
            context.DrawImage(bitmap, new Rect(client));
            foreach (var popup in popups)
            {
                var popupSize = popup.ClientSize;
                var part = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)popupSize.Width), Math.Max(1, (int)popupSize.Height)), new Vector(96, 96));
                part.Render(popup);
                parts.Add(part);
                var origin = popup.PointToScreen(default);
                var windowOrigin = _window.PointToScreen(default);
                var scale = _window.RenderScaling;
                var offset = new Point((origin.X - windowOrigin.X) / scale, (origin.Y - windowOrigin.Y) / scale);
                context.DrawImage(part, new Rect(offset, popupSize));
            }
        }
        composed.Save(Path.Combine(_folder, name + ".png"), new PngBitmapEncoderOptions());
        foreach (var part in parts)
        {
            part.Dispose();
        }
    }

    /// <summary>Sends a key to the focused element (inside an open menu, its row).</summary>
    private void Press(Avalonia.Input.Key key)
    {
        var top = TopLevel.GetTopLevel(View.Preferences) ?? _window;
        var target = top.FocusManager?.GetFocusedElement() as Avalonia.Interactivity.Interactive ?? View.Preferences;
        target.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = key });
    }

    /// <summary>The open menus (the preferences menu and an open submenu): their own top levels.</summary>
    private IEnumerable<TopLevel> OpenPopups()
    {
        var seen = new HashSet<TopLevel>();
        var rows = View.Preferences.Items.OfType<MenuEntry>().SelectMany(row => row.HasSubmenu ? new[] { row }.Concat(row.Items.OfType<MenuEntry>()) : [row]);
        foreach (var item in rows)
        {
            if (TopLevel.GetTopLevel(item) is { } top && !ReferenceEquals(top, _window) && top.IsVisible && seen.Add(top))
            {
                yield return top;
            }
        }
    }

    private static async Task Wait(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
