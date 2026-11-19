using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using TinaClass.App.Common.Theme;
using TKCode.Arc;

namespace TinaClass.App.Common.Settings;

/// <summary>
/// What this person chose on this computer, kept between runs in <c>settings.json</c> under the user's application data
/// folder: the appearance (when it differs from arc.theme.json), the language, how much the app moves and whether the
/// sidebar is folded. Never secrets.
/// </summary>
public sealed class UserSettings
{
    private static UserSettings? s_current;
    private ISettingsStore _store = null!;

    public static UserSettings Current => s_current ??= Load(Store);

    /// <summary>The desktop settings file: <c>&lt;application data&gt;/tinaclass/settings.json</c>.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "tinaclass",
        "settings.json");

    /// <summary>Where the settings live; tests set a <see cref="MemorySettingsStore"/> before the app starts.</summary>
    public static ISettingsStore Store { get; set; } = new FileSettingsStore(DefaultPath);

    /// <summary>The appearance chosen in the app, or null to keep arc.theme.json's.</summary>
    public ArcAppearance? Appearance { get; set; }

    /// <summary>How much the app moves (preferences menu); <see cref="MotionPreference.System"/> follows the system.</summary>
    public MotionPreference Motion { get; set; }

    /// <summary>The language chosen in the app (<c>zh-CN</c>), or null for app.json's or the system's.</summary>
    public string? Language { get; set; }

    /// <summary>Whether the sidebar is folded to its icon rail.</summary>
    public bool IsSidebarCollapsed { get; set; }

    public WindowPlacement? Window { get; set; }

    public static UserSettings Load(ISettingsStore store)
    {
        UserSettings? settings = null;
        try
        {
            if (store.Read() is { Length: > 0 } json)
            {
                settings = JsonSerializer.Deserialize(json, UserSettingsJsonContext.Default.UserSettings);
            }
        }
        catch (JsonException)
        {
            // Damaged settings must not stop the app: start from the defaults.
        }
        settings ??= new UserSettings();
        settings._store = store;
        return settings;
    }

    /// <summary>Forgets the settings of this run, so the next <see cref="Current"/> reads <see cref="Store"/> again (tests).</summary>
    public static void Reset() => s_current = null;

    public void Save()
    {
        if (Design.IsDesignMode)
        {
            return;
        }
        _store.Write(JsonSerializer.Serialize(this, UserSettingsJsonContext.Default.UserSettings));
    }

    /// <summary>The start-up appearance, when the person chose one in the app, and the motion level.</summary>
    public void ApplyAppearance(ArcThemeBuilder theme)
    {
        if (Appearance is { } appearance)
        {
            theme.Appearance(appearance);
        }
        theme.MotionLevel(AppMotion.Resolve(Motion));
    }

    /// <summary>Remembers the appearance whenever it changes, from anywhere in the app.</summary>
    public void TrackTheme()
    {
        ArcTheme.Manager.StateChanged += (_, e) =>
        {
            if (e.NewState.Appearance == e.OldState.Appearance || e.NewState.Appearance == Appearance)
            {
                return;
            }
            Appearance = e.NewState.Appearance;
            Save();
        };
    }

    /// <summary>Puts the window where it was last closed and saves its placement when it closes again.</summary>
    public void TrackWindow(Window window)
    {
        Window?.Restore(window);
        window.Closing += (_, _) =>
        {
            Window = WindowPlacement.Capture(window, Window);
            Save();
        };
    }
}

/// <summary>A window's size, position and maximized state.</summary>
public sealed class WindowPlacement
{
    /// <summary>The left edge, in screen pixels.</summary>
    public int X { get; set; }

    /// <summary>The top edge, in screen pixels.</summary>
    public int Y { get; set; }

    /// <summary>The client width, in device-independent pixels.</summary>
    public double Width { get; set; }

    /// <summary>The client height, in device-independent pixels.</summary>
    public double Height { get; set; }

    /// <summary>Whether the window was maximized.</summary>
    public bool IsMaximized { get; set; }

    /// <summary>
    /// Reads the placement of <paramref name="window"/>. A maximized window keeps the size and position it had before
    /// (<paramref name="previous"/>), so it un-maximizes to them next time.
    /// </summary>
    public static WindowPlacement Capture(Window window, WindowPlacement? previous)
    {
        var maximized = window.WindowState == WindowState.Maximized;
        if (maximized && previous is not null)
        {
            return new WindowPlacement
            {
                X = previous.X, Y = previous.Y, Width = previous.Width, Height = previous.Height, IsMaximized = true
            };
        }
        return new WindowPlacement
        {
            X = window.Position.X,
            Y = window.Position.Y,
            Width = window.ClientSize.Width,
            Height = window.ClientSize.Height,
            IsMaximized = maximized
        };
    }

    /// <summary>Applies the placement before the window is shown; a position on a screen that is gone is skipped.</summary>
    public void Restore(Window window)
    {
        if (Width >= window.MinWidth && Height >= window.MinHeight)
        {
            window.Width = Width;
            window.Height = Height;
        }
        var position = new Avalonia.PixelPoint(X, Y);
        foreach (var screen in window.Screens.All)
        {
            if (screen.WorkingArea.Contains(position))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = position;
                break;
            }
        }
        if (IsMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(UserSettings))]
internal sealed partial class UserSettingsJsonContext : JsonSerializerContext;
