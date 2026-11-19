using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using TinaClass.App;
using TinaClass.App.Common;
using TinaClass.App.Common.Sample;
using TinaClass.App.Common.Services;
using TinaClass.App.Common.Settings;
using TinaClass.App.Common.Theme;
using TinaClass.App.Features.Chat;
using TinaClass.App.Features.Knowledge;
using TinaClass.App.Features.Agents;
using TinaClass.App.Features.Flow;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Every test gets a fresh app on the headless platform, one after another (Avalonia has one UI thread).
[assembly: AvaloniaTestApplication(typeof(TinaClass.App.Tests.TestApp))]
[assembly: AvaloniaTestFramework]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TinaClass.App.Tests;

/// <summary>
/// The app as the tests see it: the sample data, settings in memory, "now" pinned to a Friday morning. Rendering is real
/// (Skia), so screenshots look like the desktop app.
/// </summary>
public static class TestApp
{
    /// <summary>The pinned "now": Friday 13 November 2026, 10:42.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 42, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 13)));

    public static AppBuilder BuildAvaloniaApp()
    {
        Prepare();
        return AppBuilder.Configure<DesktopApp>()
                         .UseSkia()
                         .UseDesktopSetup()
                         .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>Resets every static the app keeps, so each test starts from the same state.</summary>
    public static void Prepare(string language = "en-US")
    {
        UserSettings.Store = new MemorySettingsStore();
        UserSettings.Reset();
        UserSettings.Current.Language = language;
        AppInfo.Current = new AppInfo { Name = "TinaClass", Version = "1.0.0" };
        ThemeFile.Current = ThemeFile.Load(Path.Combine(AppContext.BaseDirectory, "arc.theme.json"));
        SampleClock.NowProvider = () => Now;
        // The sample services answer at once and stand still, so a test sees the settled state.
        SampleChatService.Speed = 0;
        SampleChatService.HoldAt = null;
        SampleKnowledgeService.AutoAdvance = false;
        SampleAgentsService.AutoAdvance = false;
        SampleFlowService.AutoAdvance = false;
        DesktopApp.Services = AppServices.Build();
    }
}
