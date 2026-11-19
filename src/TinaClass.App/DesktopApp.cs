using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using TinaClass.App.Common.Services;
using TinaClass.App.Common.Settings;
using TinaClass.App.Common.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace TinaClass.App;

/// <summary>
/// The application. Arc needs no App.axaml and no styles here: <see cref="AppSetup.UseDesktopSetup"/> registers the themes.
/// </summary>
public class DesktopApp : Application
{
    /// <summary>The app's services (Common/Services/AppServices.cs), built when the app starts.</summary>
    public static IServiceProvider Services { get; set; } = null!;

    public override void OnFrameworkInitializationCompleted()
    {
        Services ??= AppServices.Build();
        UserSettings.Current.TrackTheme();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = Services.GetRequiredService<ShellViewModel>() };
            Common.Capture.CaptureRun.Current?.Start(desktop.MainWindow);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
