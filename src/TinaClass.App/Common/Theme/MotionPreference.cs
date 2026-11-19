using System.Runtime.InteropServices;
using TKCode.Arc;
using TKCode.Arc.Motion;

namespace TinaClass.App.Common.Theme;

/// <summary>How much the app moves, chosen in the preferences menu (kept in <c>settings.json</c>).</summary>
public enum MotionPreference
{
    /// <summary>Full motion, unless the operating system asks for reduced motion.</summary>
    System,

    /// <summary>Springs, travel and fades.</summary>
    Full,

    /// <summary>Fades and color changes only: nothing travels, grows or bounces.</summary>
    Reduced,

    /// <summary>No animation at all.</summary>
    Off
}

/// <summary>
/// Turns a <see cref="MotionPreference"/> into Arc's motion level and applies it to the theme. Every Arc control, and the
/// app's own motion (page swap, inspector fade, theme crossfade, collapsibles), follows <c>ArcMotion</c>'s effective level,
/// so this one switch covers the whole app.
/// </summary>
public static class AppMotion
{
    /// <summary>The level for a preference; <see cref="MotionPreference.System"/> asks the operating system.</summary>
    public static ArcMotionLevel Resolve(MotionPreference preference) => preference switch
    {
        MotionPreference.Full => ArcMotionLevel.Full,
        MotionPreference.Reduced => ArcMotionLevel.Reduced,
        MotionPreference.Off => ArcMotionLevel.Off,
        _ => SystemPrefersReducedMotion() ? ArcMotionLevel.Reduced : ArcMotionLevel.Full
    };

    /// <summary>Applies a preference to the running app.</summary>
    public static void Apply(MotionPreference preference)
    {
        var level = Resolve(preference);
        if (ArcTheme.State.MotionLevel != level)
        {
            ArcTheme.Apply(t => t.MotionLevel(level));
        }
    }

    /// <summary>
    /// True when the system's accessibility settings ask for less motion: macOS "Reduce motion", Windows "Show animations
    /// in Windows" turned off, or GNOME's animations turned off (<c>GTK_ENABLE_ANIMATIONS=0</c> elsewhere on Linux).
    /// </summary>
    public static bool SystemPrefersReducedMotion()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return MacReduceMotion();
            }
            if (OperatingSystem.IsWindows())
            {
                return WindowsReduceMotion();
            }
            if (OperatingSystem.IsLinux())
            {
                return Environment.GetEnvironmentVariable("GTK_ENABLE_ANIMATIONS") == "0";
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // No answer from the system: full motion.
        }
        return false;
    }

    // macOS: [[NSWorkspace sharedWorkspace] accessibilityDisplayShouldReduceMotion] through the Objective-C runtime.
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern byte SendBool(IntPtr receiver, IntPtr selector);

    private static bool MacReduceMotion()
    {
        var workspaceClass = GetClass("NSWorkspace");
        if (workspaceClass == IntPtr.Zero)
        {
            return false;
        }
        var workspace = SendPointer(workspaceClass, Selector("sharedWorkspace"));
        return workspace != IntPtr.Zero && SendBool(workspace, Selector("accessibilityDisplayShouldReduceMotion")) != 0;
    }

    // Windows: SystemParametersInfo(SPI_GETCLIENTAREAANIMATION) is false when animations are turned off.
    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern int SystemParametersInfo(uint action, uint param, out int value, uint winIni);

    private static bool WindowsReduceMotion() =>
        SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var enabled, 0) != 0 && enabled == 0;
}
