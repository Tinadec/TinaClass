using Avalonia;

namespace TinaClass.App;

internal static class Program
{
    // Avalonia is not ready until Main runs: do not use any Avalonia or Arc API before BuildAvaloniaApp().
    [STAThread]
    public static void Main(string[] args)
    {
        // TINACLASS_CAPTURE=<folder>: walk every page on the sample data and save each as a PNG (Common/Capture).
        Common.Capture.CaptureRun.PrepareFromEnvironment();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the XAML previewer of your IDE.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DesktopApp>()
                  .UsePlatformDetect()
                  .UseDesktopSetup()
                  .LogToTrace();
}
